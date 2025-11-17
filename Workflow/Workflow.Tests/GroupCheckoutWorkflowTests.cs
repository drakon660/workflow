using AwesomeAssertions;
using Xunit.Internal;

namespace Workflow.Tests;

public class GroupCheckoutWorkflowTests
{
    private readonly GroupCheckoutWorkflow _workflow = new();
    private readonly WorkflowOrchestrator<GroupCheckoutInputMessage, GroupCheckoutState, GroupCheckoutOutputMessage> _orchestrator = new();

    [Fact]
    public void Initial_State_Should_Be_NotExisting()
    {
        // Arrange & Act
        var initialState = _workflow.InitialState;

        // Assert
        initialState.Should().BeOfType<NotExisting>();
    }

    [Fact]
    public void InitiateGroupCheckout_Should_Generate_CheckOut_Commands_For_All_Guests()
    {
        // Arrange
        var state = new NotExisting();
        var input = new InitiateGroupCheckout("group-123", [new Guest("guest-1"), new Guest("guest-2")]);
        
        // Act
        var commands = _workflow.Decide(input, state);

        // Assert
        commands.Should().HaveCount(2);
        commands.Should().AllBeOfType<Send<GroupCheckoutOutputMessage>>();

        var checkoutCommands = commands.Cast<Send<GroupCheckoutOutputMessage>>().ToList();
        var guestIds = checkoutCommands.Select(cmd => ((CheckOut)cmd.Message).GuestStayAccountId).ToList();
        guestIds.Should().Contain("guest-1");
        guestIds.Should().Contain("guest-2");
    }

    [Fact]
    public void InitiateGroupCheckout_Should_Transition_To_Pending_State()
    {
        // Arrange
        var state = new NotExisting();
        var initiateEvent = new InitiatedBy<GroupCheckoutInputMessage, GroupCheckoutOutputMessage>(
            new InitiateGroupCheckout("group-123", [new Guest("guest-1"), new Guest("guest-2")]));

        // Act
        var newState = _workflow.Evolve(state, initiateEvent);

        // Assert
        newState.Should().BeOfType<Pending>();
        var pendingState = (Pending)newState;
        pendingState.GroupCheckoutId.Should().Be("group-123");
        pendingState.Guests.Should().HaveCount(2);
        pendingState.Guests.Should().BeEquivalentTo([new Guest("guest-1"), new Guest("guest-2")]);
    }

    [Fact]
    public void GuestCheckedOut_Should_Update_Guest_Status_To_Completed()
    {
        // Arrange
        var state = new Pending("group-123", [new Guest("guest-1"), new Guest("guest-2")]);

        var receivedEvent = new Received<GroupCheckoutInputMessage, GroupCheckoutOutputMessage>(
            new GuestCheckedOut("guest-1"));

        // Act
        var newState = _workflow.Evolve(state, receivedEvent);

        // Assert
        newState.Should().BeOfType<Pending>();
        var pendingState = (Pending)newState;
        pendingState.Guests.First(x => x.Id == "guest-1").GuestStayStatus.Should().Be(GuestStayStatus.Completed);
        pendingState.Guests.First(x => x.Id == "guest-2").GuestStayStatus.Should().Be(GuestStayStatus.Pending);
    }

    [Fact]
    public void GuestCheckoutFailed_Should_Update_Guest_Status_To_Failed()
    {
        // Arrange
        var state = new Pending("group-123", [new Guest("guest-1"), new Guest("guest-2")]);
        var receivedEvent = new Received<GroupCheckoutInputMessage, GroupCheckoutOutputMessage>(
            new GuestCheckoutFailed("guest-1", "Payment failed"));

        // Act
        var newState = _workflow.Evolve(state, receivedEvent);

        // Assert
        newState.Should().BeOfType<Pending>();
        var pendingState = (Pending)newState;
        pendingState.Guests.First(x => x.Id == "guest-1").GuestStayStatus.Should().Be(GuestStayStatus.Failed);
        pendingState.Guests.First(x => x.Id == "guest-2").GuestStayStatus.Should().Be(GuestStayStatus.Pending);
    }

    [Fact]
    public void When_Not_All_Guests_Processed_Should_Not_Generate_Completion_Commands()
    {
        // Arrange
        var state = new Pending("group-123", [new Guest("guest-1"), new Guest("guest-2")]);
        var input = new GuestCheckedOut("guest-1");

        // Act
        var commands = _workflow.Decide(input, state);

        // Assert
        commands.Should().BeEmpty();
    }

    [Fact]
    public void When_All_Guests_Succeed_Should_Generate_GroupCheckoutCompleted()
    {
        // Arrange
        var state = new Pending("group-123",
            [new Guest("guest-1", GuestStayStatus.Completed), new Guest("guest-2", GuestStayStatus.Completed)]);
        var input = new GuestCheckedOut("guest-2");

        // Act
        var commands = _workflow.Decide(input, state);

        // Assert
        commands.Should().HaveCount(2);

        commands[0].Should().BeOfType<Send<GroupCheckoutOutputMessage>>();
        var sendCommand = (Send<GroupCheckoutOutputMessage>)commands[0];

        commands[1].Should().BeOfType<Complete<GroupCheckoutOutputMessage>>();

        sendCommand.Message.Should().BeOfType<GroupCheckoutCompleted>();
        var completedEvent = (GroupCheckoutCompleted)sendCommand.Message;
        completedEvent.GroupCheckoutId.Should().Be("group-123");
        completedEvent.CompletedCheckouts.Should().HaveCount(2);
        completedEvent.CompletedCheckouts.Should().Contain("guest-1");
        completedEvent.CompletedCheckouts.Should().Contain("guest-2");
    }

    //
    // [Fact]
    // public void When_Some_Guests_Fail_Should_Generate_GroupCheckoutFailed()
    // {
    //     // Arrange
    //     var state = new Pending("group-123", new Dictionary<string, GuestStayStatus>
    //     {
    //         { "guest-1", GuestStayStatus.Completed },
    //         { "guest-2", GuestStayStatus.Pending }
    //     });
    //     var input = new GuestCheckoutFailed("guest-2", "Balance not settled");
    //
    //     // Act
    //     var commands = _workflow.Decide(input, state);
    //
    //     // Assert
    //     commands.Should().HaveCount(2);
    //
    //     commands[0].Should().BeOfType<Send<GroupCheckoutOutputMessage>>();
    //     var sendCommand = (Send<GroupCheckoutOutputMessage>)commands[0];
    //
    //     commands[1].Should().BeOfType<Complete<GroupCheckoutOutputMessage>>();
    //
    //     sendCommand.Message.Should().BeOfType<GroupCheckoutFailed>();
    //     var failedEvent = (GroupCheckoutFailed)sendCommand.Message;
    //     failedEvent.GroupCheckoutId.Should().Be("group-123");
    //     failedEvent.CompletedCheckouts.Should().HaveCount(1);
    //     failedEvent.CompletedCheckouts.Should().Contain("guest-1");
    //     failedEvent.FailedCheckouts.Should().HaveCount(1);
    //     failedEvent.FailedCheckouts.Should().Contain("guest-2");
    // }
    //
    [Fact]
    public void When_All_Guests_Fail_Should_Generate_GroupCheckoutFailed()
    {
        // Arrange
        var state = new Pending("group-123",
            [new Guest("guest-1", GuestStayStatus.Failed), new Guest("guest-2", GuestStayStatus.Pending)]);

        var input = new GuestCheckoutFailed("guest-2", "Balance not settled");

        // Act
        var commands = _workflow.Decide(input, state);

        // Assert
        commands.Should().HaveCount(2);

        commands[0].Should().BeOfType<Send<GroupCheckoutOutputMessage>>();
        var sendCommand = (Send<GroupCheckoutOutputMessage>)commands[0];

        sendCommand.Message.Should().BeOfType<GroupCheckoutFailed>();
        var failedEvent = (GroupCheckoutFailed)sendCommand.Message;
        failedEvent.CompletedCheckouts.Should().BeEmpty();
        failedEvent.FailedCheckouts.Should().HaveCount(2);
    }

    [Fact]
    public void TimeoutGroupCheckout_Should_Generate_GroupCheckoutTimedOut()
    {
        // Arrange
        var state = new Pending("group-123",
            [new Guest("guest-1", GuestStayStatus.Failed), new Guest("guest-2"), new Guest("guest-3")]);
        var input = new TimeoutGroupCheckout("group-123");

        // Act
        var commands = _workflow.Decide(input, state);

        // Assert
        commands.Should().HaveCount(2);

        commands[0].Should().BeOfType<Send<GroupCheckoutOutputMessage>>();
        var sendCommand = (Send<GroupCheckoutOutputMessage>)commands[0];

        commands[1].Should().BeOfType<Complete<GroupCheckoutOutputMessage>>();

        sendCommand.Message.Should().BeOfType<GroupCheckoutTimedOut>();
        var timedOutEvent = (GroupCheckoutTimedOut)sendCommand.Message;
        timedOutEvent.GroupCheckoutId.Should().Be("group-123");
        timedOutEvent.PendingCheckouts.Should().HaveCount(2);
        timedOutEvent.PendingCheckouts.Should().Contain("guest-2");
        timedOutEvent.PendingCheckouts.Should().Contain("guest-3");
    }
    //
    // [Fact]
    // public void Translate_Should_Generate_Correct_Events_For_Begin()
    // {
    //     // Arrange
    //     var input = new InitiateGroupCheckout("group-123", new List<string> { "guest-1", "guest-2" });
    //     var commands = new List<WorkflowCommand<GroupCheckoutOutputMessage>>
    //     {
    //         new Send<GroupCheckoutOutputMessage>(new CheckOut("guest-1")),
    //         new Send<GroupCheckoutOutputMessage>(new CheckOut("guest-2"))
    //     };
    //
    //     // Act
    //     var events = _workflow.Translate(begins: true, input, commands);
    //
    //     // Assert
    //     events.Should().HaveCount(4);
    //     events[0].Should().BeOfType<Began<GroupCheckoutInputMessage, GroupCheckoutOutputMessage>>();
    //     events[1].Should().BeOfType<InitiatedBy<GroupCheckoutInputMessage, GroupCheckoutOutputMessage>>();
    //     events[2].Should().BeOfType<Sent<GroupCheckoutInputMessage, GroupCheckoutOutputMessage>>();
    //     events[3].Should().BeOfType<Sent<GroupCheckoutInputMessage, GroupCheckoutOutputMessage>>();
    // }
    //
    [Fact]
    public void Translate_Should_Generate_Correct_Events_For_Receive()
    {
        // Arrange
        var input = new GuestCheckedOut("guest-1");
        var commands = new List<WorkflowCommand<GroupCheckoutOutputMessage>>();
    
        // Act
        var events = _workflow.Translate(begins: false, input, commands);
    
        // Assert
        events.Should().HaveCount(1);
        events[0].Should().BeOfType<Received<GroupCheckoutInputMessage, GroupCheckoutOutputMessage>>();
    }
    
    // [Fact]
    // public void Full_Workflow_Happy_Path_All_Guests_Succeed()
    // {
    //     // This test demonstrates the complete workflow with event sourcing
    //     var eventStore = new List<WorkflowEvent<GroupCheckoutInputMessage, GroupCheckoutOutputMessage>>();
    //
    //     var workflow = new GroupCheckoutWorkflow();
    //     
    //
    //     // Step 1: Initiate group checkout
    //     var initiateInput = new InitiateGroupCheckout("group-123", [new Guest("guest-1"), new Guest("guest-2")]);
    //
    //     var workflowProcessor = new WorkflowOrchestrator<GroupCheckoutInputMessage, GroupCheckoutState, GroupCheckoutOutputMessage>();
    //     var snapshot = workflowProcessor.CreateInitialSnapshot(workflow);
    //     
    //     var result = workflowProcessor.Process(workflow, snapshot, initiateInput);
    //     
    //     eventStore.AddRange(initiateEvents);
    //
    //     // Rebuild state from events
    //     
    //     state.Should().BeOfType<Pending>();
    //
    //     // Step 2: First guest checks out successfully
    //     var guest1CheckedOut = new GuestCheckedOut("guest-1");
    //     var guest1Commands = _workflow.Decide(guest1CheckedOut, state);
    //     var guest1Events = _workflow.Translate(begins: false, guest1CheckedOut, guest1Commands);
    //     eventStore.AddRange(guest1Events);
    //
    //     foreach (var evt in guest1Events)
    //     {
    //         state = _workflow.Evolve(state, evt);
    //     }
    //
    //     state.Should().BeOfType<Pending>();
    //     var pendingState = (Pending)state;
    //     pendingState.GuestStayAccountStatuses["guest-1"].Should().Be(GuestStayStatus.Completed);
    //     pendingState.GuestStayAccountStatuses["guest-2"].Should().Be(GuestStayStatus.Pending);
    //
    //     // Step 3: Second guest checks out successfully - workflow completes
    //     var guest2CheckedOut = new GuestCheckedOut("guest-2");
    //     var guest2Commands = _workflow.Decide(guest2CheckedOut, state);
    //     var guest2Events = _workflow.Translate(begins: false, guest2CheckedOut, guest2Commands);
    //     eventStore.AddRange(guest2Events);
    //
    //     foreach (var evt in guest2Events)
    //     {
    //         state = _workflow.Evolve(state, evt);
    //     }
    //
    //     state.Should().BeOfType<Finished>();
    //
    //     // Verify event store contains complete history
    //     eventStore.Should().HaveCount(8);
    //     eventStore[0].Should().BeOfType<Began<GroupCheckoutInputMessage, GroupCheckoutOutputMessage>>();
    //     eventStore[1].Should().BeOfType<InitiatedBy<GroupCheckoutInputMessage, GroupCheckoutOutputMessage>>();
    //     eventStore[2].Should().BeOfType<Sent<GroupCheckoutInputMessage, GroupCheckoutOutputMessage>>(); // CheckOut guest-1
    //     eventStore[3].Should().BeOfType<Sent<GroupCheckoutInputMessage, GroupCheckoutOutputMessage>>(); // CheckOut guest-2
    //     eventStore[4].Should().BeOfType<Received<GroupCheckoutInputMessage, GroupCheckoutOutputMessage>>(); // GuestCheckedOut guest-1
    //     eventStore[5].Should().BeOfType<Received<GroupCheckoutInputMessage, GroupCheckoutOutputMessage>>(); // GuestCheckedOut guest-2
    //     eventStore[6].Should().BeOfType<Sent<GroupCheckoutInputMessage, GroupCheckoutOutputMessage>>(); // GroupCheckoutCompleted
    //     eventStore[7].Should().BeOfType<Completed<GroupCheckoutInputMessage, GroupCheckoutOutputMessage>>();
    //
    //     // Verify we can replay events to reconstruct final state
    //     var replayedState = _workflow.InitialState;
    //     foreach (var evt in eventStore)
    //     {
    //         replayedState = _workflow.Evolve(replayedState, evt);
    //     }
    //     replayedState.Should().Be(state);
    // }
    
    // [Fact]
    // public void Full_Workflow_Partial_Failure_Path()
    // {
    //     // This test demonstrates a workflow where one guest succeeds and one fails
    //     var eventStore = new List<WorkflowEvent<GroupCheckoutInputMessage, GroupCheckoutOutputMessage>>();
    //     var state = _workflow.InitialState;
    //
    //     // Step 1: Initiate group checkout
    //     var initiateInput = new InitiateGroupCheckout("group-123", new List<string> { "guest-1", "guest-2" });
    //     var initiateCommands = _workflow.Decide(initiateInput, state);
    //     var initiateEvents = _workflow.Translate(begins: true, initiateInput, initiateCommands);
    //     eventStore.AddRange(initiateEvents);
    //
    //     foreach (var evt in initiateEvents)
    //     {
    //         state = _workflow.Evolve(state, evt);
    //     }
    //
    //     // Step 2: First guest checks out successfully
    //     var guest1CheckedOut = new GuestCheckedOut("guest-1");
    //     var guest1Commands = _workflow.Decide(guest1CheckedOut, state);
    //     var guest1Events = _workflow.Translate(begins: false, guest1CheckedOut, guest1Commands);
    //     eventStore.AddRange(guest1Events);
    //
    //     foreach (var evt in guest1Events)
    //     {
    //         state = _workflow.Evolve(state, evt);
    //     }
    //
    //     // Step 3: Second guest checkout fails - workflow completes with failure
    //     var guest2Failed = new GuestCheckoutFailed("guest-2", "Balance not settled");
    //     var guest2Commands = _workflow.Decide(guest2Failed, state);
    //     var guest2Events = _workflow.Translate(begins: false, guest2Failed, guest2Commands);
    //     eventStore.AddRange(guest2Events);
    //
    //     foreach (var evt in guest2Events)
    //     {
    //         state = _workflow.Evolve(state, evt);
    //     }
    //
    //     state.Should().BeOfType<Finished>();
    //
    //     // Verify the failure event contains correct information
    //     var sentEvents = eventStore.OfType<Sent<GroupCheckoutInputMessage, GroupCheckoutOutputMessage>>().ToList();
    //     var failureEvent = sentEvents.FirstOrDefault(e => e.Message is GroupCheckoutFailed);
    //     failureEvent.Should().NotBeNull();
    //
    //     var failedMessage = (GroupCheckoutFailed)failureEvent!.Message;
    //     failedMessage.CompletedCheckouts.Should().HaveCount(1);
    //     failedMessage.CompletedCheckouts.Should().Contain("guest-1");
    //     failedMessage.FailedCheckouts.Should().HaveCount(1);
    //     failedMessage.FailedCheckouts.Should().Contain("guest-2");
    // }
    //
    [Fact]
    public void Full_Workflow_Timeout_Scenario()
    {
        // This test demonstrates a workflow that times out with pending guests
        var eventStore = new List<WorkflowEvent<GroupCheckoutInputMessage, GroupCheckoutOutputMessage>>();
        var state = _workflow.InitialState;
    
        // Step 1: Initiate group checkout for 3 guests
        var initiateInput = new InitiateGroupCheckout("group-123", [new Guest("guest-1", GuestStayStatus.Failed), new Guest("guest-2"), new Guest("guest-3")]);
        var initiateCommands = _workflow.Decide(initiateInput, state);
        var initiateEvents = _workflow.Translate(begins: true, initiateInput, initiateCommands);
        eventStore.AddRange(initiateEvents);
    
        foreach (var evt in initiateEvents)
        {
            state = _workflow.Evolve(state, evt);
        }
    
        // Step 2: Only first guest checks out
        var guest1CheckedOut = new GuestCheckedOut("guest-1");
        var guest1Commands = _workflow.Decide(guest1CheckedOut, state);
        var guest1Events = _workflow.Translate(begins: false, guest1CheckedOut, guest1Commands);
        eventStore.AddRange(guest1Events);
    
        foreach (var evt in guest1Events)
        {
            state = _workflow.Evolve(state, evt);
        }
    
        // Step 3: Timeout occurs with 2 guests still pending
        var timeout = new TimeoutGroupCheckout("group-123");
        var timeoutCommands = _workflow.Decide(timeout, state);
        var timeoutEvents = _workflow.Translate(begins: false, timeout, timeoutCommands);
        eventStore.AddRange(timeoutEvents);
    
        foreach (var evt in timeoutEvents)
        {
            state = _workflow.Evolve(state, evt);
        }
    
        state.Should().BeOfType<Finished>();
    
        // Verify the timeout event contains correct information
        var sentEvents = eventStore.OfType<Sent<GroupCheckoutInputMessage, GroupCheckoutOutputMessage>>().ToList();
        var timeoutEvent = sentEvents.FirstOrDefault(e => e.Message is GroupCheckoutTimedOut);
        timeoutEvent.Should().NotBeNull();
    
        var timeoutMessage = (GroupCheckoutTimedOut)timeoutEvent!.Message;
        timeoutMessage.PendingCheckouts.Should().HaveCount(2);
        timeoutMessage.PendingCheckouts.Should().Contain("guest-2");
        timeoutMessage.PendingCheckouts.Should().Contain("guest-3");
    }
}