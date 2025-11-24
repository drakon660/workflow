using AwesomeAssertions;
using Workflow.Samples.Order;

namespace Workflow.Tests;

public class OrderProcessingWorkflowTests
{
    [Fact]
    public void Check_OrderProcessingWorkflow()
    {
        var workflow = new OrderProcessingWorkflow();
        var workflowOrchestrator = new WorkflowOrchestrator<OrderProcessingInputMessage, OrderProcessingState, OrderProcessingOutputMessage>();
        var snapshot = workflowOrchestrator.CreateInitialSnapshot(workflow);

        var orderId = "order-1";

        var result = workflowOrchestrator.Run(workflow, snapshot, new PlaceOrderInputMessage(orderId), true);

        result.Events.Count.Should().Be(5);  // ✅ Correct!

        // Check state changed
        result.Snapshot.State.Should().BeOfType<OrderCreated>();  // ✅
        var orderCreatedState = (OrderCreated)result.Snapshot.State;
        orderCreatedState.OrderId.Should().Be(orderId);  // ✅

        // Check commands generated
        result.Commands.Count.Should().Be(3);
    }

    [Fact]
    public void CheckOrderState_WhenOrderCreated_ShouldReplyWithStatus()
    {
        // Arrange
        var workflow = new OrderProcessingWorkflow();
        var state = new OrderCreated("order-123");
        var input = new CheckOrderStateInputMessage("order-123");

        // Act
        var commands = workflow.Decide(input, state);

        // Assert
        commands.Should().HaveCount(1);
        var reply = commands[0].Should().BeOfType<Reply<OrderProcessingOutputMessage>>().Subject;
        var status = reply.Message.Should().BeOfType<OrderProcessingStatus>().Subject;
        status.OrderId.Should().Be("order-123");
        status.Status.Should().Be("OrderCreated");
    }
}

public class OrderProcessingAsyncWorkflowTests
{
    // Mock context for testing
    private class MockOrderContext : IOrderContext
    {
        private readonly int _inventoryCount;

        public MockOrderContext(int inventoryCount = 100)
        {
            _inventoryCount = inventoryCount;
        }

        public Task<int> GetInventoryCountAsync()
        {
            return Task.FromResult(_inventoryCount);
        }

        public Task<bool> CheckInventoryAsync(string orderId, int quantity)
        {
            return Task.FromResult(_inventoryCount >= quantity);
        }
    }

    [Fact]
    public async Task PlaceOrder_ShouldCallServiceAndGenerateCommands()
    {
        // Arrange
        var workflow = new OrderProcessingAsyncWorkflow();
        var orchestrator = new AsyncWorkflowOrchestrator<OrderProcessingInputMessage, OrderProcessingState, OrderProcessingOutputMessage, IOrderContext>();
        var snapshot = orchestrator.CreateInitialSnapshot(workflow);
        var context = new MockOrderContext(inventoryCount: 50);
        var orderId = "order-async-1";

        // Act
        var result = await orchestrator.RunAsync(workflow, snapshot, new PlaceOrderInputMessage(orderId), context, begins: true);

        // Assert
        result.Events.Count.Should().Be(5);  // Began, InitiatedBy, Sent x3
        result.Snapshot.State.Should().BeOfType<OrderCreated>();

        var orderCreatedState = (OrderCreated)result.Snapshot.State;
        orderCreatedState.OrderId.Should().Be(orderId);

        // Check commands generated
        result.Commands.Count.Should().Be(3);
        result.Commands[0].Should().BeOfType<Send<OrderProcessingOutputMessage>>();
        result.Commands[1].Should().BeOfType<Send<OrderProcessingOutputMessage>>();
        result.Commands[2].Should().BeOfType<Schedule<OrderProcessingOutputMessage>>();
    }

    [Fact]
    public async Task PaymentReceived_ShouldGenerateShipOrderCommand()
    {
        // Arrange
        var workflow = new OrderProcessingAsyncWorkflow();
        var orchestrator = new AsyncWorkflowOrchestrator<OrderProcessingInputMessage, OrderProcessingState, OrderProcessingOutputMessage, IOrderContext>();
        var snapshot = new WorkflowSnapshot<OrderProcessingInputMessage, OrderProcessingState, OrderProcessingOutputMessage>(
            State: new OrderCreated("order-123"),
            EventHistory: []
        );
        var context = new MockOrderContext();

        // Act
        var result = await orchestrator.RunAsync(workflow, snapshot, new PaymentReceivedInputMessage("order-123"), context);

        // Assert
        result.Snapshot.State.Should().BeOfType<PaymentConfirmed>();
        result.Commands.Should().HaveCount(1);

        var sendCommand = result.Commands[0].Should().BeOfType<Send<OrderProcessingOutputMessage>>().Subject;
        sendCommand.Message.Should().BeOfType<ShipOrder>();
    }

    [Fact]
    public async Task OrderShipped_ShouldGenerateNotificationCommand()
    {
        // Arrange
        var workflow = new OrderProcessingAsyncWorkflow();
        var orchestrator = new AsyncWorkflowOrchestrator<OrderProcessingInputMessage, OrderProcessingState, OrderProcessingOutputMessage, IOrderContext>();
        var snapshot = new WorkflowSnapshot<OrderProcessingInputMessage, OrderProcessingState, OrderProcessingOutputMessage>(
            State: new PaymentConfirmed("order-123"),
            EventHistory: []
        );
        var context = new MockOrderContext();

        // Act
        var result = await orchestrator.RunAsync(workflow, snapshot, new OrderShippedInputMessage("order-123", "TRACK-123"), context);

        // Assert
        result.Snapshot.State.Should().BeOfType<Shipped>();
        var shippedState = (Shipped)result.Snapshot.State;
        shippedState.TrackingNumber.Should().Be("TRACK-123");

        result.Commands.Should().HaveCount(1);
        var sendCommand = result.Commands[0].Should().BeOfType<Send<OrderProcessingOutputMessage>>().Subject;
        var notification = sendCommand.Message.Should().BeOfType<NotifyOrderShipped>().Subject;
        notification.TrackingNumber.Should().Be("TRACK-123");
    }

    [Fact]
    public async Task OrderDelivered_ShouldCompleteWorkflow()
    {
        // Arrange
        var workflow = new OrderProcessingAsyncWorkflow();
        var orchestrator = new AsyncWorkflowOrchestrator<OrderProcessingInputMessage, OrderProcessingState, OrderProcessingOutputMessage, IOrderContext>();
        var snapshot = new WorkflowSnapshot<OrderProcessingInputMessage, OrderProcessingState, OrderProcessingOutputMessage>(
            State: new Shipped("order-123", "TRACK-123"),
            EventHistory: []
        );
        var context = new MockOrderContext();

        // Act
        var result = await orchestrator.RunAsync(workflow, snapshot, new OrderDeliveredInputMessage("order-123"), context);

        // Assert
        result.Snapshot.State.Should().BeOfType<Delivered>();
        result.Commands.Should().HaveCount(2);
        result.Commands[0].Should().BeOfType<Send<OrderProcessingOutputMessage>>();
        result.Commands[1].Should().BeOfType<Complete<OrderProcessingOutputMessage>>();
    }

    [Fact]
    public async Task OrderCancelled_ShouldCompleteWorkflow()
    {
        // Arrange
        var workflow = new OrderProcessingAsyncWorkflow();
        var orchestrator = new AsyncWorkflowOrchestrator<OrderProcessingInputMessage, OrderProcessingState, OrderProcessingOutputMessage, IOrderContext>();
        var snapshot = new WorkflowSnapshot<OrderProcessingInputMessage, OrderProcessingState, OrderProcessingOutputMessage>(
            State: new OrderCreated("order-123"),
            EventHistory: []
        );
        var context = new MockOrderContext();

        // Act
        var result = await orchestrator.RunAsync(workflow, snapshot, new OrderCancelledInputMessage("order-123", "Customer request"), context);

        // Assert
        result.Snapshot.State.Should().BeOfType<Cancelled>();
        var cancelledState = (Cancelled)result.Snapshot.State;
        cancelledState.Reason.Should().Be("Customer request");

        result.Commands.Should().HaveCount(2);
        result.Commands[0].Should().BeOfType<Send<OrderProcessingOutputMessage>>();
        result.Commands[1].Should().BeOfType<Complete<OrderProcessingOutputMessage>>();
    }

    [Fact]
    public async Task PaymentTimeout_ShouldCancelAndCompleteWorkflow()
    {
        // Arrange
        var workflow = new OrderProcessingAsyncWorkflow();
        var orchestrator = new AsyncWorkflowOrchestrator<OrderProcessingInputMessage, OrderProcessingState, OrderProcessingOutputMessage, IOrderContext>();
        var snapshot = new WorkflowSnapshot<OrderProcessingInputMessage, OrderProcessingState, OrderProcessingOutputMessage>(
            State: new OrderCreated("order-123"),
            EventHistory: []
        );
        var context = new MockOrderContext();

        // Act
        var result = await orchestrator.RunAsync(workflow, snapshot, new PaymentTimeoutInputMessage("order-123"), context);

        // Assert
        result.Snapshot.State.Should().BeOfType<Cancelled>();
        var cancelledState = (Cancelled)result.Snapshot.State;
        cancelledState.Reason.Should().Be("Payment_Timeout");

        result.Commands.Should().HaveCount(2);
        var notification = result.Commands[0].Should().BeOfType<Send<OrderProcessingOutputMessage>>().Subject
            .Message.Should().BeOfType<NotifyOrderCancelled>().Subject;
        notification.Reason.Should().Be("Payment_Timeout");
    }

    [Fact]
    public async Task CheckOrderState_ShouldReplyWithStatus()
    {
        // Arrange
        var workflow = new OrderProcessingAsyncWorkflow();
        var orchestrator = new AsyncWorkflowOrchestrator<OrderProcessingInputMessage, OrderProcessingState, OrderProcessingOutputMessage, IOrderContext>();
        var snapshot = new WorkflowSnapshot<OrderProcessingInputMessage, OrderProcessingState, OrderProcessingOutputMessage>(
            State: new PaymentConfirmed("order-123"),
            EventHistory: []
        );
        var context = new MockOrderContext();

        // Act
        var result = await orchestrator.RunAsync(workflow, snapshot, new CheckOrderStateInputMessage("order-123"), context);

        // Assert
        result.Snapshot.State.Should().BeOfType<PaymentConfirmed>();  // State should not change
        result.Commands.Should().HaveCount(1);

        var reply = result.Commands[0].Should().BeOfType<Reply<OrderProcessingOutputMessage>>().Subject;
        var status = reply.Message.Should().BeOfType<OrderProcessingStatus>().Subject;
        status.OrderId.Should().Be("order-123");
        status.Status.Should().Be("PaymentConfirmed");
    }

    [Fact]
    public async Task FullWorkflow_HappyPath_ShouldCompleteSuccessfully()
    {
        // Arrange
        var workflow = new OrderProcessingAsyncWorkflow();
        var orchestrator = new AsyncWorkflowOrchestrator<OrderProcessingInputMessage, OrderProcessingState, OrderProcessingOutputMessage, IOrderContext>();
        var context = new MockOrderContext(inventoryCount: 75);
        var orderId = "order-full-test";

        // Step 1: Place order
        var snapshot = orchestrator.CreateInitialSnapshot(workflow);
        var result1 = await orchestrator.RunAsync(workflow, snapshot, new PlaceOrderInputMessage(orderId), context, begins: true);
        result1.Snapshot.State.Should().BeOfType<OrderCreated>();

        // Step 2: Payment received
        var result2 = await orchestrator.RunAsync(workflow, result1.Snapshot, new PaymentReceivedInputMessage(orderId), context);
        result2.Snapshot.State.Should().BeOfType<PaymentConfirmed>();

        // Step 3: Order shipped
        var result3 = await orchestrator.RunAsync(workflow, result2.Snapshot, new OrderShippedInputMessage(orderId, "TRACK-999"), context);
        result3.Snapshot.State.Should().BeOfType<Shipped>();

        // Step 4: Order delivered
        var result4 = await orchestrator.RunAsync(workflow, result3.Snapshot, new OrderDeliveredInputMessage(orderId), context);
        result4.Snapshot.State.Should().BeOfType<Delivered>();

        // Verify completion command was issued
        result4.Commands.Should().Contain(c => c is Complete<OrderProcessingOutputMessage>);
    }

    [Fact]
    public async Task PlaceOrder_WithInsufficientInventory_ShouldRequestWarehouseInventory()
    {
        // Arrange
        var workflow = new OrderProcessingAsyncWorkflow();
        var orchestrator = new AsyncWorkflowOrchestrator<OrderProcessingInputMessage, OrderProcessingState, OrderProcessingOutputMessage, IOrderContext>();
        var context = new MockOrderContext(inventoryCount: 0); // No local inventory available
        var orderId = "order-no-inventory";

        // Act - Step 1: Place order with insufficient local inventory
        var snapshot = orchestrator.CreateInitialSnapshot(workflow);
        var result = await orchestrator.RunAsync(workflow, snapshot, new PlaceOrderInputMessage(orderId), context, begins: true);

        // Assert - State transitions to OrderCreated (order initiated)
        result.Snapshot.State.Should().BeOfType<OrderCreated>();
        result.Commands.Should().HaveCount(1);

        // Verify insufficient inventory notification sent
        var sendCommand = result.Commands[0].Should().BeOfType<Send<OrderProcessingOutputMessage>>().Subject;
        sendCommand.Message.Should().BeOfType<NotifyInsufficientInventory>();

        // Act - Step 2: External system sends InsufficientInventoryInputMessage to trigger warehouse request
        var result2 = await orchestrator.RunAsync(workflow, result.Snapshot, new InsufficientInventoryInputMessage(orderId), context);

        // Assert - State transitions to AwaitingWarehouseInventory
        result2.Snapshot.State.Should().BeOfType<AwaitingWarehouseInventory>();
        result2.Commands.Should().HaveCount(1);

        // Verify warehouse inventory request sent
        var warehouseRequestCommand = result2.Commands[0].Should().BeOfType<Send<OrderProcessingOutputMessage>>().Subject;
        var warehouseRequest = warehouseRequestCommand.Message.Should().BeOfType<RequestInventoryFromWarehouse>().Subject;
        warehouseRequest.OrderId.Should().Be(orderId);
        warehouseRequest.Quantity.Should().Be(1);
    }

    [Fact]
    public async Task AwaitingWarehouseInventory_WhenReceived_ShouldProceedWithPayment()
    {
        // Arrange
        var workflow = new OrderProcessingAsyncWorkflow();
        var orchestrator = new AsyncWorkflowOrchestrator<OrderProcessingInputMessage, OrderProcessingState, OrderProcessingOutputMessage, IOrderContext>();
        var context = new MockOrderContext();
        var orderId = "order-warehouse-success";

        // Start with AwaitingWarehouseInventory state
        var snapshot = new WorkflowSnapshot<OrderProcessingInputMessage, OrderProcessingState, OrderProcessingOutputMessage>(
            State: new AwaitingWarehouseInventory(orderId),
            EventHistory: []
        );

        // Act - Warehouse inventory received
        var result = await orchestrator.RunAsync(workflow, snapshot, new WarehouseInventoryReceivedInputMessage(orderId), context);

        // Assert - State transitions to PaymentConfirmed
        result.Snapshot.State.Should().BeOfType<PaymentConfirmed>();

        // Verify payment and notification commands
        result.Commands.Should().HaveCount(3);
        result.Commands[0].Should().BeOfType<Send<OrderProcessingOutputMessage>>()
            .Which.Message.Should().BeOfType<ProcessPayment>();
        result.Commands[1].Should().BeOfType<Send<OrderProcessingOutputMessage>>()
            .Which.Message.Should().BeOfType<NotifyOrderPlaced>();
        result.Commands[2].Should().BeOfType<Schedule<OrderProcessingOutputMessage>>();
    }

    [Fact]
    public async Task AwaitingWarehouseInventory_WhenUnavailable_ShouldCancelOrder()
    {
        // Arrange
        var workflow = new OrderProcessingAsyncWorkflow();
        var orchestrator = new AsyncWorkflowOrchestrator<OrderProcessingInputMessage, OrderProcessingState, OrderProcessingOutputMessage, IOrderContext>();
        var context = new MockOrderContext();
        var orderId = "order-warehouse-unavailable";

        // Start with AwaitingWarehouseInventory state
        var snapshot = new WorkflowSnapshot<OrderProcessingInputMessage, OrderProcessingState, OrderProcessingOutputMessage>(
            State: new AwaitingWarehouseInventory(orderId),
            EventHistory: []
        );

        // Act - Warehouse inventory unavailable
        var result = await orchestrator.RunAsync(workflow, snapshot, new WarehouseInventoryUnavailableInputMessage(orderId), context);

        // Assert - State transitions to Cancelled
        result.Snapshot.State.Should().BeOfType<Cancelled>();
        var cancelledState = (Cancelled)result.Snapshot.State;
        cancelledState.Reason.Should().Be("Warehouse_Inventory_Unavailable");

        // Verify cancellation notification and completion
        result.Commands.Should().HaveCount(2);
        var cancellationCommand = result.Commands[0].Should().BeOfType<Send<OrderProcessingOutputMessage>>().Subject;
        var cancellation = cancellationCommand.Message.Should().BeOfType<NotifyOrderCancelled>().Subject;
        cancellation.Reason.Should().Be("Warehouse_Inventory_Unavailable");
        result.Commands[1].Should().BeOfType<Complete<OrderProcessingOutputMessage>>();
    }
}