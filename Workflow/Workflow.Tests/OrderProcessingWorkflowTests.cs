using AwesomeAssertions;

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