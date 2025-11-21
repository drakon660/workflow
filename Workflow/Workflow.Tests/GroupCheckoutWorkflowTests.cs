using AwesomeAssertions;
using Xunit.Internal;

namespace Workflow.Tests;

public class GroupCheckoutWorkflowTests
{
    private readonly GroupCheckoutWorkflow _workflow = new();
    private readonly WorkflowOrchestrator<GroupCheckoutInputMessage, GroupCheckoutState, GroupCheckoutOutputMessage> _orchestrator = new();

    // === Unit Tests (Testing individual methods) ===

    [Fact]
    public void Initial_State_Should_Be_NotExisting()
    {
        // Arrange & Act
        var initialState = _workflow.InitialState;

        // Assert
        initialState.Should().BeOfType<NotExisting>();
    }

    [Fact]
    public void Decide_InitiateGroupCheckout_Should_Generate_CheckOut_Commands_For_All_Guests()
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
    public void Evolve_InitiatedBy_Should_Transition_To_Pending_State()
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

    // === Integration Tests (Using Orchestrator) ===

    [Fact]
    public void InitiateGroupCheckout_Should_Generate_Commands_And_Transition_To_Pending()
    {
        // Arrange
        var snapshot = _orchestrator.CreateInitialSnapshot(_workflow);
        var input = new InitiateGroupCheckout("group-123", [new Guest("guest-1"), new Guest("guest-2")]);

        // Act
        var result = _orchestrator.Run(_workflow, snapshot, input, begins: true);

        // Assert - Commands
        result.Commands.Should().HaveCount(2);
        result.Commands.Should().AllBeOfType<Send<GroupCheckoutOutputMessage>>();
        var checkoutCommands = result.Commands.Cast<Send<GroupCheckoutOutputMessage>>().ToList();
        var guestIds = checkoutCommands.Select(cmd => ((CheckOut)cmd.Message).GuestStayAccountId).ToList();
        guestIds.Should().Contain("guest-1");
        guestIds.Should().Contain("guest-2");

        // Assert - State
        result.Snapshot.State.Should().BeOfType<Pending>();
        var pendingState = (Pending)result.Snapshot.State;
        pendingState.GroupCheckoutId.Should().Be("group-123");
        pendingState.Guests.Should().HaveCount(2);
    }

    [Fact]
    public void GuestCheckedOut_Should_Update_Guest_Status_To_Completed()
    {
        // Arrange - Start with pending state
        var snapshot = new WorkflowSnapshot<GroupCheckoutInputMessage, GroupCheckoutState, GroupCheckoutOutputMessage>(
            new Pending("group-123", [new Guest("guest-1"), new Guest("guest-2")]),
            []
        );
        var input = new GuestCheckedOut("guest-1");

        // Act
        var result = _orchestrator.Run(_workflow, snapshot, input, begins: false);

        // Assert
        result.Snapshot.State.Should().BeOfType<Pending>();
        var pendingState = (Pending)result.Snapshot.State;
        pendingState.Guests.First(x => x.Id == "guest-1").GuestStayStatus.Should().Be(GuestStayStatus.Completed);
        pendingState.Guests.First(x => x.Id == "guest-2").GuestStayStatus.Should().Be(GuestStayStatus.Pending);
    }

    [Fact]
    public void GuestCheckoutFailed_Should_Update_Guest_Status_To_Failed()
    {
        // Arrange - Start with pending state
        var snapshot = new WorkflowSnapshot<GroupCheckoutInputMessage, GroupCheckoutState, GroupCheckoutOutputMessage>(
            new Pending("group-123", [new Guest("guest-1"), new Guest("guest-2")]),
            []
        );
        var input = new GuestCheckoutFailed("guest-1", "Payment failed");

        // Act
        var result = _orchestrator.Run(_workflow, snapshot, input, begins: false);

        // Assert
        result.Snapshot.State.Should().BeOfType<Pending>();
        var pendingState = (Pending)result.Snapshot.State;
        pendingState.Guests.First(x => x.Id == "guest-1").GuestStayStatus.Should().Be(GuestStayStatus.Failed);
        pendingState.Guests.First(x => x.Id == "guest-2").GuestStayStatus.Should().Be(GuestStayStatus.Pending);
    }

    [Fact]
    public void When_Not_All_Guests_Processed_Should_Not_Generate_Completion_Commands()
    {
        // Arrange - Start with pending state
        var snapshot = new WorkflowSnapshot<GroupCheckoutInputMessage, GroupCheckoutState, GroupCheckoutOutputMessage>(
            new Pending("group-123", [new Guest("guest-1"), new Guest("guest-2")]),
            []
        );
        var input = new GuestCheckedOut("guest-1");

        // Act
        var result = _orchestrator.Run(_workflow, snapshot, input, begins: false);

        // Assert
        result.Commands.Should().BeEmpty();
    }

    [Fact]
    public void When_All_Guests_Succeed_Should_Generate_GroupCheckoutCompleted()
    {
        // Arrange - Start with state where both guests are already completed
        var snapshot = new WorkflowSnapshot<GroupCheckoutInputMessage, GroupCheckoutState, GroupCheckoutOutputMessage>(
            new Pending("group-123",
                [new Guest("guest-1", GuestStayStatus.Completed), new Guest("guest-2", GuestStayStatus.Completed)]),
            []
        );
        var input = new GuestCheckedOut("guest-2");

        // Act
        var result = _orchestrator.Run(_workflow, snapshot, input, begins: false);

        // Assert
        result.Commands.Should().HaveCount(2);

        result.Commands[0].Should().BeOfType<Send<GroupCheckoutOutputMessage>>();
        var sendCommand = (Send<GroupCheckoutOutputMessage>)result.Commands[0];

        result.Commands[1].Should().BeOfType<Complete<GroupCheckoutOutputMessage>>();

        sendCommand.Message.Should().BeOfType<GroupCheckoutCompleted>();
        var completedEvent = (GroupCheckoutCompleted)sendCommand.Message;
        completedEvent.GroupCheckoutId.Should().Be("group-123");
        completedEvent.CompletedCheckouts.Should().HaveCount(2);
        completedEvent.CompletedCheckouts.Should().Contain("guest-1");
        completedEvent.CompletedCheckouts.Should().Contain("guest-2");
    }


    [Fact]
    public void When_Some_Guests_Fail_Should_Generate_GroupCheckoutFailed()
    {
        // Arrange - Start with state where one guest completed and one is pending
        var snapshot = new WorkflowSnapshot<GroupCheckoutInputMessage, GroupCheckoutState, GroupCheckoutOutputMessage>(
            new Pending("group-123",
                [new Guest("guest-1", GuestStayStatus.Completed), new Guest("guest-2", GuestStayStatus.Pending)]),
            []
        );
        var input = new GuestCheckoutFailed("guest-2", "Balance not settled");

        // Act
        var result = _orchestrator.Run(_workflow, snapshot, input, begins: false);

        // Assert
        result.Commands.Should().HaveCount(2);

        result.Commands[0].Should().BeOfType<Send<GroupCheckoutOutputMessage>>();
        var sendCommand = (Send<GroupCheckoutOutputMessage>)result.Commands[0];

        result.Commands[1].Should().BeOfType<Complete<GroupCheckoutOutputMessage>>();

        sendCommand.Message.Should().BeOfType<GroupCheckoutFailed>();
        var failedEvent = (GroupCheckoutFailed)sendCommand.Message;
        failedEvent.GroupCheckoutId.Should().Be("group-123");
        failedEvent.CompletedCheckouts.Should().HaveCount(1);
        failedEvent.CompletedCheckouts.Should().Contain("guest-1");
        failedEvent.FailedCheckouts.Should().HaveCount(1);
        failedEvent.FailedCheckouts.Should().Contain("guest-2");
    }

    [Fact]
    public void When_All_Guests_Fail_Should_Generate_GroupCheckoutFailed()
    {
        // Arrange - Start with state where one guest failed and one is pending
        var snapshot = new WorkflowSnapshot<GroupCheckoutInputMessage, GroupCheckoutState, GroupCheckoutOutputMessage>(
            new Pending("group-123",
                [new Guest("guest-1", GuestStayStatus.Failed), new Guest("guest-2", GuestStayStatus.Pending)]),
            []
        );
        var input = new GuestCheckoutFailed("guest-2", "Balance not settled");

        // Act
        var result = _orchestrator.Run(_workflow, snapshot, input, begins: false);

        // Assert
        result.Commands.Should().HaveCount(2);

        result.Commands[0].Should().BeOfType<Send<GroupCheckoutOutputMessage>>();
        var sendCommand = (Send<GroupCheckoutOutputMessage>)result.Commands[0];

        result.Commands[1].Should().BeOfType<Complete<GroupCheckoutOutputMessage>>();

        sendCommand.Message.Should().BeOfType<GroupCheckoutFailed>();
        var failedEvent = (GroupCheckoutFailed)sendCommand.Message;
        failedEvent.CompletedCheckouts.Should().BeEmpty();
        failedEvent.FailedCheckouts.Should().HaveCount(2);
    }

    [Fact]
    public void TimeoutGroupCheckout_Should_Generate_GroupCheckoutTimedOut()
    {
        // Arrange - Start with pending state
        var snapshot = new WorkflowSnapshot<GroupCheckoutInputMessage, GroupCheckoutState, GroupCheckoutOutputMessage>(
            new Pending("group-123",
                [new Guest("guest-1", GuestStayStatus.Failed), new Guest("guest-2"), new Guest("guest-3")]),
            []
        );
        var input = new TimeoutGroupCheckout("group-123");

        // Act
        var result = _orchestrator.Run(_workflow, snapshot, input, begins: false);

        // Assert
        result.Commands.Should().HaveCount(2);

        result.Commands[0].Should().BeOfType<Send<GroupCheckoutOutputMessage>>();
        var sendCommand = (Send<GroupCheckoutOutputMessage>)result.Commands[0];

        result.Commands[1].Should().BeOfType<Complete<GroupCheckoutOutputMessage>>();

        sendCommand.Message.Should().BeOfType<GroupCheckoutTimedOut>();
        var timedOutEvent = (GroupCheckoutTimedOut)sendCommand.Message;
        timedOutEvent.GroupCheckoutId.Should().Be("group-123");
        timedOutEvent.PendingCheckouts.Should().HaveCount(2);
        timedOutEvent.PendingCheckouts.Should().Contain("guest-2");
        timedOutEvent.PendingCheckouts.Should().Contain("guest-3");
    }

    [Fact]
    public void Translate_Should_Generate_Correct_Events_For_Begin()
    {
        // Unit test for Translate method
        // Arrange
        var input = new InitiateGroupCheckout("group-123", [new Guest("guest-1"), new Guest("guest-2")]);
        var commands = new List<WorkflowCommand<GroupCheckoutOutputMessage>>
        {
            new Send<GroupCheckoutOutputMessage>(new CheckOut("guest-1")),
            new Send<GroupCheckoutOutputMessage>(new CheckOut("guest-2"))
        };

        // Act
        var events = _workflow.Translate(begins: true, input, commands);

        // Assert
        events.Should().HaveCount(4);
        events[0].Should().BeOfType<Began<GroupCheckoutInputMessage, GroupCheckoutOutputMessage>>();
        events[1].Should().BeOfType<InitiatedBy<GroupCheckoutInputMessage, GroupCheckoutOutputMessage>>();
        events[2].Should().BeOfType<Sent<GroupCheckoutInputMessage, GroupCheckoutOutputMessage>>();
        events[3].Should().BeOfType<Sent<GroupCheckoutInputMessage, GroupCheckoutOutputMessage>>();
    }

    [Fact]
    public void Translate_Should_Generate_Correct_Events_For_Receive()
    {
        // Unit test for Translate method
        // Arrange
        var input = new GuestCheckedOut("guest-1");
        var commands = new List<WorkflowCommand<GroupCheckoutOutputMessage>>();

        // Act
        var events = _workflow.Translate(begins: false, input, commands);

        // Assert
        events.Should().HaveCount(1);
        events[0].Should().BeOfType<Received<GroupCheckoutInputMessage, GroupCheckoutOutputMessage>>();
    }

    // === Full Workflow Scenario Tests ===

    [Fact]
    public void Full_Workflow_Happy_Path_All_Guests_Succeed()
    {
        // This test demonstrates the complete workflow with event sourcing

        // Step 1: Initiate group checkout
        var snapshot = _orchestrator.CreateInitialSnapshot(_workflow);
        var initiateInput = new InitiateGroupCheckout("group-123", [new Guest("guest-1"), new Guest("guest-2")]);

        var result1 = _orchestrator.Run(_workflow, snapshot, initiateInput, begins: true);

        result1.Snapshot.State.Should().BeOfType<Pending>();
        result1.Commands.Should().HaveCount(2); // CheckOut commands for both guests
        result1.Events.Should().HaveCount(4); // Began + InitiatedBy + 2x Sent

        // Step 2: First guest checks out successfully
        var guest1CheckedOut = new GuestCheckedOut("guest-1");
        var result2 = _orchestrator.Run(_workflow, result1.Snapshot, guest1CheckedOut, begins: false);

        result2.Snapshot.State.Should().BeOfType<Pending>();
        var pendingState = (Pending)result2.Snapshot.State;
        pendingState.Guests.First(x => x.Id == "guest-1").GuestStayStatus.Should().Be(GuestStayStatus.Completed);
        pendingState.Guests.First(x => x.Id == "guest-2").GuestStayStatus.Should().Be(GuestStayStatus.Pending);
        result2.Commands.Should().BeEmpty(); // Not complete yet

        // Step 3: Second guest checks out successfully - workflow completes
        var guest2CheckedOut = new GuestCheckedOut("guest-2");
        var result3 = _orchestrator.Run(_workflow, result2.Snapshot, guest2CheckedOut, begins: false);

        result3.Snapshot.State.Should().BeOfType<Finished>();
        result3.Commands.Should().HaveCount(2); // Send(GroupCheckoutCompleted) + Complete

        result3.Commands[0].Should().BeOfType<Send<GroupCheckoutOutputMessage>>();
        var sendCommand = (Send<GroupCheckoutOutputMessage>)result3.Commands[0];
        sendCommand.Message.Should().BeOfType<GroupCheckoutCompleted>();

        var completedEvent = (GroupCheckoutCompleted)sendCommand.Message;
        completedEvent.CompletedCheckouts.Should().HaveCount(2);
        completedEvent.CompletedCheckouts.Should().Contain("guest-1");
        completedEvent.CompletedCheckouts.Should().Contain("guest-2");

        result3.Commands[1].Should().BeOfType<Complete<GroupCheckoutOutputMessage>>();

        // Verify event history shows complete workflow execution
        var allEvents = result1.Snapshot.EventHistory
            .Concat(result2.Snapshot.EventHistory)
            .Concat(result3.Snapshot.EventHistory)
            .ToList();

        allEvents.Should().Contain(e => e is Began<GroupCheckoutInputMessage, GroupCheckoutOutputMessage>);
        allEvents.Should().Contain(e => e is InitiatedBy<GroupCheckoutInputMessage, GroupCheckoutOutputMessage>);
        allEvents.Should().Contain(e => e is Received<GroupCheckoutInputMessage, GroupCheckoutOutputMessage>);
    }

    [Fact]
    public void Full_Workflow_Partial_Failure_Path()
    {
        // This test demonstrates a workflow where one guest succeeds and one fails

        // Step 1: Initiate group checkout
        var snapshot = _orchestrator.CreateInitialSnapshot(_workflow);
        var initiateInput = new InitiateGroupCheckout("group-123", [new Guest("guest-1"), new Guest("guest-2")]);

        var result1 = _orchestrator.Run(_workflow, snapshot, initiateInput, begins: true);

        result1.Snapshot.State.Should().BeOfType<Pending>();
        result1.Commands.Should().HaveCount(2); // CheckOut commands for both guests

        // Step 2: First guest checks out successfully
        var guest1CheckedOut = new GuestCheckedOut("guest-1");
        var result2 = _orchestrator.Run(_workflow, result1.Snapshot, guest1CheckedOut, begins: false);

        result2.Snapshot.State.Should().BeOfType<Pending>();
        var pendingState = (Pending)result2.Snapshot.State;
        pendingState.Guests.First(x => x.Id == "guest-1").GuestStayStatus.Should().Be(GuestStayStatus.Completed);
        pendingState.Guests.First(x => x.Id == "guest-2").GuestStayStatus.Should().Be(GuestStayStatus.Pending);

        // Step 3: Second guest checkout fails - workflow completes with failure
        var guest2Failed = new GuestCheckoutFailed("guest-2", "Balance not settled");
        var result3 = _orchestrator.Run(_workflow, result2.Snapshot, guest2Failed, begins: false);

        result3.Snapshot.State.Should().BeOfType<Finished>();

        // Verify the failure event contains correct information
        result3.Commands.Should().HaveCount(2); // Send(GroupCheckoutFailed) + Complete

        result3.Commands[0].Should().BeOfType<Send<GroupCheckoutOutputMessage>>();
        var sendCommand = (Send<GroupCheckoutOutputMessage>)result3.Commands[0];
        sendCommand.Message.Should().BeOfType<GroupCheckoutFailed>();

        var failedMessage = (GroupCheckoutFailed)sendCommand.Message;
        failedMessage.CompletedCheckouts.Should().HaveCount(1);
        failedMessage.CompletedCheckouts.Should().Contain("guest-1");
        failedMessage.FailedCheckouts.Should().HaveCount(1);
        failedMessage.FailedCheckouts.Should().Contain("guest-2");

        result3.Commands[1].Should().BeOfType<Complete<GroupCheckoutOutputMessage>>();
    }

    [Fact]
    public void Full_Workflow_Timeout_Scenario()
    {
        // This test demonstrates a workflow that times out with pending guests

        // Step 1: Initiate group checkout for 3 guests
        var snapshot = _orchestrator.CreateInitialSnapshot(_workflow);
        var initiateInput = new InitiateGroupCheckout("group-123", [new Guest("guest-1"), new Guest("guest-2"), new Guest("guest-3")]);

        var result1 = _orchestrator.Run(_workflow, snapshot, initiateInput, begins: true);
        result1.Snapshot.State.Should().BeOfType<Pending>();

        // Step 2: Only first guest checks out
        var guest1CheckedOut = new GuestCheckedOut("guest-1");
        var result2 = _orchestrator.Run(_workflow, result1.Snapshot, guest1CheckedOut, begins: false);

        result2.Snapshot.State.Should().BeOfType<Pending>();
        var pendingState = (Pending)result2.Snapshot.State;
        pendingState.Guests.First(x => x.Id == "guest-1").GuestStayStatus.Should().Be(GuestStayStatus.Completed);

        // Step 3: Timeout occurs with 2 guests still pending
        var timeout = new TimeoutGroupCheckout("group-123");
        var result3 = _orchestrator.Run(_workflow, result2.Snapshot, timeout, begins: false);

        // Assert final state
        result3.Snapshot.State.Should().BeOfType<Finished>();

        // Verify the timeout command and event
        result3.Commands.Should().HaveCount(2);
        result3.Commands[0].Should().BeOfType<Send<GroupCheckoutOutputMessage>>();
        var sendCommand = (Send<GroupCheckoutOutputMessage>)result3.Commands[0];

        sendCommand.Message.Should().BeOfType<GroupCheckoutTimedOut>();
        var timeoutMessage = (GroupCheckoutTimedOut)sendCommand.Message;
        timeoutMessage.PendingCheckouts.Should().HaveCount(2);
        timeoutMessage.PendingCheckouts.Should().Contain("guest-2");
        timeoutMessage.PendingCheckouts.Should().Contain("guest-3");
    }

    // COMMENTED OUT: GetCheckoutStatus Reply tests
    // These tests were for learning the Reply pattern, but Reply is NOT the right approach
    // for HTTP API status queries. Reply is designed for async workflow-to-workflow communication.
    //
    // For HTTP API status endpoints, use direct stream reads:
    // var messages = await persistence.ReadStreamAsync("group-123");
    // var state = RebuildState(messages);
    // return Ok(state);
    //
    // See ChatStates/REPLY_COMMAND_PATTERNS.md for when to use Reply properly
    //
    // [Fact]
    // public void GetCheckoutStatus_Should_Generate_Reply_Command()
    // {
    //     var snapshot = new WorkflowSnapshot<GroupCheckoutInputMessage, GroupCheckoutState, GroupCheckoutOutputMessage>(
    //         new Pending("group-123",
    //         [
    //             new Guest("guest-1", GuestStayStatus.Completed),
    //             new Guest("guest-2", GuestStayStatus.Failed),
    //             new Guest("guest-3", GuestStayStatus.Pending)
    //         ]),
    //         []
    //     );
    //     var input = new GetCheckoutStatus("group-123");
    //
    //     var result = _orchestrator.Process(_workflow, snapshot, input, begins: false);
    //
    //     result.Commands.Should().HaveCount(1);
    //     result.Commands[0].Should().BeOfType<Reply<GroupCheckoutOutputMessage>>();
    //     // ... rest of assertions
    // }
    //
    // [Fact]
    // public void Evolve_GetCheckoutStatus_Should_Not_Change_State()
    // {
    //     var state = new Pending("group-123", [new Guest("guest-1"), new Guest("guest-2")]);
    //     var receivedEvent = new Received<GroupCheckoutInputMessage, GroupCheckoutOutputMessage>(
    //         new GetCheckoutStatus("group-123"));
    //
    //     var newState = _workflow.Evolve(state, receivedEvent);
    //
    //     newState.Should().Be(state);
    //     newState.Should().BeOfType<Pending>();
    // }
}