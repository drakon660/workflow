using AwesomeAssertions;
using Workflow.InboxOutbox;
using Workflow.Samples.Order;

namespace Workflow.Tests;

public class WorkflowProcessorTests
{
    const string TestWorkflowId = "order-123";

    static WorkflowProcessor<OrderProcessingInputMessage, OrderProcessingState, OrderProcessingOutputMessage> CreateProcessor(
        WorkflowStreamRepository? repository = null,
        List<WorkflowMessage>? deliveredMessages = null)
    {
        repository ??= new WorkflowStreamRepository();
        deliveredMessages ??= [];

        return new WorkflowProcessor<OrderProcessingInputMessage, OrderProcessingState, OrderProcessingOutputMessage>(
            repository,
            new OrderProcessingWorkflow(),
            deliverOutput: (msg, ct) =>
            {
                deliveredMessages.Add(msg);
                return Task.CompletedTask;
            },
            new WorkflowProcessorOptions
            {
                ProcessorId = "test-processor",
                OutputDeliveryLimit = 100,
                OutputDeliveryTimeout = TimeSpan.FromSeconds(5)
            });
    }

    [Fact]
    public async Task ProcessAsync_NewWorkflow_ShouldStoreEventsAndCommands()
    {
        // Arrange
        var repository = new WorkflowStreamRepository();
        var deliveredMessages = new List<WorkflowMessage>();
        var processor = CreateProcessor(repository, deliveredMessages);

        // Act
        await processor.ProcessAsync(TestWorkflowId, new PlaceOrderInputMessage(TestWorkflowId));

        // Assert - get the stream and verify contents
        var stream = await repository.GetOrCreate(TestWorkflowId, CancellationToken.None);

        // Debug: check all messages
        var allMessages = stream.GetAllMessages();
        // Expected: 1 input (Command) + 5 events (Event) + 3 commands (Command) = 9 total
        allMessages.Should().HaveCount(9);

        // Check input is stored first
        allMessages[0].Kind.Should().Be(MessageKind.Command);
        allMessages[0].Direction.Should().Be(MessageDirection.Input);

        // Events: Began, InitiatedBy, Sent(ProcessPayment), Sent(NotifyOrderPlaced), Scheduled(PaymentTimeout)
        var events = stream.GetEvents();
        events.Should().HaveCount(5);

        // Verify Began and InitiatedBy are first two events
        events[0].DeserializedBody.Should().BeOfType<Began<OrderProcessingInputMessage, OrderProcessingOutputMessage>>();
        events[1].DeserializedBody.Should().BeOfType<InitiatedBy<OrderProcessingInputMessage, OrderProcessingOutputMessage>>();
    }

    [Fact]
    public async Task ProcessAsync_NewWorkflow_ShouldSetBeginsTrue()
    {
        // Arrange
        var repository = new WorkflowStreamRepository();
        var deliveredMessages = new List<WorkflowMessage>();
        var processor = CreateProcessor(repository, deliveredMessages);

        // Act
        await processor.ProcessAsync(TestWorkflowId, new PlaceOrderInputMessage(TestWorkflowId));

        // Assert - first event should be Began
        var stream = await repository.GetOrCreate(TestWorkflowId, CancellationToken.None);
        var events = stream.GetEvents();

        var firstEvent = events[0].DeserializedBody;
        firstEvent.Should().BeOfType<Began<OrderProcessingInputMessage, OrderProcessingOutputMessage>>();

        var secondEvent = events[1].DeserializedBody;
        secondEvent.Should().BeOfType<InitiatedBy<OrderProcessingInputMessage, OrderProcessingOutputMessage>>();
    }

    [Fact]
    public async Task ProcessAsync_ContinueWorkflow_ShouldSetBeginsFalse()
    {
        // Arrange
        var repository = new WorkflowStreamRepository();
        var deliveredMessages = new List<WorkflowMessage>();
        var processor = CreateProcessor(repository, deliveredMessages);

        // First message starts the workflow
        await processor.ProcessAsync(TestWorkflowId, new PlaceOrderInputMessage(TestWorkflowId));
        deliveredMessages.Clear();

        // Act - second message continues
        await processor.ProcessAsync(TestWorkflowId, new PaymentReceivedInputMessage(TestWorkflowId));

        // Assert - should have Received event, not Began/InitiatedBy
        var stream = await repository.GetOrCreate(TestWorkflowId, CancellationToken.None);
        var events = stream.GetEvents();

        // First 5 events from PlaceOrder, then 2 from PaymentReceived (Received + Sent)
        events.Should().HaveCount(7);

        // The 6th event should be Received (not Began)
        var receivedEvent = events[5].DeserializedBody;
        receivedEvent.Should().BeOfType<Received<OrderProcessingInputMessage, OrderProcessingOutputMessage>>();
    }

    [Fact]
    public async Task ProcessAsync_ShouldRebuildStateFromEvents()
    {
        // Arrange
        var repository = new WorkflowStreamRepository();
        var deliveredMessages = new List<WorkflowMessage>();
        var processor = CreateProcessor(repository, deliveredMessages);

        // Process PlaceOrder -> state becomes OrderCreated
        await processor.ProcessAsync(TestWorkflowId, new PlaceOrderInputMessage(TestWorkflowId));

        // Process PaymentReceived -> state should become PaymentConfirmed
        await processor.ProcessAsync(TestWorkflowId, new PaymentReceivedInputMessage(TestWorkflowId));

        // Act - check the state by sending CheckOrderState
        deliveredMessages.Clear();
        await processor.ProcessAsync(TestWorkflowId, new CheckOrderStateInputMessage(TestWorkflowId));

        // Assert - should get a Reply with PaymentConfirmed status
        deliveredMessages.Should().HaveCount(1);
        var replyMessage = deliveredMessages[0].DeserializedBody as Reply<OrderProcessingOutputMessage>;
        replyMessage.Should().NotBeNull();

        var status = replyMessage!.Message as OrderProcessingStatus;
        status.Should().NotBeNull();
        status!.Status.Should().Be("PaymentConfirmed");
    }

    [Fact]
    public async Task ProcessAsync_FullHappyPath_ShouldComplete()
    {
        // Arrange
        var repository = new WorkflowStreamRepository();
        var deliveredMessages = new List<WorkflowMessage>();
        var processor = CreateProcessor(repository, deliveredMessages);

        // Act - full order lifecycle
        await processor.ProcessAsync(TestWorkflowId, new PlaceOrderInputMessage(TestWorkflowId));
        await processor.ProcessAsync(TestWorkflowId, new PaymentReceivedInputMessage(TestWorkflowId));
        await processor.ProcessAsync(TestWorkflowId, new OrderShippedInputMessage(TestWorkflowId, "TRACK-456"));
        await processor.ProcessAsync(TestWorkflowId, new OrderDeliveredInputMessage(TestWorkflowId));

        // Assert - verify final state
        deliveredMessages.Clear();
        await processor.ProcessAsync(TestWorkflowId, new CheckOrderStateInputMessage(TestWorkflowId));

        var replyMessage = deliveredMessages[0].DeserializedBody as Reply<OrderProcessingOutputMessage>;
        var status = replyMessage!.Message as OrderProcessingStatus;
        status!.Status.Should().Be("Delivered");
    }

    [Fact]
    public async Task ProcessAsync_OrderCancelled_ShouldComplete()
    {
        // Arrange
        var repository = new WorkflowStreamRepository();
        var deliveredMessages = new List<WorkflowMessage>();
        var processor = CreateProcessor(repository, deliveredMessages);

        // Start the order
        await processor.ProcessAsync(TestWorkflowId, new PlaceOrderInputMessage(TestWorkflowId));

        // PlaceOrder delivers: Send(ProcessPayment), Send(NotifyOrderPlaced)
        // Schedule(PaymentTimeout) is not delivered (future)
        deliveredMessages.Should().HaveCount(2);
        deliveredMessages.Clear();

        // Act - cancel the order
        await processor.ProcessAsync(TestWorkflowId, new OrderCancelledInputMessage(TestWorkflowId, "Customer request"));

        // Assert - OrderCancelled delivers: Send(NotifyOrderCancelled), Complete
        deliveredMessages.Should().HaveCount(2);

        var completeCommand = deliveredMessages
            .Where(m => m.DeserializedBody is Complete<OrderProcessingOutputMessage>)
            .ToList();
        completeCommand.Should().HaveCount(1);

        // Verify stream contents
        var stream = await repository.GetOrCreate(TestWorkflowId, CancellationToken.None);
        var allMessages = stream.GetAllMessages();

        // PlaceOrder: 1 input + 5 events + 3 commands = 9
        // OrderCancelled: 1 input + 3 events (Received, Sent, Completed) + 2 commands = 6
        // Total: 15
        allMessages.Should().HaveCount(15);
        
        var commands = allMessages.Where(x=>x.Kind == MessageKind.Command);
        var events2 = allMessages.Where(x=>x.Kind == MessageKind.Event);
        
        var events = stream.GetEvents();
        // PlaceOrder: Began, InitiatedBy, Sent, Sent, Scheduled = 5
        // OrderCancelled: Received, Sent, Completed = 3
        // Total: 8
        events.Should().HaveCount(8);
    }

    [Fact]
    public async Task ProcessAsync_MultipleWorkflows_ShouldBeIndependent()
    {
        // Arrange
        var repository = new WorkflowStreamRepository();
        var deliveredMessages = new List<WorkflowMessage>();
        var processor = CreateProcessor(repository, deliveredMessages);

        const string orderId1 = "order-1";
        const string orderId2 = "order-2";

        // Act - start two workflows
        await processor.ProcessAsync(orderId1, new PlaceOrderInputMessage(orderId1));
        await processor.ProcessAsync(orderId2, new PlaceOrderInputMessage(orderId2));

        // Advance order 1 to PaymentConfirmed
        await processor.ProcessAsync(orderId1, new PaymentReceivedInputMessage(orderId1));

        // Assert - check states are independent
        deliveredMessages.Clear();

        await processor.ProcessAsync(orderId1, new CheckOrderStateInputMessage(orderId1));
        var reply1 = (deliveredMessages[0].DeserializedBody as Reply<OrderProcessingOutputMessage>)!;
        var status1 = (reply1.Message as OrderProcessingStatus)!;
        status1.Status.Should().Be("PaymentConfirmed");

        deliveredMessages.Clear();

        await processor.ProcessAsync(orderId2, new CheckOrderStateInputMessage(orderId2));
        var reply2 = (deliveredMessages[0].DeserializedBody as Reply<OrderProcessingOutputMessage>)!;
        var status2 = (reply2.Message as OrderProcessingStatus)!;
        status2.Status.Should().Be("OrderCreated"); // Still at initial state
    }

    [Fact]
    public async Task ProcessAsync_ScheduledCommand_ShouldNotBeDeliveredImmediately()
    {
        // Arrange
        var repository = new WorkflowStreamRepository();
        var deliveredMessages = new List<WorkflowMessage>();
        var processor = CreateProcessor(repository, deliveredMessages);

        // Act
        await processor.ProcessAsync(TestWorkflowId, new PlaceOrderInputMessage(TestWorkflowId));

        // Assert - scheduled messages should not be in delivered (ScheduledTime is in future)
        var scheduledDelivered = deliveredMessages
            .Where(m => m.ScheduledTime.HasValue && m.ScheduledTime > DateTime.UtcNow)
            .ToList();

        // The Schedule command should NOT have been delivered because it's in the future
        scheduledDelivered.Should().BeEmpty();

        // But it should be stored in the stream (check all commands, then filter)
        var stream = await repository.GetOrCreate(TestWorkflowId, CancellationToken.None);

        // Get all commands in stream
        var allCommands = stream.GetAllMessages()
            .Where(m => m.Kind == MessageKind.Command && m.Direction == MessageDirection.Output)
            .ToList();
        allCommands.Should().HaveCount(3); // Send, Send, Schedule

        // The scheduled one should have ScheduledTime set
        var scheduledCommand = allCommands.SingleOrDefault(m => m.ScheduledTime.HasValue);
        scheduledCommand.Should().NotBeNull();

        // It should NOT be marked as delivered
        scheduledCommand!.IsDelivered.Should().BeFalse();
    }

    [Fact]
    public async Task ProcessAsync_ShouldSerializeInnerMessagesWithRuntimeType()
    {
        // Arrange
        var repository = new WorkflowStreamRepository();
        var deliveredMessages = new List<WorkflowMessage>();
        var processor = CreateProcessor(repository, deliveredMessages);

        // Act
        await processor.ProcessAsync(TestWorkflowId, new PlaceOrderInputMessage(TestWorkflowId));

        // Assert - check the serialized events have proper inner message types
        var stream = await repository.GetOrCreate(TestWorkflowId, CancellationToken.None);
        var events = stream.GetEvents();

        // Find the Sent event for ProcessPayment
        var sentEvent = events.First(e => e.MessageType == "Sent" && e.Body.Contains("ProcessPayment") == false);

        // The Body should now contain the actual OrderId, not just {}
        sentEvent.Body.Should().Contain("order-123");

        // InnerMessageType should be set
        sentEvent.InnerMessageType.Should().NotBeNullOrEmpty();
        sentEvent.InnerMessageType.Should().Contain("ProcessPayment");

        // The input should also be properly serialized
        var allMessages = stream.GetAllMessages();
        var input = allMessages.First(m => m.Direction == MessageDirection.Input);
        input.Body.Should().Contain("order-123");
    }

    [Fact]
    public async Task ProcessAsync_ShouldDeserializeEventsCorrectly()
    {
        // Arrange
        var repository = new WorkflowStreamRepository();
        var deliveredMessages = new List<WorkflowMessage>();
        var processor = CreateProcessor(repository, deliveredMessages);

        // Process first message
        await processor.ProcessAsync(TestWorkflowId, new PlaceOrderInputMessage(TestWorkflowId));

        // Clear DeserializedBody to force deserialization from Body
        var stream = await repository.GetOrCreate(TestWorkflowId, CancellationToken.None);
        foreach (var msg in stream.GetEvents())
        {
            msg.DeserializedBody = null;
        }

        // Act - process another message (will rebuild state from stored events)
        await processor.ProcessAsync(TestWorkflowId, new PaymentReceivedInputMessage(TestWorkflowId));

        // Assert - verify state was rebuilt correctly by checking the final state
        deliveredMessages.Clear();
        await processor.ProcessAsync(TestWorkflowId, new CheckOrderStateInputMessage(TestWorkflowId));

        var reply = deliveredMessages[0].DeserializedBody as Reply<OrderProcessingOutputMessage>;
        var status = reply!.Message as OrderProcessingStatus;
        status!.Status.Should().Be("PaymentConfirmed");
    }
}
