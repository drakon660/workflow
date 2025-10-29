using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace WorkflowEngine
{
    using IssueTrafficFineForSpeedingViolationWorkflow;
    using IssueTrafficFineForSpeedingViolationWorkflowProcessor;

    /// <summary>
    /// Simple example demonstrating the complete workflow flow:
    /// Input Message → Decide (commands) → Processor (executes) → Translate (events) → Evolve (new state)
    /// </summary>
    public class WorkflowExample
    {
        public static async Task RunExample()
        {
            // Setup processor clients
            WorkflowClients clients = new WorkflowClients
            {
                SystemNumberTopic = null, // Would be actual Pub/Sub topic
                ManualIdentificationCodeTopic = null,
                IssueTrafficFineTopic = null
            };

            // Define the workflow using types from workflow.cs
            Workflow<InputMessage, State, OutputMessage> workflow = new Workflow<InputMessage, State, OutputMessage>
            {
                InitialState = new Initial(),

                // Decide: Input + State → Commands
                Decide = (InputMessage input, State state) => (input, state) switch
                {
                    (PoliceReportPublished m, AwaitingSystemNumber s) =>
                        new List<WorkflowCommand<OutputMessage>>
                        {
                            new Send<OutputMessage>(new GenerateTrafficFineSystemNumber(s.PoliceReportId))
                        },

                    (TrafficFineSystemNumberGenerated m, AwaitingManualIdentificationCode s) =>
                        new List<WorkflowCommand<OutputMessage>>
                        {
                            new Send<OutputMessage>(
                                new GenerateTrafficFineManualIdentificationCode(s.PoliceReportId, s.SystemNumber))
                        },

                    (TrafficFineManualIdentificationCodeGenerated m, Final) =>
                        new List<WorkflowCommand<OutputMessage>>
                        {
                            new Send<OutputMessage>(new IssueTrafficFine(m.PoliceReportId, m.Number, m.Code)),
                            new Complete<OutputMessage>()
                        },

                    _ => new List<WorkflowCommand<OutputMessage>>()
                },

                // Evolve: State + Event → New State
                Evolve = (State state, WorkflowEvent<InputMessage, OutputMessage> evt) =>
                    WorkflowImplementation.Evolve(state, evt)
            };

            Console.WriteLine("=== Workflow Flow Example ===\n");

            // ========== Step 1: Police Report Published ==========
            Console.WriteLine("--- Step 1: Police Report Published ---");

            State currentState = workflow.InitialState;
            Console.WriteLine($"Current State: {currentState.GetType().Name}");

            InputMessage message1 = new PoliceReportPublished(
                "XG.96.L1.5000267/2023",
                new SpeedingViolation("50km/h"));
            Console.WriteLine($"Input Message: {message1.GetType().Name}");

            // Decide what commands to execute
            List<WorkflowCommand<OutputMessage>> commands1 = workflow.Decide(message1, currentState);
            Console.WriteLine($"Commands Decided: {commands1.Count}");

            // Execute commands via Processor (publishes to Pub/Sub)
            foreach (WorkflowCommand<OutputMessage> command in commands1)
            {
                Console.WriteLine($"  - Executing: {command.GetType().Name}");
                // await Processor.Handle(clients, "wf-123", "msg-001", command);
            }

            // Translate to events
            bool isInitiating = true;
            List<WorkflowEvent<InputMessage, OutputMessage>> events1 =
                WorkflowTranslator.Translate(isInitiating, message1, commands1);
            Console.WriteLine($"Events Generated: {events1.Count}");
            foreach (WorkflowEvent<InputMessage, OutputMessage> evt in events1)
            {
                Console.WriteLine($"  - {evt.GetType().Name}");
            }

            // Evolve state based on events
            State newState1 = currentState;
            foreach (WorkflowEvent<InputMessage, OutputMessage> evt in events1)
            {
                newState1 = workflow.Evolve(newState1, evt);
            }
            Console.WriteLine($"New State: {newState1.GetType().Name}\n");

            // ========== Step 2: System Number Generated ==========
            Console.WriteLine("--- Step 2: System Number Generated ---");

            currentState = newState1;
            Console.WriteLine($"Current State: {currentState.GetType().Name}");

            InputMessage message2 = new TrafficFineSystemNumberGenerated(
                "XG.96.L1.5000267/2023",
                "PPXRG/23TV8457");
            Console.WriteLine($"Input Message: {message2.GetType().Name}");

            // Decide what commands to execute
            List<WorkflowCommand<OutputMessage>> commands2 = workflow.Decide(message2, currentState);
            Console.WriteLine($"Commands Decided: {commands2.Count}");

            // Execute commands via Processor
            foreach (WorkflowCommand<OutputMessage> command in commands2)
            {
                Console.WriteLine($"  - Executing: {command.GetType().Name}");
                // await Processor.Handle(clients, "wf-123", "msg-002", command);
            }

            // Translate to events
            List<WorkflowEvent<InputMessage, OutputMessage>> events2 =
                WorkflowTranslator.Translate(false, message2, commands2);
            Console.WriteLine($"Events Generated: {events2.Count}");
            foreach (WorkflowEvent<InputMessage, OutputMessage> evt in events2)
            {
                Console.WriteLine($"  - {evt.GetType().Name}");
            }

            // Evolve state based on events
            State newState2 = currentState;
            foreach (WorkflowEvent<InputMessage, OutputMessage> evt in events2)
            {
                newState2 = workflow.Evolve(newState2, evt);
            }
            Console.WriteLine($"New State: {newState2.GetType().Name}\n");

            // ========== Step 3: Manual ID Code Generated ==========
            Console.WriteLine("--- Step 3: Manual ID Code Generated ---");

            currentState = newState2;
            Console.WriteLine($"Current State: {currentState.GetType().Name}");

            InputMessage message3 = new TrafficFineManualIdentificationCodeGenerated(
                "XG.96.L1.5000267/2023",
                "PPXRG/23TV8457",
                "XMfhyM");
            Console.WriteLine($"Input Message: {message3.GetType().Name}");

            // Decide what commands to execute
            List<WorkflowCommand<OutputMessage>> commands3 = workflow.Decide(message3, currentState);
            Console.WriteLine($"Commands Decided: {commands3.Count}");

            // Execute commands via Processor
            foreach (WorkflowCommand<OutputMessage> command in commands3)
            {
                Console.WriteLine($"  - Executing: {command.GetType().Name}");
                if (command is Send<OutputMessage> send)
                {
                    Console.WriteLine($"    Message: {send.Message.GetType().Name}");
                }
                // await Processor.Handle(clients, "wf-123", "msg-003", command);
            }

            // Translate to events
            List<WorkflowEvent<InputMessage, OutputMessage>> events3 =
                WorkflowTranslator.Translate(false, message3, commands3);
            Console.WriteLine($"Events Generated: {events3.Count}");
            foreach (WorkflowEvent<InputMessage, OutputMessage> evt in events3)
            {
                Console.WriteLine($"  - {evt.GetType().Name}");
            }

            // Evolve state based on events
            State newState3 = currentState;
            foreach (WorkflowEvent<InputMessage, OutputMessage> evt in events3)
            {
                newState3 = workflow.Evolve(newState3, evt);
            }
            Console.WriteLine($"New State: {newState3.GetType().Name}");

            Console.WriteLine("\n=== Workflow Complete ===");
        }

        /// <summary>
        /// Demonstrates the relationship between Commands, Events, and State
        /// </summary>
        public static void ExplainFlow()
        {
            Console.WriteLine(@"
=== Workflow Flow Explanation ===

1. INPUT MESSAGE arrives
   ↓
2. DECIDE function: (Input, State) → Commands
   - Business logic determines what to do
   - Returns list of commands (Send, Reply, Publish, Schedule, Complete)
   ↓
3. PROCESSOR executes commands
   - Send commands → Publish to Pub/Sub topics
   - Side effects happen here (external systems called)
   ↓
4. TRANSLATE commands → Events
   - Commands are converted to events for audit trail
   - Send → Sent, Reply → Replied, Complete → Completed
   ↓
5. EVOLVE function: (State, Event) → New State
   - State machine transitions based on events
   - New state is persisted
   ↓
6. Loop back to step 1 when next message arrives

Key Insight:
- Commands = What we WANT to do (intent)
- Events = What we DID (fact)
- State = Where we ARE (current position in workflow)
");
        }
    }
}
