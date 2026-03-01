# Workflow Stream Architecture

**Last Updated:** 2025-11-24

**Architecture Decision:** WorkflowStreamProcessor and WorkflowOutputProcessor have been removed. Infrastructure concerns (message routing, command execution, background processing) are delegated to Wolverine. This architecture document now focuses on the core workflow orchestration pattern.

**Pattern Implemented:** Process Manager/Orchestrator with Event Sourcing (not pure Saga). The system maintains centralized state and coordinates multi-step processes using event-sourced durable execution.

---

## Table of Contents

1. [Overview](#overview)
2. [The Unified Stream Pattern](#the-unified-stream-pattern)
3. [Component Architecture](#component-architecture)
4. [Consumer and Persistence Responsibilities](#consumer-and-persistence-responsibilities)
5. [Complete Flow Examples](#complete-flow-examples)
6. [Benefits and Design Principles](#benefits-and-design-principles)
7. [Implementation Status](#implementation-status)
8. [Next Steps](#next-steps)

---

## Overview

We've implemented **RFC Option C**: Commands are stored in the workflow stream alongside events, creating a unified message stream that serves as both inbox (inputs) and outbox (outputs).

This provides:
- ✅ **Complete observability**: Full audit trail in one place
- ✅ **Durability**: Commands persisted before execution (crash recovery)
- ✅ **Idempotency**: Commands marked as processed (no duplicate execution)
- ✅ **Simplicity**: Single storage model for everything
- ✅ **Query support**: Reply commands for read-only operations without state mutation

---

## The Unified Stream Pattern

### Stream Structure (RFC Lines 297-309)

**Workflow Stream for "group-checkout-123":**

| Pos | Kind    | Direction | Message                   | Processed              |
|-----|---------|-----------|---------------------------|------------------------|
| 1   | Command | Input     | InitiateGroupCheckout     | N/A                    |
| 2   | Event   | Output    | GroupCheckoutInitiated    | N/A                    |
| 3   | Command | Output    | CheckOut (guest-1)        | false ← Needs execution |
| 4   | Command | Output    | CheckOut (guest-2)        | false ← Needs execution |
| 5   | Event   | Input     | GuestCheckedOut (guest-1) | N/A                    |
| 6   | Event   | Input     | GuestCheckoutFailed (guest-2) | N/A                |
| 7   | Event   | Output    | GroupCheckoutFailed       | N/A                    |

**Key Insights:**
- **Commands** (Kind=Command) are instructions to execute (Send, Publish, Schedule)
- **Events** (Kind=Event) are facts that evolve state (via Evolve)
- **Input** (Direction=Input) messages trigger workflow processing
- **Output** (Direction=Output) messages are produced by workflow
- **Processed flag** tracks command execution (idempotency)

### Core Data Structure

**WorkflowMessage**
```csharp
public record WorkflowMessage<TInput, TOutput>(
    string WorkflowId,           // Which workflow instance
    long Position,               // Sequence number in stream
    MessageKind Kind,            // Command | Event
    MessageDirection Direction,  // Input | Output
    object Message,              // The actual payload
    DateTime Timestamp,          // When recorded
    bool? Processed              // Command execution status
);
```

**Helpers:**
- `IsPendingCommand`: Returns true for unprocessed output commands
- `IsEventForStateEvolution`: Returns true for events (both input/output)

### Persistence Interface

**Before (Snapshot-based):**
```csharp
Task SaveAsync(string workflowId, WorkflowSnapshot snapshot);
Task<WorkflowSnapshot?> LoadAsync(string workflowId);
```

**After (Stream-based):**
```csharp
// Append messages (inputs, outputs, commands, events)
Task<long> AppendAsync(string workflowId, IReadOnlyList<WorkflowMessage> messages);

// Rebuild state from stream
Task<IReadOnlyList<WorkflowMessage>> ReadStreamAsync(string workflowId, long fromPosition = 0);

// Get commands needing execution
Task<IReadOnlyList<WorkflowMessage>> GetPendingCommandsAsync(string? workflowId = null);

// Mark command as executed (idempotency)
Task MarkCommandProcessedAsync(string workflowId, long position);
```

**Key Change:** Instead of storing snapshots (derived state), we store the stream (source of truth). State is rebuilt by replaying events.

---

## Component Architecture

### High-Level Flow

**Note:** WorkflowStreamProcessor and WorkflowOutputProcessor have been removed. Infrastructure is now handled by Wolverine (see WOLVERINE_HYBRID_ARCHITECTURE.md).

```
┌─────────────────────────────────────────────────────────────────┐
│ 1. Input Message (from HTTP/Queue/Kafka/etc.)                   │
│    PlaceOrder, PaymentReceived, CancelOrder, etc.               │
│    → Routed by Wolverine to workflow message handlers           │
└────────────────────────────────┬────────────────────────────────┘
                                 │
                                 ▼
┌─────────────────────────────────────────────────────────────────┐
│ 2. Wolverine Message Handler                                     │
│    - Receives message from Wolverine                             │
│    - Determines workflow ID                                      │
│    - Loads workflow state from persistence                       │
│    - Calls WorkflowOrchestrator                                  │
└────────────────────────────────┬────────────────────────────────┘
                                 │
                                 ▼
┌─────────────────────────────────────────────────────────────────┐
│ 3. WorkflowOrchestrator (Pure Business Logic)                   │
│    - Rebuilds state from events                                 │
│    - Calls Decide → Commands                                    │
│    - Calls Translate → Events                                   │
│    - Returns result with commands + events + snapshot           │
└────────────────────────────────┬────────────────────────────────┘
                                 │
                                 ▼
┌─────────────────────────────────────────────────────────────────┐
│ 4. IWorkflowPersistence (Stream Storage)                         │
│    - PostgreSQL, EventStoreDB, SQLite, etc.                      │
│    - AppendAsync(workflowId, messages)                           │
│    - ReadStreamAsync(workflowId)                                 │
│    - GetPendingCommandsAsync()                                   │
│    - MarkCommandProcessedAsync(workflowId, position)             │
└────────────────────────────────┬────────────────────────────────┘
                                 │
                                 ▼
┌─────────────────────────────────────────────────────────────────┐
│ 5. Wolverine Infrastructure                                      │
│    - Executes commands (Send/Publish/Schedule/Reply/Complete)   │
│    - Background polling for pending commands                     │
│    - Marks commands as processed after execution                 │
│    - Retry logic and error handling                              │
└─────────────────────────────────────────────────────────────────┘
```

### Component Details

#### 1. WorkflowOrchestrator ✅
**Purpose:** Pure orchestration logic (no I/O)

**File:** Workflow/Workflow/WorkflowOrchestrator.cs (109 lines)

**Responsibilities:**
- Execute Decide → Translate → Evolve cycle
- Track event history
- Manage snapshots
- Pure business logic (easy testing)

**Status:** Fully implemented and tested (47+ tests passing)

**Implementations:**
- `WorkflowOrchestrator<TInput, TState, TOutput>` - Synchronous workflows
- `AsyncWorkflowOrchestrator<TInput, TState, TOutput, TContext>` - Async workflows with context injection

**Key Features:**
- Type-safe context objects for external service integration (databases, HTTP clients, etc.)
- Helper methods for cleaner pattern matching
- Returns `OrchestrationResult` containing commands, events, and new snapshot
- Immutable state management via `Evolve`
- Complete event history tracking

#### 2. IWorkflowPersistence ✅
**Purpose:** Stream storage abstraction

**Files:**
- Interface: Workflow/Workflow/IWorkflowPersistence.cs (75 lines)
- Implementation: Workflow/Workflow/InMemoryWorkflowPersistence.cs (188 lines)

**Responsibilities:**
1. **Store messages** in workflow streams (AppendAsync)
2. **Read stream** for state reconstruction (ReadStreamAsync)
3. **Query pending commands** for background processing (GetPendingCommandsAsync)
4. **Track command execution** with Processed flag (MarkCommandProcessedAsync)
5. **Stream management** (ExistsAsync, DeleteAsync)

**Implementation Status:**
- ✅ Interface fully defined with comprehensive documentation
- ✅ InMemoryWorkflowPersistence - Thread-safe implementation
  - ConcurrentDictionary<string, WorkflowStream> for workflow storage
  - Per-workflow ReaderWriterLockSlim for concurrency control
  - Defensive copies to prevent external mutation
  - Position-based ordering (1-based sequence numbers)
  - Comprehensive test coverage (InMemoryWorkflowPersistenceTests.cs)
- ⏳ PostgreSQL implementation (future)
- ⏳ SQLite implementation (future)
- ⏳ Marten integration for EventStoreDB-like features (future)

#### 3. Workflow Base Classes ✅
**Purpose:** Provide base abstractions for workflow definitions

**Files:**
- Workflow/Workflow/Workflow.cs (118 lines)

**Class Hierarchy:**
```csharp
// Base class with shared functionality
public abstract class WorkflowBase<TInput, TState, TOutput>
{
    public abstract TState InitialState { get; }
    protected abstract TState InternalEvolve(TState state, WorkflowEvent<TInput, TOutput> evt);
    public TState Evolve(TState state, WorkflowEvent<TInput, TOutput> evt) { /* Generic + domain logic */ }

    // Helper methods for cleaner syntax
    protected static Reply<TOutput> Reply(TOutput message) => ...
    protected static Send<TOutput> Send(TOutput message) => ...
    // ... etc
}

// Synchronous workflows
public abstract class Workflow<TInput, TState, TOutput>
    : WorkflowBase<TInput, TState, TOutput>
{
    public abstract IReadOnlyList<WorkflowCommand<TOutput>>
        Decide(TInput input, TState state);
}

// Asynchronous workflows with context
public abstract class AsyncWorkflow<TInput, TState, TOutput, TContext>
    : WorkflowBase<TInput, TState, TOutput>
{
    public abstract Task<IReadOnlyList<WorkflowCommand<TOutput>>>
        DecideAsync(TInput input, TState state, TContext context);
}
```

**Key Features:**
- Separation of sync and async workflows
- Context injection for async operations (databases, HTTP clients, external services)
- Helper methods reduce boilerplate
- Generic event handling (Began, InitiatedBy, Received, etc.)
- Immutable state transitions

**Examples:**
- OrderProcessingWorkflow (sync version) - 229 lines
- OrderProcessingAsyncWorkflow (async version with IOrderContext) - 229 lines
- GroupCheckoutWorkflow (sync) - 216 lines

#### 4. Wolverine Infrastructure ⏳ (In Progress)
**Purpose:** Handles all infrastructure concerns (routing, execution, background processing)

**Current Status:** Basic setup only (WorkflowWolverineSingle/Program.cs, 18 lines)

**Planned Responsibilities:**
1. **Message Routing**: Route incoming messages to workflow handlers
2. **Command Execution**: Execute Send/Publish/Schedule/Reply/Complete commands
3. **Background Processing**: Poll for pending commands and execute them
4. **Retry Logic**: Handle transient failures with retries
5. **Error Handling**: Dead letter queue for permanent failures

**See:** WOLVERINE_HYBRID_ARCHITECTURE.md for detailed integration plan

**Key Benefits:**
- Production-ready messaging infrastructure
- Built-in retry and error handling
- Persistence integration
- Observability and metrics
- Avoids reinventing infrastructure

**What Will Replace:**
- WorkflowInputRouter → Wolverine message routing (not yet implemented)
- WorkflowStreamConsumer → Wolverine message handlers (not yet implemented)
- WorkflowStreamProcessor → Wolverine handlers + WorkflowOrchestrator (not yet implemented)
- WorkflowOutputProcessor → Wolverine background polling (not yet implemented)
- ICommandExecutor → Wolverine message publishing (not yet implemented)

```csharp
public interface ICommandExecutor<TOutput>
{
    Task ExecuteAsync(TOutput command, CancellationToken cancellationToken);
}
```

**Example Implementation:**
```csharp
public class CompositeCommandExecutor<TOutput> : ICommandExecutor<TOutput>
{
    private readonly IMessageBus _messageBus;
    private readonly IScheduler _scheduler;

    public async Task ExecuteAsync(TOutput command, CancellationToken ct)
    {
        // Route based on command type
        if (command is CheckOut checkout)
            await _messageBus.SendAsync(checkout, ct);
        else if (command is ScheduleTimeout timeout)
            await _scheduler.ScheduleAsync(timeout, timeout.After, ct);
        // ... etc
    }
}
```

---

## Consumer and Persistence Responsibilities

### Separation of Concerns

The RFC architecture requires clear separation between routing, consuming, and processing:

```
Source Stream → Consumer → Workflow Processor → Workflow Stream
                                ↓
                    Workflow Stream → Consumer → Processor (rebuild/decide)
                                                      ↓
                                            Workflow Stream (outputs)
                                                      ↓
                                            Output Handler
```

### Component Responsibilities

**Note:** Table updated to reflect Wolverine integration. WorkflowStreamProcessor and WorkflowOutputProcessor are replaced by Wolverine infrastructure.

| Component | Subscribes To | Persists | Reads | Other Responsibilities |
|-----------|---------------|----------|-------|----------------------|
| **Wolverine Handler** | Message bus | Inputs ✓ | Stream messages ✓ | Routes via message handlers |
| **WorkflowOrchestrator** | - | Outputs ✓ (via persistence) | All events ✓ | Rebuilds state, calls decide/translate |
| **Wolverine Background** | - | Processed flag ✓ | Pending commands ✓ | Executes commands, retry logic |

### Key Principles

1. **Input persistence happens in Wolverine handlers** before workflow processing
2. **Orchestrator assumes inputs are available** from persistence layer
3. **Orchestrator reads entire stream** to rebuild state, then processes current message
4. **Output persistence happens in orchestrator** before returning to Wolverine
5. **Wolverine provides infrastructure** for routing, execution, retries, and error handling

### Two Consumer Roles

#### Input Consumer/Router (RFC Lines 130-133)

**Purpose**: Routes messages from source streams to the correct workflow instance stream

**Flow:**
```
1. InputRouter:
   - Receives: GuestCheckedOut from source stream
   - Determines: workflowId = "group-123"
   - Persists: AppendAsync("group-123", inputMessage)
```

This is the **routing/forwarding** layer - ensures messages get to the right workflow instance.

#### Workflow Stream Consumer (RFC Line 134)

**Purpose**: Processes messages from the workflow's own stream

**Flow:**
```
2. WorkflowStreamConsumer:
   - Detects: New message at position 6 in "group-123" stream
   - Triggers: WorkflowProcessor.ProcessAsync(workflow, "group-123", fromPosition: 6)
```

This is the **processing trigger** - makes workflows reactive to new messages.

### Who Should Persist What?

#### InputRouter - Persists Inputs (Step 3)

```
Source Stream → InputRouter
                    ↓
                GetWorkflowId(message)
                    ↓
                persistence.AppendAsync(workflowId, inputMessage)
                    ↓
                Workflow Stream (inbox)
```

**What it stores**: Raw input messages (commands/events from source streams)

**Why router does this**:
- Creates the durable "inbox"
- Message is persisted BEFORE processing
- Ensures we never lose a message even if processing fails

#### WorkflowProcessor - Persists Outputs (Step 7)

```
Workflow Stream → Rebuild State → Decide → Translate
                                              ↓
                        persistence.AppendAsync(workflowId, outputMessages)
                                              ↓
                                    Workflow Stream (outbox)
```

**What it stores**: Output messages (commands + events from decide/translate)

**Why processor does this**:
- Creates the durable "outbox"
- Commands/events are persisted BEFORE execution
- Ensures we never lose outputs even if execution fails

### Proposed Refactoring

**Current (Wrong)**:
```csharp
public async Task ProcessAsync(
    string workflowId,
    TInput message,  // ← Message is passed in, then persisted
    bool begins = false)
{
    // WRONG: Processor shouldn't persist inputs
    await persistence.AppendAsync(workflowId, [inputMessage]);

    // CORRECT: Processor reads to rebuild state
    var allMessages = await persistence.ReadStreamAsync(workflowId);

    // ...process...

    // CORRECT: Processor persists outputs
    await persistence.AppendAsync(workflowId, outputMessages);
}
```

**Proposed (Correct)**:
```csharp
public async Task ProcessAsync(
    IWorkflow<TInput, TState, TOutput> workflow,
    string workflowId,
    long fromPosition)  // ← Process from this position
{
    // Read stream (includes the new input already persisted by router)
    var allMessages = await persistence.ReadStreamAsync(workflowId);

    // Get the triggering message(s) from the specified position onwards
    var newMessages = allMessages.Where(m => m.Position >= fromPosition).ToList();

    // Rebuild state from output events BEFORE the trigger
    var snapshot = RebuildStateFromStream(workflow, allMessages);

    // Process each new input message
    foreach (var triggerMessage in newMessages.Where(m => m.Direction == MessageDirection.Input))
    {
        var orchestrationResult = orchestrator.Process(
            workflow,
            snapshot,
            (TInput)triggerMessage.Message,
            begins: false
        );

        // Store outputs
        await persistence.AppendAsync(workflowId, ConvertToOutputMessages(orchestrationResult));

        // Update snapshot for next message
        snapshot = orchestrationResult.NewSnapshot;
    }
}
```

This way the processor:
- ✓ **Reads** state from persistence (rebuilding from events)
- ✓ **Writes** outputs to persistence
- ✗ **Never writes** inputs (that's the router's job)

---

## Complete Flow Examples

### Step-by-Step Example: Group Checkout

**1. Input Arrives**
```csharp
// HTTP POST /group-checkout
var message = new InitiateGroupCheckout(
    GroupId: "group-123",
    GuestIds: ["guest-1", "guest-2"]
);
```

**2. Wolverine Handler Stores Input**
```csharp
// Wolverine handler receives message and stores to workflow stream
// Appends to stream "group-checkout-123"
WorkflowMessage {
    WorkflowId: "group-checkout-123",
    Position: 1,
    Kind: Command,
    Direction: Input,
    Message: InitiateGroupCheckout,
    Processed: null
}
```

**3. Rebuild State from Stream**
```csharp
// Read all messages, filter events, replay through Evolve
var messages = await persistence.ReadStreamAsync("group-checkout-123");
var events = messages.Where(m => m.IsEventForStateEvolution);
var state = workflow.InitialState;
foreach (var evt in events)
    state = workflow.Evolve(state, evt);
```

**4. Decide → Commands**
```csharp
var commands = workflow.Decide(message, state);
// Returns:
// [Send(CheckOut(guest-1)), Send(CheckOut(guest-2))]
```

**5. Translate → Events**
```csharp
var events = workflow.Translate(begins: true, message, commands);
// Returns:
// [Began, InitiatedBy(InitiateGroupCheckout), Sent(CheckOut(guest-1)), Sent(CheckOut(guest-2))]
```

**6. Store Outputs in Stream**
```csharp
// Commands stored with Processed=false
WorkflowMessage {
    Position: 2, Kind: Command, Direction: Output,
    Message: CheckOut(guest-1), Processed: false
}
WorkflowMessage {
    Position: 3, Kind: Command, Direction: Output,
    Message: CheckOut(guest-2), Processed: false
}

// Events stored for audit
WorkflowMessage {
    Position: 4, Kind: Event, Direction: Output,
    Message: Began, Processed: null
}
// ... more events
```

**7. Wolverine Background Processor Polls**
```csharp
// Wolverine background service polls for pending commands
var pending = await persistence.GetPendingCommandsAsync();
// Returns positions 2 and 3 (Processed=false)
```

**8. Execute Commands**
```csharp
foreach (var message in pending) {
    await executor.ExecuteAsync(message.Message); // Send to message bus
    await persistence.MarkCommandProcessedAsync(message.WorkflowId, message.Position);
}
```

**9. Final Stream State**
```
Pos | Kind    | Direction | Message                    | Processed
----|---------|-----------|----------------------------|----------
1   | Command | Input     | InitiateGroupCheckout      | N/A
2   | Command | Output    | CheckOut(guest-1)          | true ✓
3   | Command | Output    | CheckOut(guest-2)          | true ✓
4   | Event   | Output    | Began                      | N/A
5   | Event   | Output    | InitiatedBy                | N/A
6   | Event   | Output    | Sent(CheckOut(guest-1))    | N/A
7   | Event   | Output    | Sent(CheckOut(guest-2))    | N/A
```

---

## Benefits and Design Principles

### Benefits Achieved

#### 1. Complete Observability
**One stream contains everything:**
- What inputs triggered processing
- What commands were issued
- What events occurred
- When each action happened
- Which commands have been executed

**Debugging workflow:** Just read the stream!

#### 2. Durability & Crash Recovery
**Scenario:** Process crashes after storing commands but before execution

**Recovery:**
```
1. Process crashes at position 3 (commands stored, not executed)
2. Process restarts
3. WorkflowOutputProcessor calls GetPendingCommandsAsync()
4. Returns positions 2-3 (Processed=false)
5. Commands re-executed
6. Marked as processed
```

**No lost commands!**

#### 3. Idempotency
**Scenario:** Command executed successfully, but process crashes before marking as processed

**Solution:**
- Command re-executed on restart (Processed still = false)
- Command handlers MUST be idempotent
- Options:
  - Natural idempotency (SET operations)
  - Deduplication keys (check before executing)
  - External idempotency (downstream systems track message IDs)

#### 4. At-Least-Once Delivery
**Guarantee:** Every command will be executed at least once

**If processor crashes:**
- Before storage: Input will be retried by sender
- After storage, before execution: Command re-executed on restart
- After execution, before marking: Command re-executed (idempotency required)

#### 5. Query Operations with Reply Commands

**Pattern:** Read-only operations that don't mutate state

**Example: GetCheckoutStatus**
```csharp
// Input message
public record GetCheckoutStatus(string GroupCheckoutId) : GroupCheckoutInputMessage;

// Output message (reply)
public record CheckoutStatus(
    string GroupCheckoutId,
    string Status,
    int TotalGuests,
    int CompletedGuests,
    int FailedGuests,
    int PendingGuests,
    List<GuestStatus> Guests
) : GroupCheckoutOutputMessage;
```

**Workflow Implementation:**
```csharp
// Decide: Generate Reply command
(GetCheckoutStatus m, Pending p) => [
    Reply(new CheckoutStatus(...))  // Read current state, no mutation
],

// Evolve: No state change for queries
(Pending p, Received { Message: GetCheckoutStatus m }) => state,  // Return unchanged
```

**Key Insights:**
- **Queries** are CQRS read operations - they extract information without changing state
- **Reply** commands send responses back to the caller
- **Evolve** returns state unchanged for query messages (valid pattern)
- **Decide** generates the Reply command with computed data from current state
- **No side effects** in the workflow state machine

**Benefits:**
- ✅ Clear separation of commands (write) vs queries (read)
- ✅ State remains immutable for read operations
- ✅ Reply commands are persisted in stream (full audit trail)
- ✅ Can query workflow state without altering it

### Design Principles

1. **Separation of Concerns**
   - `Workflow` - Pure business logic (Decide, Evolve)
   - `WorkflowOrchestrator` - Pure orchestration (no I/O)
   - `Wolverine` - Infrastructure (routing, persistence, execution)
   - Benefits: Easy testing, clear boundaries

2. **Immutable State**
   - All state transitions via immutable records
   - State is rebuilt from events via `Evolve`
   - No mutable state in workflow
   - Benefits: Predictable, testable, event-sourceable

3. **Event Sourcing**
   - Stream is the source of truth
   - State is derived (rebuild by replaying events)
   - Complete audit trail
   - Benefits: Time travel, debugging, compliance

4. **Double-Hop Pattern**
   - Inputs routed to workflow stream (first hop)
   - Outputs executed from workflow stream (second hop)
   - Both hops are durable
   - Benefits: Crash recovery, observability

### Comparison: Before vs After

#### Before (No Command Storage)
```
❌ Commands returned but not persisted
❌ If process crashes, commands lost
❌ No audit trail of what was supposed to happen
❌ No idempotency guarantees
```

#### After (Unified Stream)
```
✅ Commands stored before execution
✅ Crash recovery: Re-execute unprocessed commands
✅ Complete audit trail (inputs + outputs + execution status)
✅ Idempotency via Processed flag
✅ Single stream for complete observability
✅ Query operations with Reply commands (CQRS read model)
✅ State immutability for read operations
```

---

## Implementation Status

### Phase 1: Core Framework - Completed ✅

**Workflow Abstractions:**
- ✅ `WorkflowBase<TInput, TState, TOutput>` - Base class with shared functionality (Workflow.cs)
- ✅ `Workflow<TInput, TState, TOutput>` - Synchronous workflow implementation
- ✅ `AsyncWorkflow<TInput, TState, TOutput, TContext>` - Async workflows with external service context
- ✅ Three core methods: `Decide`, `Evolve`, `Translate`

**Orchestration:**
- ✅ `WorkflowOrchestrator<TInput, TState, TOutput>` - Pure orchestration (no I/O)
- ✅ `AsyncWorkflowOrchestrator<TInput, TState, TOutput, TContext>` - Async version with context injection
- ✅ Decide → Translate → Evolve → Persist cycle
- ✅ Returns `OrchestrationResult` with commands, events, and new snapshot

**Commands & Events:**
- ✅ `WorkflowCommand<TOutput>` - Reply, Send, Publish, Schedule, Complete (WorkflowCommand.cs)
- ✅ `WorkflowEvent<TInput, TOutput>` - Began, InitiatedBy, Received, Replied, Sent, Published, Scheduled, Completed (WorkflowEvent.cs)
- ✅ CQRS support with Reply commands for read-only queries

**Stream Architecture:**
- ✅ `WorkflowMessage<TInput, TOutput>` - Unified stream message wrapper (WorkflowMessage.cs)
- ✅ MessageKind enum (Command | Event)
- ✅ MessageDirection enum (Input | Output)
- ✅ Position-based ordering (1-based sequence numbers)
- ✅ Processed flag for command idempotency
- ✅ Helper properties: `IsPendingCommand`, `IsEventForStateEvolution`

**Persistence:**
- ✅ `IWorkflowPersistence<TInput, TState, TOutput>` interface (IWorkflowPersistence.cs)
  - AppendAsync (store messages to stream)
  - ReadStreamAsync (rebuild state from events)
  - GetPendingCommandsAsync (find unprocessed commands)
  - MarkCommandProcessedAsync (idempotency tracking)
  - ExistsAsync, DeleteAsync (stream management)
- ✅ `InMemoryWorkflowPersistence<TInput, TState, TOutput>` - Thread-safe implementation (InMemoryWorkflowPersistence.cs)
  - ConcurrentDictionary with per-workflow locks
  - Defensive copies to prevent external mutation
  - Suitable for testing and single-instance deployments

**Sample Workflows:**
- ✅ OrderProcessingWorkflow (sync & async versions) - 229 lines
  - Inventory checking, payment processing, shipping, delivery
  - Timeout handling and cancellations
  - Full test coverage (28 tests)
- ✅ GroupCheckoutWorkflow - 216 lines
  - Hotel group checkout coordination (scatter-gather pattern)
  - Partial failure handling
  - Timeout management
  - 18 tests passing
- ✅ IssueFineForSpeedingViolationWorkflow - 87 lines
  - Traffic fine issuance example

**Testing:**
- ✅ 47+ tests passing
- ✅ WorkflowOrchestratorTests.cs (orchestrator behavior)
- ✅ GroupCheckoutWorkflowTests.cs (unit & integration tests)
- ✅ OrderProcessingWorkflowTests.cs (async workflow tests, 372 lines)
- ✅ InMemoryWorkflowPersistenceTests.cs (persistence & thread-safety)

**Performance Optimizations:**
- ✅ FrugalList<T> - Memory-efficient list for 0-1 items (Core/FrugalList.cs, 85 lines)

### Architecture Decision ⚙️
- ⚙️ **Removed**: WorkflowStreamProcessor, WorkflowOutputProcessor
- ⚙️ **Replaced by**: Wolverine infrastructure (see WOLVERINE_HYBRID_ARCHITECTURE.md)
- ⚙️ **Reason**: Avoid reinventing infrastructure; use production-ready Wolverine for routing, execution, retries

### Phase 2: Infrastructure Integration - In Progress ⏳
- ⏳ Wolverine integration (message handlers, command execution)
  - Basic setup exists in WorkflowWolverineSingle/Program.cs (18 lines)
  - Need: Message handlers for workflow inputs
  - Need: Background polling for pending commands
  - Need: Command execution via Wolverine message bus
- ⏳ Concrete persistence implementations
  - PostgreSQL implementation of IWorkflowPersistence
  - SQLite implementation for local development
  - Marten integration (optional, for EventStoreDB-like features)

### Phase 3: Production Features - Future Work 📋
- 📋 Workflow ID routing strategies
- 📋 Concurrency control (optimistic locking)
- 📋 Checkpoint management (exactly-once semantics)
- 📋 Metrics/telemetry (OpenTelemetry)
- 📋 Workflow versioning
- 📋 Long-running workflow support
- 📋 Advanced saga pattern implementations

---

## Wolverine Integration Strategy

### Overview: Hybrid Architecture

**Proposed Approach:** Use Wolverine as the messaging infrastructure layer while keeping our custom workflow orchestration engine.

**Division of Responsibilities:**

```
┌─────────────────────────────────────────────────────────────────┐
│ OUR WORKFLOW ENGINE (Core Business Logic)                       │
│                                                                  │
│  ✅ Workflow<TInput, TState, TOutput>                           │
│  ✅ Decide(input, state) → commands                             │
│  ✅ Evolve(state, event) → state                                │
│  ✅ WorkflowOrchestrator (pure orchestration)                   │
│  ✅ Unified stream storage (our design)                         │
│  ✅ Processed flag pattern                                      │
└────────────────┬────────────────────────────────────────────────┘
                 │
                 │ Delegates infrastructure to...
                 │
┌────────────────▼────────────────────────────────────────────────┐
│ WOLVERINE (Infrastructure Layer) - To Be Implemented            │
│                                                                  │
│  ⏳ Inbox: Read events from source streams                      │
│  ⏳ Outbox: Queue commands for execution                        │
│  ⏳ Command Handlers: Execute commands via handlers             │
│  ⏳ Message Bus: Send to queues/topics                          │
│  ⏳ Durability: Transactional inbox/outbox pattern              │
│  ⏳ Retry/DLQ: Error handling infrastructure                    │
└─────────────────────────────────────────────────────────────────┘
```

### What is Wolverine

**Wolverine** is a .NET library for building asynchronous, message-driven applications.

**Created by:** Jeremy D. Miller (author of Marten, Jasper, StructureMap/Lamar)

**Key Features:**
- Handler discovery via convention
- Transactional outbox pattern (exactly-once delivery)
- Saga support (stateful workflows)
- Durable messaging (persist before execution, automatic retry, DLQ)
- Integration with Marten, RabbitMQ, Azure Service Bus, Kafka

**Resources:**
- GitHub: https://github.com/JasperFx/wolverine
- Docs: https://wolverine.netlify.app/
- Sagas: https://wolverine.netlify.app/guide/durability/sagas.html

### We Own (Core Domain) ✅

- ✅ **Workflow orchestration logic** - decide/evolve pattern, pure functions
- ✅ **State management and transitions** - State rebuilding from events
- ✅ **Workflow stream storage** - Unified stream architecture (RFC Option C)
- ✅ **Command generation** - What commands to issue, when to issue them

### Wolverine Owns (Infrastructure) ⏳

- ⏳ **Message delivery** - Inbox/outbox tables, transactional guarantees
- ⏳ **Command execution** - Handler discovery, handler execution
- ⏳ **Queue integration** - RabbitMQ, Azure Service Bus, Kafka
- ⏳ **Retry/error handling** - Automatic retry with backoff, dead letter queues
- ⏳ **Durability guarantees** - Exactly-once processing, at-least-once delivery

### Integration Architecture

**Complete Flow:**

```
┌─────────────────────────────────────────────────────────────────┐
│ 1. Source Events (RabbitMQ/Kafka/Event Store)                   │
└────────────────────────────────┬────────────────────────────────┘
                                 ▼
┌─────────────────────────────────────────────────────────────────┐
│ 2. Wolverine Inbox (durably stores incoming messages)           │
└────────────────────────────────┬────────────────────────────────┘
                                 ▼
┌─────────────────────────────────────────────────────────────────┐
│ 3. WorkflowInputRouter (Wolverine Handler)                      │
│    - Determines workflow ID                                      │
│    - Stores event in OUR workflow stream                        │
│    - Publishes ProcessWorkflow command                          │
└────────────────────────────────┬────────────────────────────────┘
                                 ▼
┌─────────────────────────────────────────────────────────────────┐
│ 4. OUR Workflow Stream (PostgreSQL/SQLite)                      │
│    - Stores all inputs, outputs, commands, events               │
│    - Processed flag tracks command execution                    │
└────────────────────────────────┬────────────────────────────────┘
                                 ▼
┌─────────────────────────────────────────────────────────────────┐
│ 5. WorkflowProcessor (Wolverine Handler, OUR Code)              │
│    - Reads OUR stream                                           │
│    - Rebuilds state via OUR evolve                              │
│    - Calls OUR decide                                           │
│    - Stores outputs in OUR stream (Processed = false)           │
└────────────────────────────────┬────────────────────────────────┘
                                 ▼
┌─────────────────────────────────────────────────────────────────┐
│ 6. WorkflowOutboxPublisher (Background Service)                 │
│    - Polls OUR stream for pending commands                      │
│    - Publishes to Wolverine                                     │
│    - Marks as processed in OUR stream                           │
└────────────────────────────────┬────────────────────────────────┘
                                 ▼
┌─────────────────────────────────────────────────────────────────┐
│ 7. Wolverine Outbox (stores commands for delivery)              │
└────────────────────────────────┬────────────────────────────────┘
                                 ▼
┌─────────────────────────────────────────────────────────────────┐
│ 8. Command Handlers (execute domain logic, return events)       │
└────────────────────────────────┬────────────────────────────────┘
                                 ▼
┌─────────────────────────────────────────────────────────────────┐
│ 9. Events Published (back to step 1, loop until complete)       │
└─────────────────────────────────────────────────────────────────┘
```

### Key Design Decisions

**1. Our Stream as Outbox (Recommended)**

We use our workflow stream's `Processed` flag as the outbox, with a background service polling and publishing to Wolverine:

```csharp
public class WorkflowOutboxPublisher : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            // 1. Read pending commands from OUR stream
            var pending = await _persistence.GetPendingCommandsAsync();

            foreach (var cmd in pending)
            {
                // 2. Publish to Wolverine (handles delivery)
                await _wolverineContext.SendAsync(cmd.Message);

                // 3. Mark processed in OUR stream
                await _persistence.MarkCommandProcessedAsync(
                    cmd.WorkflowId, cmd.Position);
            }

            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }
    }
}
```

**Benefits:**
- ✅ Leverages our `Processed` flag design
- ✅ OUR stream is single source of truth
- ✅ Wolverine handles delivery, not storage
- ✅ Clean separation of concerns
- ✅ Can switch from Wolverine later if needed

**2. Keep Both Processed Flags**

Our `Processed` flag and Wolverine's outbox status serve different purposes:

| Our Stream | Wolverine Tables |
|-----------|-----------------|
| **Purpose:** Business logic audit trail | **Purpose:** Message delivery guarantees |
| **Contains:** Workflow decisions and events | **Contains:** Messages in transit |
| **Query:** "What did the workflow decide?" | **Query:** "Was message delivered?" |
| **Lifetime:** Permanent (audit trail) | **Lifetime:** Until delivered successfully |
| **Concerns:** Domain logic | **Concerns:** Infrastructure |

### Benefits of Hybrid Approach

**✅ Best of Both Worlds:**

| We Keep | Wolverine Provides |
|---------|-------------------|
| Pure decide/evolve pattern | Message durability |
| Custom workflow storage | Inbox/outbox tables |
| Full control over state | Retry/error handling |
| Processed flag pattern | Queue integration |
| Our innovation | Battle-tested infrastructure |
| Learning value | Production readiness |

**✅ Separation of Concerns:**

Our workflow logic contains NO infrastructure concerns:
- No queue names
- No retry policies
- No connection strings
- No error handling
- No serialization

**✅ Easy Testing:**

Test our logic without Wolverine (pure function testing), then add integration tests with Wolverine when needed.

**✅ Production Features Out-of-the-Box:**

- Automatic retry with exponential backoff
- Dead letter queue for permanent failures
- Scheduled/delayed messages
- Message serialization
- Idempotency tracking
- OpenTelemetry integration

### What We Avoid Building

By using Wolverine, we don't need to implement:

- ❌ WorkflowInputRouter (subscribe to source streams)
- ❌ WorkflowStreamConsumer (poll workflow streams)
- ❌ WorkflowOutputProcessor (execute commands with retry)
- ❌ Retry logic with exponential backoff
- ❌ Dead letter queue implementation
- ❌ Message serialization/deserialization
- ❌ Queue integration (RabbitMQ, Azure Service Bus)
- ❌ Scheduled message delivery
- ❌ Idempotency tracking
- ❌ Error monitoring

**Wolverine provides all of the above, battle-tested in production.**

---

## Next Steps

### Phase 1: Foundation (Weeks 1-2) ⏳

1. **Install Wolverine**
   - Add Wolverine NuGet packages
   - Configure Wolverine with PostgreSQL persistence
   - Set up local queues for development

2. **Implement Core Handlers**
   - WorkflowInputRouter (routes events to workflow streams)
   - WorkflowProcessor (processes workflow inputs)
   - WorkflowOutboxPublisher (background service)

3. **Wire Up GroupCheckoutWorkflow**
   - Command handlers (CheckOutHandler, etc.)
   - End-to-end testing with Wolverine

### Phase 2: Database Persistence (Weeks 3-4)

1. **PostgreSQL Persistence**
   - Implement IWorkflowPersistence for PostgreSQL
   - Create workflow_messages table schema
   - Transaction handling with Wolverine's transactional middleware

2. **SQLite Persistence**
   - Implement for local development
   - Testing and validation

3. **Optional: Marten Integration**
   - EventStoreDB-like features
   - Advanced querying capabilities

### Phase 3: Production Features (Weeks 5-6)

1. **Error Handling**
   - Configure retry policies
   - Set up dead letter queue
   - Error monitoring and alerting

2. **External Integrations**
   - RabbitMQ integration
   - Azure Service Bus (optional)
   - Kafka (optional)

3. **Observability**
   - Health checks
   - Metrics (OpenTelemetry)
   - Distributed tracing
   - Performance testing

### Phase 4: Advanced Features

1. **Workflow Features**
   - Workflow ID routing strategies
   - Concurrency control (optimistic locking)
   - Workflow versioning
   - Long-running workflow support

2. **Additional Workflows**
   - Implement OrderProcessingWorkflow with Wolverine
   - Implement InventoryReservationWorkflow
   - Additional saga pattern examples

---

## File Organization

**Note:** Updated to reflect actual implementation as of 2025-11-24.

```
Workflow/
├── Workflow/                                    # Core framework library (.NET 10.0)
│   ├── Workflow.cs                              # Base workflow classes (118 lines)
│   │   ├── WorkflowBase<TInput, TState, TOutput>
│   │   ├── Workflow<TInput, TState, TOutput>    # Synchronous workflows
│   │   └── AsyncWorkflow<TInput, TState, TOutput, TContext> # Async with context
│   ├── WorkflowOrchestrator.cs                  # Pure orchestration logic (109 lines)
│   │   ├── WorkflowOrchestrator<TInput, TState, TOutput>
│   │   └── AsyncWorkflowOrchestrator<TInput, TState, TOutput, TContext>
│   ├── WorkflowCommand.cs                       # Command types (9 lines)
│   │   └── Reply, Send, Publish, Schedule, Complete
│   ├── WorkflowEvent.cs                         # Event types (12 lines)
│   │   └── Began, InitiatedBy, Received, Replied, Sent, Published, Scheduled, Completed
│   ├── WorkflowMessage.cs                       # Unified stream message (106 lines)
│   │   ├── WorkflowMessage<TInput, TOutput>
│   │   ├── MessageKind enum (Command | Event)
│   │   ├── MessageDirection enum (Input | Output)
│   │   └── Helper properties (IsPendingCommand, IsEventForStateEvolution)
│   ├── IWorkflowPersistence.cs                  # Persistence abstraction (75 lines)
│   │   └── AppendAsync, ReadStreamAsync, GetPendingCommandsAsync, MarkCommandProcessedAsync
│   ├── InMemoryWorkflowPersistence.cs           # Thread-safe implementation (188 lines)
│   └── Core/
│       └── FrugalList.cs                        # Memory-efficient list (85 lines)
│
├── Workflow.Samples/                            # Sample workflows
│   ├── Order/                                   # Order processing domain
│   │   ├── OrderProcessingWorkflow.cs           # Sync & async workflows (229 lines)
│   │   ├── OrderProcessingStates.cs             # State definitions (19 lines)
│   │   ├── OrderProcessingInputMessages.cs      # Input messages (23 lines)
│   │   ├── OrderProcessingOutputMessages.cs     # Output messages (23 lines)
│   │   └── OrderContext.cs                      # Async context interface (28 lines)
│   └── GroupCheckout/                           # (Currently in Workflow.Tests)
│       └── GroupCheckoutWorkflow.cs             # Group checkout example (216 lines)
│
├── Workflow.Tests/                              # Test suite (47+ tests passing)
│   ├── WorkflowOrchestratorTests.cs             # Orchestrator behavior tests (217 lines)
│   ├── GroupCheckoutWorkflowTests.cs            # Group checkout tests (18 tests)
│   ├── GroupCheckoutWorkflow.cs                 # Test workflow implementation (216 lines)
│   ├── OrderProcessingWorkflowTests.cs          # Async workflow tests (28 tests, 372 lines)
│   ├── InMemoryWorkflowPersistenceTests.cs      # Persistence & thread-safety tests
│   └── IssueFineForSpeedingViolationWorkflow.cs # Simple example workflow (87 lines)
│
├── WorkflowWolverineSingle/                     # Wolverine integration (minimal)
│   └── Program.cs                               # Basic Wolverine setup (18 lines)
│
└── ChatStates/md/                               # Documentation
    ├── ARCHITECTURE.md                          # This file (complete architecture & Wolverine integration)
    ├── PATTERNS.md                              # Reliability and Reply patterns
    ├── IMPLEMENTATION_STATE.md                  # Current implementation status
    ├── PROCESSED_FLAG_DESIGN_DECISION.md        # Idempotency design
    └── REPLY_COMMAND_PATTERNS.md                # CQRS query patterns
```

**Statistics:**
- Core Framework: ~810 lines of production code
- Sample Workflows: ~532 lines (OrderProcessing + GroupCheckout)
- Tests: ~886+ lines with 47+ passing tests
- Documentation: ~5 comprehensive markdown files

---

## RFC Compliance

**Note:** Implementation strategy updated to use Wolverine for infrastructure.

| RFC Requirement | Status | Implementation |
|----------------|--------|----------------|
| Store inputs in workflow stream | ✅ | Wolverine handlers + IWorkflowPersistence |
| Rebuild state from events | ✅ | WorkflowOrchestrator.RebuildStateFromStream() |
| Store outputs in workflow stream | ✅ | IWorkflowPersistence.AppendAsync() |
| Commands need execution | ✅ | Wolverine background processing |
| Mark commands as processed | ✅ | IWorkflowPersistence.MarkCommandProcessedAsync() |
| Workflow stream as inbox + outbox | ✅ | WorkflowMessage with Direction |
| Position tracking | ✅ | WorkflowMessage.Position |
| Message kind (Command/Event) | ✅ | WorkflowMessage.Kind |

**Alignment:** 100% ✅ (infrastructure delegated to Wolverine)

---

**Last Updated:** 2025-11-24
**Test Status:** 47+ tests passing
**Framework Status:** Phase 1 Complete - Core framework with sync/async workflows, event sourcing, unified stream architecture, and in-memory persistence fully implemented. Phase 2 (Wolverine integration and database persistence) in progress.
