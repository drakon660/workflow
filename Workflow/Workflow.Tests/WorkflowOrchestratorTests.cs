using AwesomeAssertions;

namespace Workflow.Tests;

/// <summary>
/// Demonstrates how to use the WorkflowOrchestrator to centralize workflow processing logic.
/// The orchestrator handles the complete cycle: Decide -> Translate -> Append -> Evolve -> Persist
/// </summary>
public class WorkflowOrchestratorTests
{
    private readonly WorkflowOrchestrator<GroupCheckoutInputMessage, GroupCheckoutState, GroupCheckoutOutputMessage> _orchestrator = new();
    private readonly GroupCheckoutWorkflow _workflow = new();

    [Fact]
    public void CreateInitialSnapshot_Should_Return_Snapshot_With_Initial_State_And_Empty_History()
    {
        // Act
        var snapshot = _orchestrator.CreateInitialSnapshot(_workflow);

        // Assert
        snapshot.State.Should().BeOfType<NotExisting>();
        snapshot.EventHistory.Should().BeEmpty();
    }

    [Fact]
    public void Process_When_Beginning_Workflow_Should_Create_Events_And_Evolve_State()
    {
        // Arrange
        var initialSnapshot = _orchestrator.CreateInitialSnapshot(_workflow);
        var message = new InitiateGroupCheckout("group-123", [new Guest("guest-1"), new Guest("guest-2")]);

        // Act - Process the initiating message
        var result = _orchestrator.Process(_workflow, initialSnapshot, message, begins: true);

        // Assert - New snapshot has updated state
        result.NewSnapshot.State.Should().BeOfType<Pending>();
        var pendingState = (Pending)result.NewSnapshot.State;
        pendingState.GroupCheckoutId.Should().Be("group-123");
        pendingState.Guests.Should().HaveCount(2);

        // Assert - Commands were generated
        result.Commands.Should().HaveCount(2);
        result.Commands.Should().AllBeOfType<Send<GroupCheckoutOutputMessage>>();

        // Assert - Events were generated and appended
        result.NewEvents.Should().HaveCount(4); // Began + InitiatedBy + 2x Sent
        result.NewEvents[0].Should().BeOfType<Began<GroupCheckoutInputMessage, GroupCheckoutOutputMessage>>();
        result.NewEvents[1].Should().BeOfType<InitiatedBy<GroupCheckoutInputMessage, GroupCheckoutOutputMessage>>();
        result.NewEvents[2].Should().BeOfType<Sent<GroupCheckoutInputMessage, GroupCheckoutOutputMessage>>();
        result.NewEvents[3].Should().BeOfType<Sent<GroupCheckoutInputMessage, GroupCheckoutOutputMessage>>();

        // Assert - Event history includes all events
        result.NewSnapshot.EventHistory.Should().HaveCount(4);
    }

    [Fact]
    public void Process_When_Receiving_Message_Should_Append_To_Event_History()
    {
        // Arrange - Start with a workflow in Pending state
        var snapshot = new WorkflowSnapshot<GroupCheckoutInputMessage, GroupCheckoutState, GroupCheckoutOutputMessage>(
            State: new Pending("group-123", [new Guest("guest-1"), new Guest("guest-2")]),
            EventHistory: new List<WorkflowEvent<GroupCheckoutInputMessage, GroupCheckoutOutputMessage>>
            {
                new Began<GroupCheckoutInputMessage, GroupCheckoutOutputMessage>(),
                new InitiatedBy<GroupCheckoutInputMessage, GroupCheckoutOutputMessage>(
                    new InitiateGroupCheckout("group-123", [new Guest("guest-1"), new Guest("guest-2")]))
            }
        );

        var message = new GuestCheckedOut("guest-1");

        // Act - Process the incoming message
        var result = _orchestrator.Process(_workflow, snapshot, message, begins: false);

        // Assert - State evolved correctly
        result.NewSnapshot.State.Should().BeOfType<Pending>();
        var pendingState = (Pending)result.NewSnapshot.State;
        pendingState.Guests.First(g => g.Id == "guest-1").GuestStayStatus.Should().Be(GuestStayStatus.Completed);
        pendingState.Guests.First(g => g.Id == "guest-2").GuestStayStatus.Should().Be(GuestStayStatus.Pending);

        // Assert - No commands generated (workflow still pending)
        result.Commands.Should().BeEmpty();

        // Assert - New event was added
        result.NewEvents.Should().HaveCount(1);
        result.NewEvents[0].Should().BeOfType<Received<GroupCheckoutInputMessage, GroupCheckoutOutputMessage>>();

        // Assert - Event history accumulated correctly
        result.NewSnapshot.EventHistory.Should().HaveCount(3); // Original 2 + new 1
    }

    [Fact]
    public void Process_Complete_Workflow_Demonstrates_Full_Orchestration_Pattern()
    {
        // This test demonstrates the complete orchestration pattern with persistence

        // Step 1: Create initial snapshot for new workflow
        var snapshot = _orchestrator.CreateInitialSnapshot(_workflow);

        // Step 2: Process initiating message
        var initiateMessage = new InitiateGroupCheckout("group-123", [new Guest("guest-1"), new Guest("guest-2")]);
        var result1 = _orchestrator.Process(_workflow, snapshot, initiateMessage, begins: true);

        // At this point, you would:
        // - Execute the commands (result1.Commands)
        // - Persist the snapshot (result1.NewSnapshot) to database/event store
        result1.Commands.Should().HaveCount(2); // CheckOut commands for both guests
        result1.NewSnapshot.EventHistory.Should().HaveCount(4);

        // Step 3: Process first guest checkout
        var guest1CheckedOut = new GuestCheckedOut("guest-1");
        var result2 = _orchestrator.Process(_workflow, result1.NewSnapshot, guest1CheckedOut, begins: false);

        // No commands yet (still waiting for guest-2)
        result2.Commands.Should().BeEmpty();
        result2.NewSnapshot.EventHistory.Should().HaveCount(5); // Previous 4 + new 1

        // Step 4: Process second guest checkout - workflow completes
        var guest2CheckedOut = new GuestCheckedOut("guest-2");
        var result3 = _orchestrator.Process(_workflow, result2.NewSnapshot, guest2CheckedOut, begins: false);

        // Commands generated for completion
        result3.Commands.Should().HaveCount(2); // Send + Complete
        result3.Commands[0].Should().BeOfType<Send<GroupCheckoutOutputMessage>>();
        result3.Commands[1].Should().BeOfType<Complete<GroupCheckoutOutputMessage>>();

        // Final state
        result3.NewSnapshot.State.Should().BeOfType<Finished>();
        result3.NewSnapshot.EventHistory.Should().HaveCount(8); // Complete event history

        // Verify we can rebuild state from event history
        var rebuiltState = _workflow.InitialState;
        foreach (var evt in result3.NewSnapshot.EventHistory)
        {
            rebuiltState = _workflow.Evolve(rebuiltState, evt);
        }
        rebuiltState.Should().Be(result3.NewSnapshot.State);
    }

    [Fact]
    public void Process_With_Timeout_Shows_Orchestrator_Handles_All_Scenarios()
    {
        // Arrange - Workflow with some guests still pending
        var snapshot = new WorkflowSnapshot<GroupCheckoutInputMessage, GroupCheckoutState, GroupCheckoutOutputMessage>(
            State: new Pending("group-123",
                [new Guest("guest-1", GuestStayStatus.Failed),
                 new Guest("guest-2", GuestStayStatus.Pending),
                 new Guest("guest-3", GuestStayStatus.Pending)]),
            EventHistory: new List<WorkflowEvent<GroupCheckoutInputMessage, GroupCheckoutOutputMessage>>()
        );

        var timeoutMessage = new TimeoutGroupCheckout("group-123");

        // Act
        var result = _orchestrator.Process(_workflow, snapshot, timeoutMessage, begins: false);

        // Assert - Workflow completes with timeout
        result.NewSnapshot.State.Should().BeOfType<Finished>();
        result.Commands.Should().HaveCount(2); // Send + Complete

        var sendCommand = (Send<GroupCheckoutOutputMessage>)result.Commands[0];
        sendCommand.Message.Should().BeOfType<GroupCheckoutTimedOut>();

        var timeoutEvent = (GroupCheckoutTimedOut)sendCommand.Message;
        timeoutEvent.PendingCheckouts.Should().HaveCount(2);
        timeoutEvent.PendingCheckouts.Should().Contain("guest-2");
        timeoutEvent.PendingCheckouts.Should().Contain("guest-3");
    }

    [Fact]
    public async Task Orchestrator_Centralizes_All_Workflow_Processing_Logic()
    {
        var workflow = new GroupCheckoutWorkflow();
        string workflowId = "1";
        var persistance = new InMemoryWorkflowPersistence<GroupCheckoutInputMessage, GroupCheckoutState, GroupCheckoutOutputMessage>();
        var orchestrator = new WorkflowOrchestrator<GroupCheckoutInputMessage, GroupCheckoutState, GroupCheckoutOutputMessage>();
        var streamProcessor = new WorkflowStreamProcessor<GroupCheckoutInputMessage, GroupCheckoutState, GroupCheckoutOutputMessage>(orchestrator, persistance);
        // This test demonstrates that the orchestrator:
        // (a) Calls Decide
        // (b) Translates commands to events
        // (c) Appends events to history
        // (d) Evolves state
        // (e) Returns everything needed for persistence

        var message = new InitiateGroupCheckout("group-123", [new Guest("guest-1")]);

        //var result = _orchestrator.Process(_workflow, snapshot, message, begins: true);

        var result = await streamProcessor.ProcessAsync(workflow, workflowId, message, true);
        // Verify all orchestration steps occurred:

        // (a) Decide was called - commands generated
        // result.Commands.Should().NotBeEmpty();
        //
        // // (b) Translate was called - events match command pattern
        // result.NewEvents.Should().Contain(e => e is Sent<GroupCheckoutInputMessage, GroupCheckoutOutputMessage>);
        //
        // // (c) Events appended to history
        // result.NewSnapshot.EventHistory.Should().HaveCount(result.NewEvents.Count);
        //
        // // (d) State evolved from initial
        // result.NewSnapshot.State.Should().NotBe(snapshot.State);
        //
        // // (e) New snapshot ready for persistence
        // result.NewSnapshot.Should().NotBeNull();
        // result.NewSnapshot.State.Should().BeOfType<Pending>();
    }
}
