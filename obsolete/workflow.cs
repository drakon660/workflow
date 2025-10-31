using System;
using System.Collections.Generic;
using System.Linq;

namespace WorkflowEngine
{
    // Workflow command types
    public abstract record WorkflowCommand<TOutput>;
    public record Reply<TOutput>(TOutput Message) : WorkflowCommand<TOutput>;
    public record Send<TOutput>(TOutput Message) : WorkflowCommand<TOutput>;
    public record Publish<TOutput>(TOutput Message) : WorkflowCommand<TOutput>;
    public record Schedule<TOutput>(TimeSpan After, TOutput Message) : WorkflowCommand<TOutput>;
    public record Complete<TOutput> : WorkflowCommand<TOutput>;

    // Workflow trigger - encapsulates message with metadata/headers (context)
    // This is useful when workflow decision-making needs access to metadata like:
    // - Source, MessageName, MessageVersion
    // - Causation and Correlation identifiers
    // - Other headers/metadata
    public class WorkflowTrigger<TInput>
    {
        public TInput Body { get; init; }
        public string Source { get; init; }
        public string MessageName { get; init; }
        public string MessageVersion { get; init; }
        public string CausationId { get; init; }
        public string CorrelationId { get; init; }
        public Dictionary<string, object> Headers { get; init; } = new();

        public WorkflowTrigger(TInput body)
        {
            Body = body;
        }
    }

    // Workflow event types
    public abstract record WorkflowEvent<TInput, TOutput>;
    public record Began<TInput, TOutput> : WorkflowEvent<TInput, TOutput>;
    public record InitiatedBy<TInput, TOutput>(TInput Message) : WorkflowEvent<TInput, TOutput>;
    public record Received<TInput, TOutput>(TInput Message) : WorkflowEvent<TInput, TOutput>;
    public record Replied<TInput, TOutput>(TOutput Message) : WorkflowEvent<TInput, TOutput>;
    public record Sent<TInput, TOutput>(TOutput Message) : WorkflowEvent<TInput, TOutput>;
    public record Published<TInput, TOutput>(TOutput Message) : WorkflowEvent<TInput, TOutput>;
    public record Scheduled<TInput, TOutput>(TimeSpan After, TOutput Message) : WorkflowEvent<TInput, TOutput>;
    public record Completed<TInput, TOutput> : WorkflowEvent<TInput, TOutput>;

    // Workflow definition
    // Note: The Decide function can take either TInput or WorkflowTrigger<TInput>
    // depending on whether you need access to message metadata in decision-making
    public class Workflow<TInput, TState, TOutput>
    {
        public TState InitialState { get; init; }
        public Func<TState, WorkflowEvent<TInput, TOutput>, TState> Evolve { get; init; }
        public Func<TInput, TState, List<WorkflowCommand<TOutput>>> Decide { get; init; }
    }

    // Alternative workflow definition with WorkflowTrigger for metadata-aware decisions
    public class WorkflowWithTrigger<TInput, TState, TOutput>
    {
        public TState InitialState { get; init; }
        public Func<TState, WorkflowEvent<TInput, TOutput>, TState> Evolve { get; init; }
        public Func<WorkflowTrigger<TInput>, TState, List<WorkflowCommand<TOutput>>> Decide { get; init; }
    }

    // Helper class for translating commands to events
    public static class WorkflowTranslator
    {
        public static List<WorkflowEvent<TInput, TOutput>> Translate<TInput, TOutput>(
            bool begins,
            TInput message,
            List<WorkflowCommand<TOutput>> commands)
        {
            var events = new List<WorkflowEvent<TInput, TOutput>>();

            if (begins)
            {
                events.Add(new Began<TInput, TOutput>());
                events.Add(new InitiatedBy<TInput, TOutput>(message));
            }
            else
            {
                events.Add(new Received<TInput, TOutput>(message));
            }

            foreach (var command in commands)
            {
                events.Add(command switch
                {
                    Reply<TOutput> r => new Replied<TInput, TOutput>(r.Message),
                    Send<TOutput> s => new Sent<TInput, TOutput>(s.Message),
                    Publish<TOutput> p => new Published<TInput, TOutput>(p.Message),
                    Schedule<TOutput> sc => new Scheduled<TInput, TOutput>(sc.After, sc.Message),
                    Complete<TOutput> => new Completed<TInput, TOutput>(),
                    _ => throw new InvalidOperationException($"Unknown command type: {command}")
                });
            }

            return events;
        }
    }
}

// Domain-specific implementation for traffic fine workflow
namespace IssueTrafficFineForSpeedingViolationWorkflow
{
    using WorkflowEngine;

    // State types
    public abstract record State;
    public record Initial : State;
    public record AwaitingSystemNumber(string PoliceReportId) : State;
    public record AwaitingManualIdentificationCode(string PoliceReportId, string SystemNumber) : State;
    public record Final : State;

    // Input message types
    public abstract record InputMessage;
    public record PoliceReportPublished(string PoliceReportId, Offense Offense) : InputMessage;
    public record TrafficFineSystemNumberGenerated(string PoliceReportId, string Number) : InputMessage;
    public record TrafficFineManualIdentificationCodeGenerated(string PoliceReportId, string Number, string Code) : InputMessage;

    // Output message types
    public abstract record OutputMessage;
    public record GenerateTrafficFineSystemNumber(string PoliceReportId) : OutputMessage;
    public record GenerateTrafficFineManualIdentificationCode(string PoliceReportId, string SystemNumber) : OutputMessage;
    public record IssueTrafficFine(string PoliceReportId, string SystemNumber, string ManualIdentificationCode) : OutputMessage;

    // Offense types
    public abstract record Offense;
    public record SpeedingViolation(string MaximumSpeed) : Offense;
    public record ParkingViolation : Offense;

    // Workflow implementation
    public static class WorkflowImplementation
    {
        public static State Evolve(State state, WorkflowEvent<InputMessage, OutputMessage> workflowEvent)
        {
            return (state, workflowEvent) switch
            {
                (Initial, InitiatedBy<InputMessage, OutputMessage> e) when e.Message is PoliceReportPublished m =>
                    m.Offense switch
                    {
                        SpeedingViolation v => new AwaitingSystemNumber(m.PoliceReportId),
                        ParkingViolation v => new Final(),
                        _ => throw new InvalidOperationException($"Unknown offense type: {m.Offense}")
                    },

                (AwaitingSystemNumber s, Received<InputMessage, OutputMessage> e) when e.Message is TrafficFineSystemNumberGenerated m =>
                    new AwaitingManualIdentificationCode(s.PoliceReportId, m.Number),

                (AwaitingManualIdentificationCode, Received<InputMessage, OutputMessage> e) when e.Message is TrafficFineManualIdentificationCodeGenerated =>
                    new Final(),

                _ => throw new InvalidOperationException($"{workflowEvent} not supported by {state}")
            };
        }
    }

    // Example usage showing the event sequence
    public static class ExampleEventSequence
    {
        public static List<WorkflowEvent<InputMessage, OutputMessage>> GetSampleEvents()
        {
            return new List<WorkflowEvent<InputMessage, OutputMessage>>
            {
                new Began<InputMessage, OutputMessage>(),

                new InitiatedBy<InputMessage, OutputMessage>(
                    new PoliceReportPublished(
                        "XG.96.L1.5000267/2023",
                        new SpeedingViolation("50km/h")
                    )
                ),

                new Sent<InputMessage, OutputMessage>(
                    new GenerateTrafficFineSystemNumber("XG.96.L1.5000267/2023")
                ),

                new Received<InputMessage, OutputMessage>(
                    new TrafficFineSystemNumberGenerated(
                        "XG.96.L1.5000267/2023",
                        "PPXRG/23TV8457"
                    )
                ),

                new Sent<InputMessage, OutputMessage>(
                    new GenerateTrafficFineManualIdentificationCode(
                        "XG.96.L1.5000267/2023",
                        "PPXRG/23TV8457"
                    )
                ),

                new Received<InputMessage, OutputMessage>(
                    new TrafficFineManualIdentificationCodeGenerated(
                        "XG.96.L1.5000267/2023",
                        "PPXRG/23TV8457",
                        "XMfhyM"
                    )
                ),

                new Sent<InputMessage, OutputMessage>(
                    new IssueTrafficFine(
                        "XG.96.L1.5000267/2023",
                        "PPXRG/23TV8457",
                        "XMfhyM"
                    )
                ),

                new Completed<InputMessage, OutputMessage>()
            };
        }
    }
}
