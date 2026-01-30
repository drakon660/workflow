# Workflow Stream Architecture

**Last Updated:** 2026-01-30

**Pattern Implemented:** Process Manager/Orchestrator with Event Sourcing. The system maintains centralized state and coordinates multi-step processes using event-sourced durable execution.

**Infrastructure:** Wolverine handles message routing, command execution, and background processing. The core workflow engine is infrastructure-agnostic.

---

## Table of Contents

1. [Overview](#overview)
2. [The Unified Stream Pattern](#the-unified-stream-pattern)
3. [Component Architecture](#component-architecture)
4. [Wolverine Integration](#wolverine-integration)
5. [Fluent DSL](#fluent-dsl)
6. [Visualization](#visualization)
7. [Complete Flow Examples](#complete-flow-examples)
8. [Implementation Status](#implementation-status)
9. [File Organization](#file-organization)

---

## Overview

Commands are stored in the workflow stream alongside events, creating a unified message stream that serves as both inbox (inputs) and outbox (outputs).

This provides:
- **Complete observability**: Full audit trail in one place
- **Durability**: Commands persisted before execution (crash recovery)
- **Idempotency**: Commands tracked via delivery status (no duplicate execution)
- **Simplicity**: Single storage model for everything
- **Query support**: Reply commands for read-only operations without state mutation

---

## The Unified Stream Pattern

### Stream Structure

```
Workflow Stream for "order-123":
Seq | Kind    | Direction | Message                          | Delivered
----|---------|-----------|----------------------------------|----------
1   | Command | Input     | PlaceOrder                       | N/A
2   | Event   | Output    | Began                            | N/A
3   | Event   | Output    | InitiatedBy(PlaceOrder)          | N/A
4   | Command | Output    | Send(ProcessPayment)             | false  ← Needs delivery
5   | Event   | Output    | Sent(ProcessPayment)             | N/A
6   | Command | Input     | PaymentReceived                  | N/A
7   | Event   | Output    | Received(PaymentReceived)        | N/A
8   | Command | Output    | Send(ShipOrder)                  | true ✓
```

**Key Concepts:**
- **Commands** (Kind=Command) are instructions to execute (Send, Publish, Schedule, Reply)
- **Events** (Kind=Event) are facts that evolve state (via Evolve)
- **Input** (Direction=Input) messages trigger workflow processing
- **Output** (Direction=Output) messages are produced by the workflow
- **DeliveredTime** tracks command execution (idempotency)

### Core Data Structure

**WorkflowMessage** (mutable class for in-memory storage)
```csharp
public class WorkflowMessage
{
    public long SequenceNumber { get; set; }        // Position in stream (1-based)
    public Guid MessageId { get; set; }             // Unique message ID
    public MessageDirection Direction { get; set; }  // Input | Output
    public MessageKind Kind { get; set; }            // Command | Event
    public string MessageType { get; set; }          // Outer type name (e.g., "Sent", "Send")
    public string? InnerMessageType { get; set; }    // Inner message CLR type (AssemblyQualifiedName)
    public string Body { get; set; }                 // JSON-serialized inner message
    public string? AdditionalData { get; set; }      // Extra data (e.g., TimeSpan for Schedule)
    public DateTime RecordedTime { get; set; }       // When stored
    public DateTime? DeliveredTime { get; set; }     // When command was delivered
    public string? DestinationAddress { get; set; }  // Delivery destination (send/reply/publish/schedule/complete)
    public Guid? CorrelationId { get; set; }         // Tracing correlation
    public DateTime? ScheduledTime { get; set; }     // For scheduled commands
    public object? DeserializedBody { get; set; }    // Cached deserialized body (not persisted)
    public bool IsDelivered => DeliveredTime.HasValue;
}
```

### Stream Storage

**WorkflowStream** - Per-workflow in-memory event log with locking:
```csharp
public class WorkflowStream
{
    public string WorkflowId { get; }
    public long? LastProcessedSequence { get; set; }
    public long? LastDeliveredSequence { get; set; }

    public long Append(WorkflowMessage message);
    public List<WorkflowMessage> GetEvents();
    public List<WorkflowMessage> GetPendingOutputs();
    public bool HasAnyEvents();
    public bool HasInput(Guid messageId);
    public List<WorkflowMessage> GetAllMessages();
}
```

**WorkflowStreamRepository** - Manages streams by workflow ID:
```csharp
public class WorkflowStreamRepository
{
    public Task<WorkflowStream> GetOrCreate(string workflowId, CancellationToken ct);
    public Task<WorkflowStream> Lock(string workflowId, CancellationToken ct);
    public WorkflowStream GetAll(string workflowId);
}
```

---

## Component Architecture

### High-Level Flow

```
┌─────────────────────────────────────────────────────────────────┐
│ 1. Input Message (from HTTP/Queue/Kafka/etc.)                   │
│    PlaceOrder, PaymentReceived, CancelOrder, etc.               │
│    → Client calls IWorkflowBus.SendAsync(workflowId, input)     │
└────────────────────────────────┬────────────────────────────────┘
                                 │
                                 ▼
┌─────────────────────────────────────────────────────────────────┐
│ 2. WolverineWorkflowBus                                         │
│    - Resolves workflow type from IWorkflowTypeRegistry           │
│    - Wraps input in WorkflowInputEnvelope                       │
│    - Sends envelope via Wolverine IMessageBus                   │
└────────────────────────────────┬────────────────────────────────┘
                                 │
                                 ▼
┌─────────────────────────────────────────────────────────────────┐
│ 3. WorkflowInputHandler (Wolverine Handler)                     │
│    - Receives WorkflowInputEnvelope from local queue            │
│    - Routes to IWorkflowProcessorFactory.ProcessAsync()         │
│    - Factory resolves correct WorkflowProcessor<T,S,O>          │
└────────────────────────────────┬────────────────────────────────┘
                                 │
                                 ▼
┌─────────────────────────────────────────────────────────────────┐
│ 4. WorkflowProcessor<TInput, TState, TOutput>                   │
│    - Locks workflow stream                                      │
│    - Stores input in inbox (StoreInput)                         │
│    - Rebuilds snapshot from stored events (RebuildSnapshot)     │
│    - Calls WorkflowOrchestrator.Run() → Decide/Translate/Evolve │
│    - Stores output events (StoreEvent) and commands (StoreCommand) │
│    - Optionally delivers pending outputs                        │
└────────────────────────────────┬────────────────────────────────┘
                                 │
                                 ▼
┌─────────────────────────────────────────────────────────────────┐
│ 5. WorkflowStream (In-Memory Storage)                           │
│    - Stores all messages with sequence numbers                  │
│    - Thread-safe via SemaphoreSlim locking                      │
│    - Tracks LastProcessedSequence / LastDeliveredSequence        │
└─────────────────────────────────────────────────────────────────┘
```

### Component Details

#### 1. Workflow Base Classes

**File:** `Workflow/Workflow/Workflow.cs` (118 lines)

```csharp
// Base class with shared functionality
public abstract class WorkflowBase<TInput, TState, TOutput>
{
    public abstract TState InitialState { get; }
    protected abstract TState InternalEvolve(TState state, WorkflowEvent<TInput, TOutput> evt);
    public TState Evolve(TState state, WorkflowEvent<TInput, TOutput> evt);  // Generic + domain
    public virtual IReadOnlyList<WorkflowEvent<TInput, TOutput>> Translate(...); // Commands → Events

    // Helper methods for commands
    protected Reply<TOutput> Reply(TOutput message);
    protected Send<TOutput> Send(TOutput message);
    protected Publish<TOutput> Publish(TOutput message);
    protected Schedule<TOutput> Schedule(TimeSpan after, TOutput message);
    protected Complete<TOutput> Complete();

    // Helper methods for events
    protected Began<TInput, TOutput> Began();
    protected InitiatedBy<TInput, TOutput> InitiatedBy(TInput message);
    protected Received<TInput, TOutput> Received(TInput message);
    // ... etc
}

// Synchronous workflows
public abstract class Workflow<TInput, TState, TOutput>
    : WorkflowBase<TInput, TState, TOutput>, IWorkflow<TInput, TState, TOutput>
{
    public abstract IReadOnlyList<WorkflowCommand<TOutput>> Decide(TInput input, TState state);
}

// Asynchronous workflows with context
public abstract class AsyncWorkflow<TInput, TState, TOutput, TContext>
    : WorkflowBase<TInput, TState, TOutput>, IAsyncWorkflow<TInput, TState, TOutput, TContext>
{
    public abstract Task<IReadOnlyList<WorkflowCommand<TOutput>>>
        DecideAsync(TInput input, TState state, TContext context);
}
```

#### 2. WorkflowOrchestrator

**File:** `Workflow/Workflow/WorkflowOrchestrator.cs` (109 lines)

**Purpose:** Pure orchestration logic (no I/O). Executes the Decide → Translate → Evolve cycle.

```csharp
public class WorkflowOrchestrator<TInput, TState, TOutput>
{
    public OrchestrationResult<TInput, TState, TOutput> Run(
        IWorkflow<TInput, TState, TOutput> workflow,
        WorkflowSnapshot<TInput, TState, TOutput> snapshot,
        TInput message,
        bool begins = false);

    public WorkflowSnapshot<TInput, TState, TOutput> CreateInitialSnapshot(
        IWorkflow<TInput, TState, TOutput> workflow);
}

public class AsyncWorkflowOrchestrator<TInput, TState, TOutput, TContext>
{
    public Task<OrchestrationResult<TInput, TState, TOutput>> RunAsync(
        IAsyncWorkflow<TInput, TState, TOutput, TContext> workflow,
        WorkflowSnapshot<TInput, TState, TOutput> snapshot,
        TInput message,
        TContext context,
        bool begins = false);
}
```

**Records:**
- `WorkflowSnapshot<TInput, TState, TOutput>(TState State, IReadOnlyList<WorkflowEvent> EventHistory)`
- `OrchestrationResult<TInput, TState, TOutput>(Snapshot, Commands, Events)`

#### 3. WorkflowProcessor

**File:** `Workflow/Workflow/InboxOutbox/WorkflowProcessor.cs` (295 lines)

**Purpose:** Bridges orchestration with stream storage. Handles serialization/deserialization.

**Responsibilities:**
1. Lock workflow stream for exclusive access
2. Store input messages in inbox (serialized via System.Text.Json)
3. Rebuild state from stored events (deserialize and replay through Evolve)
4. Call orchestrator to produce commands and events
5. Store output events and commands in stream (outbox)
6. Optionally deliver pending command outputs

**Key Methods:**
- `ProcessAsync(workflowId, input, ct)` - Main processing pipeline
- `StoreInput(stream, input)` - Serialize and append input
- `StoreEvent(stream, evt)` - Extract inner message, serialize, append
- `StoreCommand(stream, command)` - Extract inner message, serialize, append
- `RebuildSnapshot(stream)` - Replay events to reconstruct state
- `DeserializeEvent(message)` / `DeserializeCommand(message)` - Reconstruct typed objects

#### 4. WorkflowCommand & WorkflowEvent Types

**File:** `Workflow/Workflow/WorkflowCommand.cs` (8 lines)
```csharp
public abstract record WorkflowCommand<TOutput>;
public record Reply<TOutput>(TOutput Message) : WorkflowCommand<TOutput>;
public record Send<TOutput>(TOutput Message) : WorkflowCommand<TOutput>;
public record Publish<TOutput>(TOutput Message) : WorkflowCommand<TOutput>;
public record Schedule<TOutput>(TimeSpan After, TOutput Message) : WorkflowCommand<TOutput>;
public record Complete<TOutput> : WorkflowCommand<TOutput>;
```

**File:** `Workflow/Workflow/WorkflowEvent.cs` (12 lines)
```csharp
public abstract record WorkflowEvent<TInput, TOutput>;
public record Began<TInput, TOutput> : WorkflowEvent<TInput, TOutput>;
public record InitiatedBy<TInput, TOutput>(TInput Message) : WorkflowEvent<TInput, TOutput>;
public record Received<TInput, TOutput>(TInput Message) : WorkflowEvent<TInput, TOutput>;
public record Replied<TInput, TOutput>(TOutput Message) : WorkflowEvent<TInput, TOutput>;
public record Sent<TInput, TOutput>(TOutput Message) : WorkflowEvent<TInput, TOutput>;
public record Published<TInput, TOutput>(TOutput Message) : WorkflowEvent<TInput, TOutput>;
public record Scheduled<TInput, TOutput>(TimeSpan After, TOutput Message) : WorkflowEvent<TInput, TOutput>;
public record Completed<TInput, TOutput> : WorkflowEvent<TInput, TOutput>;
```

---

## Wolverine Integration

### Bus Abstraction

**IWorkflowBus** - Clean API for sending inputs to workflows:
```csharp
public interface IWorkflowBus
{
    Task SendAsync<TInput>(string workflowId, TInput input, CancellationToken ct = default);
    Task SendAsync<TInput>(string workflowType, string workflowId, TInput input, CancellationToken ct = default);
}
```

**WolverineWorkflowBus** (`Workflow/InboxOutbox/WolverineWorkflowBus.cs`, 46 lines) - Wraps Wolverine `IMessageBus`, creates `WorkflowInputEnvelope`, and sends via Wolverine local queue.

### Type Registry

**IWorkflowTypeRegistry** - Maps input types to workflow type keys with inheritance support:
```csharp
public interface IWorkflowTypeRegistry
{
    string GetWorkflowType(Type inputType);  // Walks inheritance chain
    void Register(Type inputType, string workflowType);
    bool HasMapping(Type inputType);
}
```

**WorkflowTypeRegistry** (`Workflow/InboxOutbox/IWorkflowBus.cs`, 123 lines) - Supports direct type match, base type chain, and interface lookup.

### Processor Factory

**IWorkflowProcessorFactory** - Resolves processors by workflow type key:
```csharp
public interface IWorkflowProcessorFactory
{
    Task ProcessAsync(string workflowType, string workflowId, object input, CancellationToken ct);
    bool HasProcessor(string workflowType);
}
```

**WorkflowProcessorFactory** (`Workflow/InboxOutbox/IWorkflowProcessorFactory.cs`, 91 lines) - Creates DI scope and delegates to typed `WorkflowProcessorRegistration<TInput, TState, TOutput>`.

### Message Routing

**WorkflowInputEnvelope** (`Workflow/InboxOutbox/WorkflowInputEnvelope.cs`, 33 lines) - Non-generic wrapper for Wolverine routing:
```csharp
public record WorkflowInputEnvelope
{
    public required string WorkflowId { get; init; }
    public required string WorkflowType { get; init; }
    public required object Input { get; init; }
    public string? CorrelationId { get; init; }
    public DateTime CreatedAt { get; init; }
}
```

**WorkflowInputHandler** (`Workflow/InboxOutbox/WorkflowInputHandler.cs`, 57 lines) - Wolverine handler that receives `WorkflowInputEnvelope` and routes to the correct processor via `IWorkflowProcessorFactory`.

### DI Registration

**WorkflowExtensions** (`Workflow/WorkflowExtensions.cs`, 127 lines) - Extension methods for service registration:

```csharp
// Basic infrastructure
services.AddWorkflow<TInput, TState, TOutput>();

// With workflow implementation
services.AddWorkflow<TInput, TState, TOutput, TWorkflow>();

// With Wolverine integration (registers bus, factory, type registry)
services.AddWorkflow<TInput, TState, TOutput, TWorkflow>(workflowType: "OrderProcessing");

// Async workflow
services.AddAsyncWorkflow<TInput, TState, TOutput, TContext, TWorkflow>();
```

### Wolverine Configuration (in API)

```csharp
builder.Host.UseWolverine(opts =>
{
    opts.LocalQueue("workflow-inputs").Sequential();
    opts.PublishMessage<WorkflowInputEnvelope>().ToLocalQueue("workflow-inputs");
    opts.Discovery.IncludeAssembly(typeof(WorkflowInputHandler).Assembly);
});
```

---

## Fluent DSL

**Module:** `Workflow/Workflow/Fluent/` (616 lines)

Declarative API for defining workflows without manual pattern matching:

```csharp
public class MyWorkflow : FluentWorkflow<MyInput, MyState, MyOutput>
{
    public MyWorkflow()
    {
        Initially<InitialState>()
            .On<StartMessage>()
            .Execute(ctx => [Send(new DoSomething())])
            .TransitionTo<ProcessingState>();

        During<ProcessingState>()
            .On<CompletedMessage>()
            .Execute(ctx => [Complete()])
            .TransitionTo<FinishedState>();
    }
}
```

**Files:**
- `FluentWorkflow.cs` (278 lines) - Base class with `Initially<T>()`, `During<T>()`, `ToMermaidStateDiagram()`, `ToMermaidFlowchart()`
- `FluentBuilders.cs` (272 lines) - Builder classes: `StateBuilder`, `TransitionBuilder`, `ConditionalTransitionBuilder`, `ElseBuilder`
- `WorkflowDefinition.cs` (66 lines) - Data records: `WorkflowDefinition`, `StateDefinition`, `TransitionDefinition`, `TransitionContext`

**Features:**
- `On<TMessage>()` - Define transitions per message type
- `Execute(ctx => commands)` - Produce commands
- `TransitionTo<TState>()` / `Stay()` - State transitions
- `If(predicate)` / `Else()` - Conditional transitions
- Built-in Mermaid diagram generation

---

## Visualization

**Module:** `Workflow/Workflow/Visual/` (1,058 lines)

Auto-generates Mermaid diagrams from workflow C# source code using Roslyn analysis.

**Files:**
- `WorkflowAnalyzer.cs` (440 lines) - Roslyn-based parser that extracts state transitions from `InternalEvolve` and decision rules from `Decide`
- `MermaidGenerator.cs` (358 lines) - Generates Mermaid state diagrams and flowcharts from `WorkflowDiagramModel`
- `FlowchartVisualisationBuilder.cs` (203 lines) - Fluent builder API: `WorkflowDiagram.From(sourceCode).GenerateAll()`
- `WorkflowDiagramModel.cs` (57 lines) - Data model: `StateTransition`, `DecisionRule`, `CommandInfo`, `CommandKind`

**Dependencies:** Microsoft.CodeAnalysis.CSharp (Roslyn), MermaidDotNet

---

## Complete Flow Examples

### Step-by-Step: Order Processing via API

**1. HTTP POST /orders**
```csharp
// Carter module creates input and sends via bus
var input = new PlaceOrder(orderId, items);
await workflowBus.SendAsync(orderId, input);
```

**2. WolverineWorkflowBus wraps in envelope**
```csharp
WorkflowInputEnvelope {
    WorkflowId: "order-123",
    WorkflowType: "OrderProcessing",  // Resolved from IWorkflowTypeRegistry
    Input: PlaceOrder(...)
}
// Sent to Wolverine local queue "workflow-inputs"
```

**3. WorkflowInputHandler receives envelope**
```csharp
// Routes to WorkflowProcessorFactory → WorkflowProcessor<OrderProcessingInputMessage, ...>
await processorFactory.ProcessAsync("OrderProcessing", "order-123", input, ct);
```

**4. WorkflowProcessor.ProcessAsync**
```csharp
// Lock stream
var stream = await repository.Lock("order-123", ct);

// Store input in inbox
StoreInput(stream, PlaceOrder(...));  // Seq 1, Direction=Input, Kind=Command

// Rebuild snapshot (empty for new workflow)
var snapshot = RebuildSnapshot(stream);

// Run orchestrator: Decide → Translate → Evolve
var result = orchestrator.Run(workflow, snapshot, input, begins: true);

// Store output events
StoreEvent(stream, Began());                    // Seq 2
StoreEvent(stream, InitiatedBy(PlaceOrder));    // Seq 3
StoreEvent(stream, Sent(ProcessPayment));       // Seq 4

// Store output commands
StoreCommand(stream, Send(ProcessPayment));     // Seq 5, Destination="send"
```

**5. Final Stream State**
```
Seq | Kind    | Direction | MessageType      | InnerMessageType | Delivered
----|---------|-----------|------------------|------------------|----------
1   | Command | Input     | PlaceOrder       | -                | N/A
2   | Event   | Output    | Began            | -                | N/A
3   | Event   | Output    | InitiatedBy      | PlaceOrder       | N/A
4   | Event   | Output    | Sent             | ProcessPayment   | N/A
5   | Command | Output    | Send             | ProcessPayment   | false ← Pending
```

---

## Implementation Status

### Core Framework — Complete

**Workflow Abstractions** (`Workflow.cs`, 118 lines):
- `WorkflowBase<TInput, TState, TOutput>` - Base with Evolve, Translate, helper methods
- `Workflow<TInput, TState, TOutput>` - Synchronous with `Decide`
- `AsyncWorkflow<TInput, TState, TOutput, TContext>` - Async with `DecideAsync`
- Interfaces: `IWorkflowBase`, `IWorkflow`, `IAsyncWorkflow`

**Orchestration** (`WorkflowOrchestrator.cs`, 109 lines):
- `WorkflowOrchestrator<TInput, TState, TOutput>` - Sync
- `AsyncWorkflowOrchestrator<TInput, TState, TOutput, TContext>` - Async with context

**Commands & Events** (`WorkflowCommand.cs` 8 lines, `WorkflowEvent.cs` 12 lines):
- Commands: Reply, Send, Publish, Schedule, Complete
- Events: Began, InitiatedBy, Received, Replied, Sent, Published, Scheduled, Completed

**InboxOutbox Module** (`Workflow/InboxOutbox/`, 811 lines):
- `WorkflowProcessor<TInput, TState, TOutput>` - Core processing with serialization (295 lines)
- `WorkflowStream` - Per-workflow in-memory event log with locking (67 lines)
- `WorkflowStreamRepository` - Stream management by ID (37 lines)
- `WorkflowMessage` - Persistence model (43 lines)
- `WorkflowOutput` / `WorkflowProcessorOptions` - Output delivery config (20 lines)
- `IWorkflowBus` / `WorkflowTypeRegistry` - Bus abstraction and type registry (123 lines)
- `WolverineWorkflowBus` - Wolverine IMessageBus integration (46 lines)
- `WorkflowInputEnvelope` - Non-generic routing wrapper (33 lines)
- `WorkflowInputHandler` - Wolverine message handler (57 lines)
- `IWorkflowProcessorFactory` / `WorkflowProcessorFactory` - Runtime processor resolution (91 lines)

**Fluent DSL** (`Workflow/Fluent/`, 616 lines):
- `FluentWorkflow<TInput, TState, TOutput>` - Declarative workflow definition (278 lines)
- `FluentBuilders` - Builder classes for fluent API (272 lines)
- `WorkflowDefinition` - Data model records (66 lines)

**Visualization** (`Workflow/Visual/`, 1,058 lines):
- `WorkflowAnalyzer` - Roslyn source analysis (440 lines)
- `MermaidGenerator` - Mermaid diagram generation (358 lines)
- `FlowchartVisualisationBuilder` - Fluent builder API (203 lines)
- `WorkflowDiagramModel` - Data model (57 lines)

**DI Registration** (`WorkflowExtensions.cs`, 127 lines):
- `AddWorkflow<TInput, TState, TOutput>()` - Infrastructure only
- `AddWorkflow<TInput, TState, TOutput, TWorkflow>()` - With implementation
- `AddWorkflow<TInput, TState, TOutput, TWorkflow>(workflowType)` - With Wolverine integration
- `AddAsyncWorkflow<TInput, TState, TOutput, TContext, TWorkflow>()` - Async version

**Performance** (`Core/FrugalList.cs`, 84 lines):
- Memory-efficient list for 0-1 items (no allocation for empty, single value for 1 item)

### Sample Workflows

**Cinema** (`Workflow.Samples/Cinema/`, 119 lines):
- `MovieTicketsWorkflow` - Cinema ticket reservation with seat locking and payment (65 lines)
- States: NoTicketState → TicketRequestCreated → SeatsReserved → PaymentConfirmed → TicketPurchasedConfirmed
- Also handles SeatUnavailable path

**Order Processing** (`Workflow.Samples/Order/`, 322 lines):
- `OrderProcessingWorkflow` - Sync workflow (89 lines)
- `OrderProcessingAsyncWorkflow` - Async with `IOrderContext` for inventory checking (131 lines)
- `FluentOrderProcessingWorkflow` - Fluent DSL version (33 lines, partial)
- States: NoOrder → OrderCreated → PaymentConfirmed → Shipped → Delivered
- Also handles cancellation, insufficient inventory, warehouse inventory check

**Group Checkout** (`Workflow.Samples/GroupCheckout/` in Tests, 215 lines):
- `GroupCheckoutWorkflow` - Hotel group checkout coordination (scatter-gather)
- States: NotExisting → Pending → Finished
- Tracks individual guest checkout success/failure

**Speeding Violation** (`Workflow.Tests/`, 82 lines):
- `IssueFineForSpeedingViolationWorkflow` - Simple traffic fine issuance example

### REST API — Working

**Workflow.Samples.Api** (423 lines):
- `Program.cs` (77 lines) - ASP.NET Core setup with Carter, Wolverine, DI registration
- `Modules/OrderModule.cs` (231 lines) - 7 endpoints: create order, get status, payment, shipping, delivery, cancellation
- `Services/WorkflowOutputHandler.cs` (115 lines) - Output message routing factory

**Dependencies:** Carter v10.0.0, WolverineFx v5.12.0, Swagger

### Tests — 67 Tests

**Test Files:**
- `OrderProcessingWorkflowTests.cs` (396 lines) - Sync and async workflow decisions
- `GroupCheckoutWorkflowTests.cs` (488 lines) - Multi-guest checkout scenarios
- `WorkflowOrchestratorTests.cs` (216 lines) - Orchestration pattern
- `WorkflowProcessorTests.cs` (342 lines) - Processor storage, serialization, deserialization
- `IssueFineForSpeedingViolationWorkflowTests.cs` (259 lines) - Conditional logic
- `Visual/WorkflowDiagramTests.cs` (278 lines) - Roslyn diagram generation

**Framework:** xunit.v3, AwesomeAssertions v9.2.1

---

## File Organization

```
Workflow/
├── Workflow/                                    # Core framework library (.NET 10.0)
│   ├── Workflow.cs                              # Base workflow classes (118 lines)
│   │   ├── WorkflowBase<TInput, TState, TOutput>
│   │   ├── Workflow<TInput, TState, TOutput>    # Synchronous
│   │   ├── AsyncWorkflow<TInput, TState, TOutput, TContext> # Async
│   │   └── IWorkflowBase, IWorkflow, IAsyncWorkflow
│   ├── WorkflowOrchestrator.cs                  # Pure orchestration (109 lines)
│   │   ├── WorkflowOrchestrator<TInput, TState, TOutput>
│   │   ├── AsyncWorkflowOrchestrator<TInput, TState, TOutput, TContext>
│   │   ├── WorkflowSnapshot<TInput, TState, TOutput>
│   │   └── OrchestrationResult<TInput, TState, TOutput>
│   ├── WorkflowCommand.cs                       # Command types (8 lines)
│   │   └── Reply, Send, Publish, Schedule, Complete
│   ├── WorkflowEvent.cs                         # Event types (12 lines)
│   │   └── Began, InitiatedBy, Received, Replied, Sent, Published, Scheduled, Completed
│   ├── WorkflowExtensions.cs                    # DI registration (127 lines)
│   │   └── AddWorkflow, AddAsyncWorkflow, AddWorkflowImplementation
│   ├── InboxOutbox/                             # Stream storage & Wolverine integration (811 lines)
│   │   ├── WorkflowProcessor.cs                 # Core processing with serialization (295 lines)
│   │   ├── WorkflowStream.cs                    # In-memory event log with locking (67 lines)
│   │   ├── WorkflowStreamRepository.cs          # Stream management by ID (37 lines)
│   │   ├── WorkflowMessage.cs                   # Persistence model (43 lines)
│   │   ├── WorkflowOutput.cs                    # Output struct + options (20 lines)
│   │   ├── IWorkflowBus.cs                      # Bus interface + WorkflowTypeRegistry (123 lines)
│   │   ├── WolverineWorkflowBus.cs              # Wolverine IMessageBus wrapper (46 lines)
│   │   ├── WorkflowInputEnvelope.cs             # Non-generic routing wrapper (33 lines)
│   │   ├── WorkflowInputHandler.cs              # Wolverine message handler (57 lines)
│   │   └── IWorkflowProcessorFactory.cs         # Runtime processor resolution (91 lines)
│   ├── Fluent/                                  # Fluent DSL (616 lines)
│   │   ├── FluentWorkflow.cs                    # Declarative base class (278 lines)
│   │   ├── FluentBuilders.cs                    # Builder classes (272 lines)
│   │   └── WorkflowDefinition.cs                # Data records (66 lines)
│   ├── Visual/                                  # Roslyn-based diagram generation (1,058 lines)
│   │   ├── WorkflowAnalyzer.cs                  # Source code analysis (440 lines)
│   │   ├── MermaidGenerator.cs                   # Mermaid diagram output (358 lines)
│   │   ├── FlowchartVisualisationBuilder.cs     # Fluent builder API (203 lines)
│   │   └── WorkflowDiagramModel.cs              # Data model (57 lines)
│   └── Core/
│       └── FrugalList.cs                        # Memory-efficient list (84 lines)
│
├── Workflow.Samples/                            # Sample workflows
│   ├── Cinema/                                  # Cinema ticket reservation (119 lines)
│   │   ├── MovieTicketsWorkflow.cs              # Workflow implementation (65 lines)
│   │   ├── InputMessages.cs                     # 5 input message types (22 lines)
│   │   ├── States.cs                            # 6 states (23 lines)
│   │   └── OutputMessages.cs                    # 4 output message types (9 lines)
│   └── Order/                                   # Order processing domain (322 lines)
│       ├── OrderProcessingWorkflow.cs           # Sync workflow (89 lines)
│       ├── OrderProcessingAsyncWorkflow.cs      # Async with IOrderContext (131 lines)
│       ├── FluentOrderProcessingWorkflow.cs     # Fluent DSL version (33 lines)
│       ├── Inputs.cs                            # 9 input message types (22 lines)
│       ├── States.cs                            # 8 states (18 lines)
│       ├── Outputs.cs                           # 12 output message types (22 lines)
│       └── OrderContext.cs                      # Async context interface (27 lines)
│
├── Workflow.Samples.Api/                        # REST API (423 lines)
│   ├── Program.cs                               # ASP.NET Core + Carter + Wolverine setup (77 lines)
│   ├── Modules/OrderModule.cs                   # 7 HTTP endpoints (231 lines)
│   └── Services/WorkflowOutputHandler.cs        # Output delivery routing (115 lines)
│
├── Workflow.Tests/                              # Test suite (67 tests, 2,061 lines)
│   ├── OrderProcessingWorkflowTests.cs          # Sync + async workflow tests (396 lines)
│   ├── GroupCheckoutWorkflowTests.cs            # Multi-guest checkout tests (488 lines)
│   ├── WorkflowOrchestratorTests.cs             # Orchestration pattern tests (216 lines)
│   ├── WorkflowProcessorTests.cs                # Processor serialization tests (342 lines)
│   ├── IssueFineForSpeedingViolationWorkflow.cs # Example workflow (82 lines)
│   ├── IssueFineForSpeedingViolationWorkflowTests.cs # Conditional logic tests (259 lines)
│   ├── GroupCheckoutWorkflow.cs                 # Test workflow (215 lines)
│   └── Visual/WorkflowDiagramTests.cs           # Diagram generation tests (278 lines)
│
└── DiagramDemo/                                 # Quick diagram demo console app (20 lines)
```

**Dependencies (Workflow.csproj):**
- WolverineFx v5.12.0
- MermaidDotNet v0.7.31
- Microsoft.CodeAnalysis.CSharp v4.14.0

**Statistics:**
- Core Framework: ~3,010 lines of production code
- Sample Workflows: ~441 lines
- API: ~423 lines
- Tests: ~2,061 lines with 67 tests
- Target Framework: .NET 10.0
