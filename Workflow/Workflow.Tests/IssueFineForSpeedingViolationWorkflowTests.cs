using AwesomeAssertions;

namespace Workflow.Tests;

public class IssueFineForSpeedingViolationWorkflowTests
{
    [Fact]
    public void Check_ParkingViolation_Is_Starting_From_Initial_State()
    {
        var policeFineWorkflow = new IssueFineForSpeedingViolationWorkflow();
        policeFineWorkflow.InitialState.Should().BeOfType<Initial>();
    }

    [Fact]
    public void Check_ParkingViolation_Police_Report_Published()
    {
        var policeFineWorkflow = new IssueFineForSpeedingViolationWorkflow();
        var state = policeFineWorkflow.InitialState;

        InputMessage message1 = new PoliceReportPublished(
            "XG.96.L1.5000267/2023",
            new SpeedingViolation("50km/h"));

        var commands = policeFineWorkflow.Decide(message1, state);
        var events = policeFineWorkflow.Translate(true, message1, commands);

        foreach (var @event in events)
            state = policeFineWorkflow.Evolve(state, @event);

        state.Should().BeOfType<AwaitingSystemNumber>();
    }

    [Fact]
    public void SpeedingViolation_Step1_Generates_Correct_Command()
    {
        var workflow = new IssueFineForSpeedingViolationWorkflow();
        var state = workflow.InitialState;

        InputMessage message = new PoliceReportPublished(
            "XG.96.L1.5000267/2023",
            new SpeedingViolation("50km/h"));

        var commands = workflow.Decide(message, state);

        commands.Should().HaveCount(1);
        commands[0].Should().BeOfType<Send<OutputMessage>>();
        var sendCommand = (Send<OutputMessage>)commands[0];
        sendCommand.Message.Should().BeOfType<GenerateTrafficFineSystemNumber>();
        var generateMessage = (GenerateTrafficFineSystemNumber)sendCommand.Message;
        generateMessage.PoliceReportId.Should().Be("XG.96.L1.5000267/2023");
    }

    [Fact]
    public void SpeedingViolation_Step2_Generates_Correct_Command()
    {
        var workflow = new IssueFineForSpeedingViolationWorkflow();
        var state = new AwaitingSystemNumber("XG.96.L1.5000267/2023");

        InputMessage message = new TrafficFineSystemNumberGenerated(
            "XG.96.L1.5000267/2023",
            "SN-12345");

        var commands = workflow.Decide(message, state);

        commands.Should().HaveCount(1);
        commands[0].Should().BeOfType<Send<OutputMessage>>();
        var sendCommand = (Send<OutputMessage>)commands[0];
        sendCommand.Message.Should().BeOfType<GenerateTrafficFineManualIdentificationCode>();
        var generateMessage = (GenerateTrafficFineManualIdentificationCode)sendCommand.Message;
        generateMessage.PoliceReportId.Should().Be("XG.96.L1.5000267/2023");
        generateMessage.SystemNumber.Should().Be("SN-12345");
    }

    [Fact]
    public void SpeedingViolation_Step3_Generates_Correct_Commands()
    {
        var workflow = new IssueFineForSpeedingViolationWorkflow();
        var state = new AwaitingManualIdentificationCode("XG.96.L1.5000267/2023", "SN-12345");

        InputMessage message = new TrafficFineManualIdentificationCodeGenerated(
            "XG.96.L1.5000267/2023",
            "SN-12345",
            "CODE-789");

        var commands = workflow.Decide(message, state);

        commands.Should().HaveCount(2);
        commands[0].Should().BeOfType<Send<OutputMessage>>();
        var sendCommand = (Send<OutputMessage>)commands[0];
        sendCommand.Message.Should().BeOfType<IssueTrafficFine>();
        var issueMessage = (IssueTrafficFine)sendCommand.Message;
        issueMessage.PoliceReportId.Should().Be("XG.96.L1.5000267/2023");
        issueMessage.SystemNumber.Should().Be("SN-12345");
        issueMessage.ManualIdentificationCode.Should().Be("CODE-789");

        commands[1].Should().BeOfType<Complete<OutputMessage>>();
    }

    [Fact]
    public void SpeedingViolation_Full_Workflow_Happy_Path()
    {
        var workflow = new IssueFineForSpeedingViolationWorkflow();
        var eventStore = new List<WorkflowEvent<InputMessage, OutputMessage>>();

        // Step 1: Police report published
        InputMessage message1 = new PoliceReportPublished(
            "XG.96.L1.5000267/2023",
            new SpeedingViolation("50km/h"));
        
        var workflowOrchestrator = new WorkflowOrchestrator<InputMessage, State, OutputMessage>();

        var snapshot = workflowOrchestrator.CreateInitialSnapshot(workflow);

        var result = workflowOrchestrator.Process(workflow, snapshot, message1, true);

        eventStore.AddRange(result.Events);
        
        result.Snapshot.State.Should().BeOfType<AwaitingSystemNumber>();
        var awaitingSystemNumber = (AwaitingSystemNumber)result.Snapshot.State;
        awaitingSystemNumber.PoliceReportId.Should().Be("XG.96.L1.5000267/2023");

        // Step 2: System number generated
        InputMessage message2 = new TrafficFineSystemNumberGenerated(
            "XG.96.L1.5000267/2023",
            "SN-12345");
        
        var nextResult = workflowOrchestrator.Process(workflow, result.Snapshot, message2);
        
        eventStore.AddRange(nextResult.Events);
        
        nextResult.Snapshot.State.Should().BeOfType<AwaitingManualIdentificationCode>();
        var awaitingCode = (AwaitingManualIdentificationCode)nextResult.Snapshot.State;
        awaitingCode.PoliceReportId.Should().Be("XG.96.L1.5000267/2023");
        awaitingCode.SystemNumber.Should().Be("SN-12345");

        // Step 3: Manual identification code generated
        InputMessage message3 = new TrafficFineManualIdentificationCodeGenerated(
            "XG.96.L1.5000267/2023",
            "SN-12345",
            "CODE-789");

        var nextResult1 = workflowOrchestrator.Process(workflow, nextResult.Snapshot, message3);
        
        eventStore.AddRange(nextResult1.Events);
        
        nextResult1.Snapshot.State.Should().BeOfType<Final>();
        //
        // // Validate all events are present in correct order
        eventStore.Should().HaveCount(8);
        
        // Step 1 events (3 events: Began, InitiatedBy, Sent)
        eventStore[0].Should().BeOfType<Began<InputMessage, OutputMessage>>();
        
        eventStore[1].Should().BeOfType<InitiatedBy<InputMessage, OutputMessage>>();
        var initiatedBy = (InitiatedBy<InputMessage, OutputMessage>)eventStore[1];
        initiatedBy.Message.Should().BeOfType<PoliceReportPublished>();
        
        eventStore[2].Should().BeOfType<Sent<InputMessage, OutputMessage>>();
        var sent1 = (Sent<InputMessage, OutputMessage>)eventStore[2];
        sent1.Message.Should().BeOfType<GenerateTrafficFineSystemNumber>();
        
        // Step 2 events (2 events: Received, Sent)
        eventStore[3].Should().BeOfType<Received<InputMessage, OutputMessage>>();
        var received1 = (Received<InputMessage, OutputMessage>)eventStore[3];
        received1.Message.Should().BeOfType<TrafficFineSystemNumberGenerated>();
        
        eventStore[4].Should().BeOfType<Sent<InputMessage, OutputMessage>>();
        var sent2 = (Sent<InputMessage, OutputMessage>)eventStore[4];
        sent2.Message.Should().BeOfType<GenerateTrafficFineManualIdentificationCode>();
        
        // Step 3 events (3 events: Received, Sent, Completed)
        eventStore[5].Should().BeOfType<Received<InputMessage, OutputMessage>>();
        var received2 = (Received<InputMessage, OutputMessage>)eventStore[5];
        received2.Message.Should().BeOfType<TrafficFineManualIdentificationCodeGenerated>();
        
        eventStore[6].Should().BeOfType<Sent<InputMessage, OutputMessage>>();
        var sent3 = (Sent<InputMessage, OutputMessage>)eventStore[6];
        sent3.Message.Should().BeOfType<IssueTrafficFine>();
        var issueFine = (IssueTrafficFine)sent3.Message;
        issueFine.PoliceReportId.Should().Be("XG.96.L1.5000267/2023");
        issueFine.SystemNumber.Should().Be("SN-12345");
        issueFine.ManualIdentificationCode.Should().Be("CODE-789");
        
        eventStore[7].Should().BeOfType<Completed<InputMessage, OutputMessage>>();
        
        // Bonus: Verify we can reconstruct state from events
        var reconstructedState = workflow.InitialState;
        foreach (var @event in eventStore)
        {
            reconstructedState = workflow.Evolve(reconstructedState, @event);
        }
        reconstructedState.Should().BeOfType<Final>();
    }

    [Fact]
    public void ParkingViolation_Completes_Immediately()
    {
        var workflow = new IssueFineForSpeedingViolationWorkflow();
        var state = workflow.InitialState;

        InputMessage message = new PoliceReportPublished(
            "XG.96.L1.5000267/2023",
            new ParkingViolation());

        var commands = workflow.Decide(message, state);

        commands.Should().HaveCount(1);
        commands[0].Should().BeOfType<Complete<OutputMessage>>();

        var events = workflow.Translate(true, message, commands);

        foreach (var @event in events)
            state = workflow.Evolve(state, @event);

        state.Should().BeOfType<Final>();
    }

    [Fact]
    public void Translate_Generates_Correct_Events_For_Begin()
    {
        var workflow = new IssueFineForSpeedingViolationWorkflow();

        InputMessage message = new PoliceReportPublished(
            "XG.96.L1.5000267/2023",
            new SpeedingViolation("50km/h"));

        var commands = new List<WorkflowCommand<OutputMessage>>
        {
            new Send<OutputMessage>(new GenerateTrafficFineSystemNumber("XG.96.L1.5000267/2023"))
        };

        var events = workflow.Translate(true, message, commands);

        events.Should().HaveCount(3);
        events[0].Should().BeOfType<Began<InputMessage, OutputMessage>>();
        events[1].Should().BeOfType<InitiatedBy<InputMessage, OutputMessage>>();
        events[2].Should().BeOfType<Sent<InputMessage, OutputMessage>>();
    }

    [Fact]
    public void Translate_Generates_Correct_Events_For_Receive()
    {
        var workflow = new IssueFineForSpeedingViolationWorkflow();

        InputMessage message = new TrafficFineSystemNumberGenerated(
            "XG.96.L1.5000267/2023",
            "SN-12345");

        var commands = new List<WorkflowCommand<OutputMessage>>
        {
            new Send<OutputMessage>(new GenerateTrafficFineManualIdentificationCode("XG.96.L1.5000267/2023", "SN-12345"))
        };

        var events = workflow.Translate(false, message, commands);

        events.Should().HaveCount(2);
        events[0].Should().BeOfType<Received<InputMessage, OutputMessage>>();
        events[1].Should().BeOfType<Sent<InputMessage, OutputMessage>>();
    }
}