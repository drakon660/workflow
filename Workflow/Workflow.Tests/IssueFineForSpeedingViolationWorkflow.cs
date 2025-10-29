namespace Workflow.Tests;

public class IssueFineForSpeedingViolationWorkflow : Workflow<InputMessage, State, OutputMessage>
{
    public override State InitialState => new Initial();

    protected override State InternalEvolve(State state, WorkflowEvent<InputMessage, OutputMessage> workflowEvent)
    {
        return (state, workflowEvent) switch
        {
            // Domain-specific events - change state
            (Initial, InitiatedBy<InputMessage, OutputMessage> { Message: PoliceReportPublished m }) =>
                m.Offense switch
                {
                    SpeedingViolation => new AwaitingSystemNumber(m.PoliceReportId),
                    ParkingViolation => new Final(),
                    _ => throw new InvalidOperationException($"Unknown offense type: {m.Offense}")
                },

            (AwaitingSystemNumber s, Received<InputMessage, OutputMessage> e) when e.Message is TrafficFineSystemNumberGenerated m =>
                new AwaitingManualIdentificationCode(s.PoliceReportId, m.Number),

            (AwaitingManualIdentificationCode, Received<InputMessage, OutputMessage> e) when e.Message is TrafficFineManualIdentificationCodeGenerated =>
                new Final(),

            // Unhandled events - return state unchanged
            _ => state
        };
    }

    public override IReadOnlyList<WorkflowCommand<OutputMessage>> Decide(InputMessage input, State state)
    {
        return (input, state) switch
        {
            (PoliceReportPublished m, Initial) =>
                m.Offense switch
                {
                    SpeedingViolation => new List<WorkflowCommand<OutputMessage>>
                    {
                        new Send<OutputMessage>(new GenerateTrafficFineSystemNumber(m.PoliceReportId))
                    },
                    _ => new List<WorkflowCommand<OutputMessage>> { new Complete<OutputMessage>() }
                },

            (TrafficFineSystemNumberGenerated m, AwaitingSystemNumber s) =>
                new List<WorkflowCommand<OutputMessage>>
                {
                    new Send<OutputMessage>(
                        new GenerateTrafficFineManualIdentificationCode(s.PoliceReportId, m.Number))
                },

            (TrafficFineManualIdentificationCodeGenerated m, AwaitingManualIdentificationCode s) =>
                new List<WorkflowCommand<OutputMessage>>
                {
                    new Send<OutputMessage>(new IssueTrafficFine(m.PoliceReportId, s.SystemNumber, m.Code)),
                    new Complete<OutputMessage>()
                },

            _ => new List<WorkflowCommand<OutputMessage>>()
        };
    }
}

public abstract record State;

public abstract record InputMessage;
public record PoliceReportPublished(string PoliceReportId, Offense Offense) : InputMessage;
public record TrafficFineSystemNumberGenerated(string PoliceReportId, string Number) : InputMessage;
public record TrafficFineManualIdentificationCodeGenerated(string PoliceReportId, string Number, string Code) : InputMessage;

// Output message types
public abstract record OutputMessage;
public record GenerateTrafficFineSystemNumber(string PoliceReportId) : OutputMessage;
public record GenerateTrafficFineManualIdentificationCode(string PoliceReportId, string SystemNumber) : OutputMessage;
public record IssueTrafficFine(string PoliceReportId, string SystemNumber, string ManualIdentificationCode) : OutputMessage;


public record Initial : State;
public record AwaitingSystemNumber(string PoliceReportId) : State;
public record AwaitingManualIdentificationCode(string PoliceReportId, string SystemNumber) : State;
public record Final : State;

public abstract record Offense;
public record SpeedingViolation(string MaximumSpeed) : Offense;
public record ParkingViolation : Offense;