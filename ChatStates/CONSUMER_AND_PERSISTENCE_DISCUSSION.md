# Consumer and Persistence Architecture Discussion

**Date:** 2025-11-01

---

## Context

After implementing the generic `WorkflowOrchestrator` and `WorkflowStreamProcessor`, we discussed the proper separation of concerns based on the RFC pattern, particularly around:
1. The role of consumers in the workflow architecture
2. Who should be responsible for persistence at each stage
3. How the processor should interact with persisted state

---

## Question 1: Should Consumer Be a Separated Thing?

### Initial Implementation
We had conflated the consumer and processor into a single `WorkflowStreamProcessor` class that:
- Stored input messages
- Rebuilt state from the stream
- Called decide/translate
- Stored output messages

### RFC Architecture (Lines 130-158)

According to the RFC, there should be clear separation:

```
Source Stream → Consumer → Workflow Processor → Workflow Stream
                                ↓
                    Workflow Stream → Consumer → Processor (rebuild/decide)
                                                      ↓
                                            Workflow Stream (outputs)
                                                      ↓
                                            Output Handler
```

### What We Should Have

1. **WorkflowInputRouter** - Routes from source streams to workflow streams based on `getWorkflowId`
2. **WorkflowStreamConsumer** - Subscribes to workflow streams and triggers processing
3. **WorkflowStreamProcessor** - Pure processing logic (rebuild state → decide → store outputs)
4. **WorkflowOutputProcessor** - Executes output commands ✓ (already implemented correctly)

**Benefits of Separation:**
- Clear boundaries: Each component has one responsibility
- Testability: Can test routing separate from processing
- Flexibility: Can swap consumer implementations (polling, push, etc.)
- Matches RFC: Double-hop pattern is explicit

---

## Question 2: What Does the Consumer Do Exactly?

### Two Different Consumer Roles

#### 1. Input Consumer/Router (RFC Lines 130-133)

**Purpose**: Routes messages from source streams to the correct workflow instance stream

**Responsibilities**:
1. Subscribes to source streams (e.g., `GuestCheckout` stream, `Orders` stream)
2. Filters for messages the workflow cares about (based on `inputs` configuration)
3. Calls `getWorkflowId(message)` to determine which workflow instance should handle this
4. Stores the message in the workflow's stream as an Input message

**Example**:
```csharp
// Event arrives: GuestCheckedOut { guestId: "g1", groupCheckoutId: "group-123" }
// Router calls: getWorkflowId(event) → returns "group-123"
// Router stores in: workflow-group-123 stream
```

This is the **routing/forwarding** layer - ensures messages get to the right workflow instance.

#### 2. Workflow Stream Consumer (RFC Line 134)

**Purpose**: Processes messages from the workflow's own stream

**Responsibilities**:
1. Subscribes to workflow instance streams (pattern: `workflow-*`)
2. Detects new messages in the workflow stream (inputs from router, or resuming after restart)
3. Triggers processing:
   - Reads all events from stream
   - Rebuilds state
   - Calls decide
   - Stores outputs
4. Tracks checkpoint (position in stream) for exactly-once processing

**Example**:
```csharp
// New message appears at position 6 in workflow-group-123 stream
// Consumer reads positions 1-6
// Consumer triggers: rebuild state → decide → store outputs
// Consumer saves checkpoint: 6
```

This is the **processing trigger** - makes workflows reactive to new messages.

### What's Missing

Currently, `WorkflowStreamProcessor` does the processing logic but **there's no consumer infrastructure** that:
- ✗ Subscribes to streams automatically
- ✗ Polls for new messages
- ✗ Manages checkpoints
- ✗ Handles retries/errors
- ✗ Provides at-least-once/exactly-once delivery

**The consumer is the background service that makes workflows event-driven** rather than manually triggered.

---

## Question 3: Who Should Persist?

### Persistence Responsibilities (RFC Lines 131-138)

#### 1. InputRouter - Persists Inputs (Step 3)

```
Source Stream → InputRouter
                    ↓
                getWorkflowId(message)
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

#### 2. WorkflowProcessor - Persists Outputs (Step 7)

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

### Current Implementation Issue

`WorkflowStreamProcessor.ProcessAsync` currently does BOTH:

```csharp
// Step 1: Store input ← THIS SHOULD BE DONE BY ROUTER
await persistence.AppendAsync(workflowId, [inputMessage]);

// Steps 2-4: Process
var orchestrationResult = orchestrator.Process(...);

// Step 5: Store outputs ← THIS IS CORRECT
await persistence.AppendAsync(workflowId, outputMessages);
```

### Correct Separation

1. **InputRouter** - has IWorkflowPersistence, persists inputs
2. **WorkflowStreamProcessor** - has IWorkflowPersistence, persists outputs ONLY
3. **WorkflowStreamConsumer** - coordinates but doesn't persist (delegates to router/processor)
4. **WorkflowOutputProcessor** - no persistence, just executes commands and marks as processed

---

## Question 4: WorkflowProcessor Reads State from Persistence?

### Yes! WorkflowProcessor Has Two Persistence Operations

**READS** from persistence (to rebuild state):
```csharp
// Rebuild state from ALL events in the stream
var allMessages = await persistence.ReadStreamAsync(workflowId);
var snapshot = RebuildStateFromStream(workflow, allMessages);
```

**WRITES** to persistence (to store outputs):
```csharp
// Store outputs (commands + events) after processing
await persistence.AppendAsync(workflowId, outputMessages);
```

### The Complete Flow

```
1. InputRouter:
   - Receives: GuestCheckedOut from source stream
   - Determines: workflowId = "group-123"
   - Persists: AppendAsync("group-123", inputMessage)

2. WorkflowStreamConsumer:
   - Detects: New message at position 6 in "group-123" stream
   - Triggers: WorkflowProcessor.ProcessAsync(workflow, "group-123", fromPosition: 6)

3. WorkflowProcessor:
   - Reads: ReadStreamAsync("group-123") → positions 1-6 ← FROM PERSISTENCE
   - Rebuilds: State from output events at positions 1-5
   - Gets: Message at position 6 (the trigger)
   - Decides: workflow.Decide(message, state)
   - Persists: AppendAsync("group-123", outputMessages) ← TO PERSISTENCE

4. WorkflowOutputProcessor:
   - Reads: GetPendingCommandsAsync()
   - Executes: Commands from outputs
   - Marks: MarkCommandProcessedAsync(workflowId, position)
```

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

## Summary of Architectural Decisions

### Component Responsibilities

| Component | Subscribes To | Persists | Reads | Other Responsibilities |
|-----------|---------------|----------|-------|----------------------|
| **InputRouter** | Source streams | Inputs ✓ | - | Routes via getWorkflowId |
| **WorkflowStreamConsumer** | Workflow streams | - | - | Triggers processing, manages checkpoints |
| **WorkflowStreamProcessor** | - | Outputs ✓ | All messages ✓ | Rebuilds state, calls decide/translate |
| **WorkflowOutputProcessor** | - | Processed flag ✓ | Pending commands ✓ | Executes commands via ICommandExecutor |

### Key Principles

1. **Input persistence happens at the edge** (router), not during processing
2. **Processor assumes inputs are already in the stream** when triggered
3. **Processor reads entire stream** to rebuild state, then processes from trigger position
4. **Output persistence happens in processor** before execution
5. **Consumer is the glue** that makes everything reactive and event-driven

---

## Next Steps

1. Create `WorkflowInputRouter<TInput, TState, TOutput>`
   - Subscribe to source streams
   - Implement `getWorkflowId` routing logic
   - Persist inputs to workflow streams

2. Create `WorkflowStreamConsumer<TInput, TState, TOutput>`
   - Subscribe to workflow instance streams
   - Detect new messages
   - Trigger `WorkflowStreamProcessor` with position
   - Manage checkpoints

3. Refactor `WorkflowStreamProcessor<TInput, TState, TOutput>`
   - Remove input persistence
   - Change signature to accept `fromPosition` instead of message
   - Keep output persistence
   - Keep state rebuilding from persistence

4. Keep `WorkflowOutputProcessor<TInput, TState, TOutput>` as-is
   - Already correctly implemented

---

## Implementation Status

- ✅ WorkflowOrchestrator (pure business logic)
- ✅ WorkflowStreamProcessor (needs refactoring - remove input persistence)
- ✅ WorkflowOutputProcessor (correct as-is)
- ✅ InMemoryWorkflowPersistence (complete with tests)
- ⏳ WorkflowInputRouter (needs implementation)
- ⏳ WorkflowStreamConsumer (needs implementation)
