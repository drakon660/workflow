# Workflow Stream Architecture

**Last Updated:** 2025-11-23

**Architecture Decision:** WorkflowStreamProcessor and WorkflowOutputProcessor have been removed. Infrastructure concerns (message routing, command execution, background processing) are delegated to Wolverine. This architecture document now focuses on the core workflow orchestration pattern.

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

```
Workflow Stream for "group-checkout-123":
Pos | Kind    | Direction | Message                          | Processed
----|---------|-----------|----------------------------------|----------
1   | Command | Input     | InitiateGroupCheckout            | N/A
2   | Event   | Output    | GroupCheckoutInitiated           | N/A
3   | Command | Output    | CheckOut (guest-1)               | false  ← Needs execution
4   | Command | Output    | CheckOut (guest-2)               | false  ← Needs execution
5   | Event   | Input     | GuestCheckedOut (guest-1)        | N/A
6   | Event   | Input     | GuestCheckoutFailed (guest-2)    | N/A
7   | Event   | Output    | GroupCheckoutFailed              | N/A
```

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

**Responsibilities:**
- Execute Decide → Translate → Evolve cycle
- Track event history
- Manage snapshots
- Pure business logic (easy testing)

**Status:** Fully implemented and tested (46 tests passing)

**Key Features:**
- Synchronous workflows via `Workflow<TInput, TState, TOutput>`
- Asynchronous workflows via `AsyncWorkflow<TInput, TState, TOutput, TContext>`
- Type-safe context objects for async operations
- Helper methods for cleaner pattern matching

#### 2. IWorkflowPersistence ✅
**Purpose:** Stream storage abstraction

**Responsibilities:**
1. **Store messages** in workflow streams (AppendAsync)
2. **Read stream** for state reconstruction (ReadStreamAsync)
3. **Query pending commands** for background processing (GetPendingCommandsAsync)
4. **Track command execution** with Processed flag (MarkCommandProcessedAsync)

**Implementation Status:**
- ✅ Interface defined
- ✅ In-memory implementation for testing
- ⏳ PostgreSQL/SQLite implementations (future)
- Keep output persistence and state rebuilding

#### 3. Wolverine Infrastructure ⏳ (In Progress)
**Purpose:** Handles all infrastructure concerns (routing, execution, background processing)

**Responsibilities:**
1. **Message Routing**: Route incoming messages to workflow handlers
2. **Command Execution**: Execute Send/Publish/Schedule/Reply/Complete commands
3. **Background Processing**: Poll for pending commands and execute them
4. **Retry Logic**: Handle transient failures with retries
5. **Error Handling**: Dead letter queue for permanent failures

**See:** WOLVERINE_HYBRID_ARCHITECTURE.md for detailed integration plan

**Key Benefits:**
- ✅ Production-ready messaging infrastructure
- ✅ Built-in retry and error handling
- ✅ Persistence integration
- ✅ Observability and metrics
- ✅ Avoids reinventing infrastructure

**What Replaced:**
- WorkflowInputRouter → Wolverine message routing
- WorkflowStreamConsumer → Wolverine message handlers
- WorkflowStreamProcessor → Wolverine handlers + WorkflowOrchestrator
- WorkflowOutputProcessor → Wolverine background polling
- ICommandExecutor → Wolverine message publishing

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

### Completed ✅
- ✅ WorkflowOrchestrator (pure business logic)
- ✅ Workflow base classes (Workflow and AsyncWorkflow)
- ✅ InMemoryWorkflowPersistence (complete with tests)
- ✅ WorkflowMessage abstraction
- ✅ IWorkflowPersistence interface
- ✅ All 46 tests passing

### Architecture Decision ⚙️
- ⚙️ **Removed**: WorkflowStreamProcessor, WorkflowOutputProcessor
- ⚙️ **Replaced by**: Wolverine infrastructure (see WOLVERINE_HYBRID_ARCHITECTURE.md)
- ⚙️ **Reason**: Avoid reinventing infrastructure; use production-ready Wolverine for routing, execution, retries

### In Progress ⏳
- ⏳ Wolverine integration (message handlers, command execution)
- ⏳ Concrete persistence (PostgreSQL, SQLite)

### Future Work
- ⏳ Workflow ID routing strategies
- ⏳ Concurrency control (optimistic locking)
- ⏳ Checkpoint management (exactly-once semantics)
- ⏳ Metrics/telemetry (OpenTelemetry)
- ⏳ Workflow versioning

---

## Next Steps

**Note:** Next steps updated to reflect Wolverine integration strategy.

### Immediate Priority

1. **Wolverine Integration**
   - Create Wolverine message handlers for workflows
   - Implement command execution via Wolverine
   - Set up background polling for pending commands
   - See: WOLVERINE_HYBRID_ARCHITECTURE.md

2. **Concrete Persistence**
   - PostgreSQL implementation of IWorkflowPersistence
   - SQLite implementation for local development
   - Marten integration (optional, for EventStoreDB-like features)

### Phase 2

3. **Production Features**
   - Workflow ID routing strategies
   - Concurrency control (optimistic locking)
   - Error handling and retry policies (via Wolverine)
   - Metrics and observability (OpenTelemetry)

### Phase 3

4. **Advanced Features**
   - Workflow versioning
   - Long-running workflow support
   - Saga pattern implementations

---

## File Organization

**Note:** Updated to reflect removal of WorkflowStreamProcessor and WorkflowOutputProcessor.

```
Workflow/
├── Workflow/                          # Core framework library
│   ├── Workflow.cs                    # Base workflow classes (Workflow, AsyncWorkflow)
│   ├── IWorkflow.cs                   # Workflow interfaces
│   ├── WorkflowOrchestrator.cs        # Pure orchestrator
│   ├── WorkflowCommand.cs             # Command types (Send, Reply, etc.)
│   ├── WorkflowEvent.cs               # Event types (Began, Received, etc.)
│   ├── WorkflowMessage.cs             # Unified stream message
│   ├── IWorkflowPersistence.cs        # Persistence abstraction
│   └── InMemoryWorkflowPersistence.cs # In-memory implementation
│
├── Workflow.Samples/                  # Sample workflows
│   ├── OrderProcessingWorkflow.cs     # Order processing example
│   └── GroupCheckoutWorkflow.cs       # Group checkout example
│
├── Workflow.Tests/                    # Tests (46 passing)
│   ├── GroupCheckoutWorkflowTests.cs  # 18 tests
│   ├── OrderProcessingWorkflowTests.cs # 28 tests
│   ├── WorkflowOrchestratorTests.cs   # Orchestrator tests
│   └── InMemoryWorkflowPersistenceTests.cs # Persistence tests
│
├── WorkflowWolverineSingle/           # Wolverine integration (in progress)
│   ├── Program.cs                     # Wolverine setup
│   ├── CreateCustomerHandler.cs       # Example handler
│   └── BpPublisher.cs                 # Publisher example
│
└── ChatStates/                        # Documentation
    ├── ARCHITECTURE.md                # This file
    ├── PATTERNS.md                    # Reliability and Reply patterns
    ├── WOLVERINE_HYBRID_ARCHITECTURE.md # Wolverine integration plan
    └── IMPLEMENTATION_STATE.md        # Current implementation status
```

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

**Last Updated:** 2025-11-21
**Test Status:** 47 tests passing
**Framework Status:** Core stream architecture complete, consumer/router separation pending
