# Workflow Implementation State

**Last Updated:** 2025-01-26

**Architecture Update:** Wolverine integration completed. Using Wolverine local queues as the messaging infrastructure with generic routing.

---

## Current Status: Phase 2 In Progress ⏳

Core workflow orchestration framework complete. Wolverine integration for input processing implemented.

---

## What's Implemented ✅

### 1. Core Framework Components

#### **WorkflowOrchestrator** ✅
- Pure orchestration logic (no I/O)
- Decide → Translate → Evolve cycle
- Event history tracking
- Snapshot management
- Fully tested with 46 passing tests

#### **Workflow Abstractions** ✅
```csharp
// Base class with shared functionality
public abstract class WorkflowBase<TInput, TState, TOutput>
{
    public abstract TState InitialState { get; }
    protected abstract TState InternalEvolve(TState state, WorkflowEvent<TInput, TOutput> workflowEvent);
}

// Synchronous workflows
public abstract class Workflow<TInput, TState, TOutput> : WorkflowBase<TInput, TState, TOutput>
{
    public abstract IReadOnlyList<WorkflowCommand<TOutput>> Decide(TInput input, TState state);
}

// Asynchronous workflows with typed context
public abstract class AsyncWorkflow<TInput, TState, TOutput, TContext> : WorkflowBase<TInput, TState, TOutput>
{
    public abstract Task<IReadOnlyList<WorkflowCommand<TOutput>>> DecideAsync(TInput input, TState state, TContext context);
}
```

#### **Command Types** ✅
- `Send<TOutput>` - Send message to specific handler
- `Publish<TOutput>` - Publish event to subscribers
- `Schedule<TOutput>` - Schedule delayed message
- `Reply<TOutput>` - Reply to caller (query operations)
- `Complete<TOutput>` - Mark workflow as complete

#### **Event Types** ✅
- `Began` - Workflow started
- `InitiatedBy` - Input that started workflow
- `Received` - Input received (continuation)
- `Sent` - Command sent
- `Published` - Event published
- `Scheduled` - Delayed message scheduled
- `Replied` - Reply sent (query operations)
- `Completed` - Workflow completed

### 2. Wolverine Integration ✅ NEW

#### **Architecture Overview**

```
┌─────────────────────────────────────────────────────────────────────┐
│  Wolverine-Based Input Processing                                   │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  API / External                                                     │
│       │                                                             │
│       │  await workflowBus.SendAsync(orderId, input)               │
│       ▼                                                             │
│  ┌─────────────────┐                                               │
│  │  IWorkflowBus   │  ◄── Clean abstraction (no envelope needed)   │
│  │  (interface)    │                                               │
│  └────────┬────────┘                                               │
│           │                                                         │
│           ▼                                                         │
│  ┌─────────────────────┐                                           │
│  │ WolverineWorkflowBus │  ◄── Creates envelope internally         │
│  │  - Type registry lookup                                          │
│  │  - Inheritance support                                           │
│  └────────┬─────────────┘                                          │
│           │                                                         │
│           ▼                                                         │
│  ┌─────────────────────┐                                           │
│  │ WorkflowInputEnvelope│  ◄── Internal message (hidden from API)  │
│  │  - WorkflowId        │                                          │
│  │  - WorkflowType      │                                          │
│  │  - Input (object)    │                                          │
│  └────────┬─────────────┘                                          │
│           │                                                         │
│           ▼                                                         │
│  ┌─────────────────────────┐                                       │
│  │ Wolverine Local Queue   │  "workflow-inputs"                    │
│  │  - Sequential processing │                                       │
│  │  - Retry/error handling  │                                       │
│  └────────┬─────────────────┘                                      │
│           │                                                         │
│           ▼                                                         │
│  ┌─────────────────────┐                                           │
│  │ WorkflowInputHandler │  ◄── ROUTER (RFC role)                   │
│  │  - Routes by WorkflowType                                        │
│  │  - Resolves processor                                            │
│  └────────┬─────────────┘                                          │
│           │                                                         │
│           ▼                                                         │
│  ┌───────────────────────────┐                                     │
│  │ IWorkflowProcessorFactory │  ◄── Resolves by type key           │
│  └────────┬──────────────────┘                                     │
│           │                                                         │
│           ▼                                                         │
│  ┌─────────────────────────────────────────┐                       │
│  │ WorkflowProcessor<TInput,TState,TOutput> │  ◄── RFC PROCESS     │
│  │                                          │                       │
│  │  → Lock(workflowId)                      │                       │
│  │  → StoreInput() (inbox)                  │                       │
│  │  → RebuildState() (replay events)        │                       │
│  │  → Decide() → get outputs                │                       │
│  │  → StoreOutput() (outbox)                │                       │
│  │  → DeliverOutputs()                      │                       │
│  │  → ReleaseLock()                         │                       │
│  └─────────────────────────────────────────┘                       │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

#### **New Components** ✅

| Component | Location | Purpose |
|-----------|----------|---------|
| `IWorkflowBus` | `Workflow/InboxOutbox/IWorkflowBus.cs` | Clean abstraction for sending workflow inputs |
| `WolverineWorkflowBus` | `Workflow/InboxOutbox/WolverineWorkflowBus.cs` | Wolverine implementation, creates envelope internally |
| `WorkflowInputEnvelope` | `Workflow/InboxOutbox/WorkflowInputEnvelope.cs` | Non-generic message for Wolverine routing |
| `WorkflowInputHandler` | `Workflow/InboxOutbox/WorkflowInputHandler.cs` | Generic Wolverine handler (Router role) |
| `IWorkflowProcessorFactory` | `Workflow/InboxOutbox/IWorkflowProcessorFactory.cs` | Factory for resolving processors by type |
| `IWorkflowTypeRegistry` | `Workflow/InboxOutbox/IWorkflowBus.cs` | Maps input types to workflow types |

#### **Registration Pattern** ✅

```csharp
// Program.cs - Single line registers everything
builder.Services.AddWorkflow<OrderProcessingInputMessage, OrderProcessingState,
    OrderProcessingOutputMessage, OrderProcessingWorkflow>("OrderProcessing");

// This automatically registers:
// - IWorkflowBus (WolverineWorkflowBus)
// - IWorkflowProcessorFactory
// - IWorkflowTypeRegistry
// - IWorkflowProcessorRegistration for this workflow type

// Wolverine configuration
builder.Host.UseWolverine(opts =>
{
    opts.LocalQueue("workflow-inputs").Sequential();
    opts.PublishMessage<WorkflowInputEnvelope>().ToLocalQueue("workflow-inputs");
    opts.Discovery.IncludeAssembly(typeof(WorkflowInputHandler).Assembly);
});
```

#### **Client Usage** ✅

```csharp
// Clean API - no envelope, no workflow type needed
public class OrderModule
{
    public async Task<IResult> CreateOrder(
        CreateOrderRequest request,
        IWorkflowBus workflowBus,
        CancellationToken ct)
    {
        var orderId = Guid.NewGuid().ToString();

        // Just send the input - library handles everything else
        await workflowBus.SendAsync(orderId, new PlaceOrderInputMessage(orderId), ct);

        return Results.Created($"/orders/{orderId}", ...);
    }
}
```

#### **Type Inheritance Support** ✅

The `IWorkflowTypeRegistry` supports inheritance lookup:

```csharp
// Registration maps base type
AddWorkflow<OrderProcessingInputMessage, ...>("OrderProcessing");

// Client sends concrete type
await workflowBus.SendAsync(orderId, new PlaceOrderInputMessage(orderId));
// PlaceOrderInputMessage : OrderProcessingInputMessage

// Registry walks inheritance chain to find mapping:
// PlaceOrderInputMessage → OrderProcessingInputMessage → "OrderProcessing"
```

### 3. Stream Architecture (RFC Option C)

#### **WorkflowMessage** ✅
Unified message wrapper for stream storage:
```csharp
public record WorkflowMessage<TInput, TOutput>(
    string WorkflowId,
    long Position,
    MessageKind Kind,           // Command | Event
    MessageDirection Direction, // Input | Output
    object Message,
    DateTime Timestamp,
    bool? Processed             // For command execution tracking
);
```

#### **IWorkflowPersistence** ✅
Stream-based persistence abstraction:
```csharp
Task<long> AppendAsync(string workflowId, IReadOnlyList<WorkflowMessage> messages);
Task<IReadOnlyList<WorkflowMessage>> ReadStreamAsync(string workflowId, long fromPosition = 0);
Task<IReadOnlyList<WorkflowMessage>> GetPendingCommandsAsync(string? workflowId = null);
Task MarkCommandProcessedAsync(string workflowId, long position);
```

#### **InMemoryWorkflowPersistence** ✅
Fully implemented and tested in-memory persistence for testing purposes.

### 4. Domain Implementation: OrderProcessingWorkflow ✅

**API Endpoints:**
- `POST /orders` - Create new order
- `POST /orders/{id}/payment` - Record payment
- `POST /orders/{id}/ship` - Ship order
- `POST /orders/{id}/deliver` - Mark delivered
- `POST /orders/{id}/cancel` - Cancel order
- `GET /orders/{id}/status` - Get order status

**State Machine:**
```
NotExisting → OrderCreated → PaymentReceived → Shipped → Delivered
                    ↓              ↓            ↓
                 Cancelled      Cancelled    Cancelled
```

### 5. Testing ✅

**Total Tests: 47+ (all passing)**

---

## What's NOT Yet Implemented ⏳

### 1. Output Processing via Wolverine

**Current State:**
- Outputs are delivered via `WorkflowOutputHandler` delegate
- Simple logging implementation

**Needed:**
- Wolverine-based output delivery
- Use `IMessageBus.SendAsync()` for Send commands
- Use `IMessageBus.PublishAsync()` for Publish commands
- Use `IMessageBus.ScheduleAsync()` for Schedule commands

### 2. Router Function (RFC Compliance)

**Current State:**
- `workflowId` is provided by caller in `SendAsync(workflowId, input)`

**RFC Pattern:**
- Router extracts `workflowId` from message content
- User implements `IWorkflowRouter<TInput>` to define extraction logic

```csharp
// Future enhancement
public interface IWorkflowRouter<TInput>
{
    string GetWorkflowId(TInput input);
}

// Then client becomes:
await workflowBus.SendAsync(new PlaceOrderInputMessage("order-123"));
// Router extracts "order-123" automatically
```

### 3. Concrete Persistence Implementations

**Needed:**
- PostgreSQL implementation of IWorkflowPersistence
- SQLite implementation for local development
- Marten integration (optional)

### 4. Production Features

**Missing:**
- Concurrency control (optimistic locking)
- Checkpoint management (exactly-once semantics)
- Metrics/telemetry (OpenTelemetry integration)
- Workflow versioning

---

## Key Design Decisions

### 1. Wolverine as Infrastructure Layer ✅
**Decision:** Use Wolverine for messaging, routing, and execution
- Local queues for in-process messaging
- Built-in retry and error handling
- Sequential processing option for ordering guarantees
- Dead letter queue for failures

### 2. Clean Client API ✅
**Decision:** Hide infrastructure details from client code
- `IWorkflowBus` abstraction with simple `SendAsync(workflowId, input)`
- No envelope creation in client code
- Type registry handles workflow type resolution
- Inheritance support for message types

### 3. Generic Handler Pattern ✅
**Decision:** Single handler for all workflow types
- `WorkflowInputHandler` handles all `WorkflowInputEnvelope` messages
- `IWorkflowProcessorFactory` resolves correct processor by type
- New workflows added via registration, no new handlers needed

### 4. Library-Owned Integration ✅
**Decision:** Wolverine integration is part of Workflow library
- `WolverineWorkflowBus` in core library (not client project)
- Reduces boilerplate for library consumers
- Consistent behavior across all implementations

---

## File Organization

```
Workflow/
├── Workflow/                              # Core framework library
│   ├── InboxOutbox/
│   │   ├── IWorkflowBus.cs               # Clean abstraction + registry
│   │   ├── WolverineWorkflowBus.cs       # Wolverine implementation
│   │   ├── WorkflowInputEnvelope.cs      # Internal message envelope
│   │   ├── WorkflowInputHandler.cs       # Generic Wolverine handler
│   │   ├── IWorkflowProcessorFactory.cs  # Processor factory + registration
│   │   ├── WorkflowProcessor.cs          # RFC Process implementation
│   │   ├── WorkflowStream.cs             # Stream abstraction
│   │   └── WorkflowStreamRepository.cs   # In-memory repository
│   ├── WorkflowExtensions.cs             # DI registration extensions
│   ├── Workflow.cs                       # Base workflow classes
│   ├── IWorkflow.cs                      # Workflow interfaces
│   └── WorkflowOrchestrator.cs           # Pure orchestrator
│
├── Workflow.Samples/                      # Sample workflows
│   └── Order/
│       ├── OrderProcessingWorkflow.cs
│       ├── OrderProcessingInputMessage.cs
│       ├── OrderProcessingOutputMessage.cs
│       └── OrderProcessingState.cs
│
├── Workflow.Samples.Api/                  # Sample API
│   ├── Program.cs                         # Wolverine + workflow setup
│   ├── Modules/
│   │   └── OrderModule.cs                # Carter endpoints
│   └── Services/
│       └── WorkflowOutputHandler.cs      # Output delivery
│
└── ChatStates/md/                         # Documentation
    ├── ARCHITECTURE.md
    ├── IMPLEMENTATION_STATE.md            # This file
    └── PATTERNS.md
```

---

## Next Actions

### Immediate Priority
1. **Output Processing via Wolverine**
   - Create output message types for Wolverine routing
   - Implement handlers for Send/Publish/Schedule commands
   - Replace delegate-based output handler

2. **Router Function**
   - Add `IWorkflowRouter<TInput>` interface
   - Allow automatic workflowId extraction from messages

### Phase 2
3. Implement concrete persistence (PostgreSQL, SQLite)
4. Add concurrency control (optimistic locking)
5. Add metrics/telemetry

### Phase 3
6. Workflow versioning
7. Saga/compensation patterns
8. Distributed tracing

---

## Recent Changes (2025-01-26)

1. ✅ Integrated Wolverine as messaging infrastructure
2. ✅ Created `IWorkflowBus` abstraction for clean client API
3. ✅ Created `WolverineWorkflowBus` implementation in core library
4. ✅ Created `WorkflowInputEnvelope` for internal routing
5. ✅ Created `WorkflowInputHandler` as generic Wolverine handler (Router)
6. ✅ Created `IWorkflowProcessorFactory` for processor resolution
7. ✅ Created `IWorkflowTypeRegistry` with inheritance support
8. ✅ Updated `WorkflowExtensions` to auto-register all components
9. ✅ Updated `OrderModule` to use clean `IWorkflowBus` API
10. ✅ Fixed `WorkflowOutputHandler` logger registration issue

---

## Summary

**Phase 1: Complete** ✅
- Core framework implemented
- Unified stream architecture (RFC Option C)
- CQRS support with Reply commands
- Comprehensive testing (47 tests)

**Phase 2: In Progress** ⏳
- ✅ Wolverine integration for input processing
- ✅ Clean client API (`IWorkflowBus`)
- ⏳ Output processing via Wolverine
- ⏳ Router function for workflowId extraction

**Phase 3: Not Started**
- Concrete persistence implementations
- Production features (metrics, versioning)
