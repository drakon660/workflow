# Processed Flag Design Decision

**Date:** 2025-11-23

**Topic:** Command Execution Tracking - `Processed` Flag vs Original Patterns

---

## Table of Contents

1. [Overview](#overview)
2. [Original Patterns Analysis](#original-patterns-analysis)
3. [Our Innovation: The Processed Flag](#our-innovation-the-processed-flag)
4. [Trade-offs Analysis](#trade-offs-analysis)
5. [Design Decision](#design-decision)
6. [Implementation Guidelines](#implementation-guidelines)
7. [Concurrency Safeguards](#concurrency-safeguards)

---

## Overview

During implementation, we added a `Processed` flag to track command execution status:

```csharp
public record WorkflowMessage<TInput, TOutput>(
    string WorkflowId,
    long Position,
    MessageKind Kind,            // Command | Event
    MessageDirection Direction,  // Input | Output
    object Message,
    DateTime Timestamp,
    bool? Processed              // ← OUR INNOVATION: null for events, bool for commands
);
```

**Key Discovery:** This pattern is NOT mentioned in either:
- Yves Reynhout's "The Workflow Pattern" (workflow.txt)
- Oskar Dudycz's Emmett RFC (RFC.txt)

This document analyzes why we added it and whether it's the right approach.

---

## Original Patterns Analysis

### Yves Reynhout's Approach (workflow.txt)

**No explicit "processed" tracking in the stream.**

**Stream Structure:**
```
Position | Event
---------|-----------------------------------------------
1        | Began
2        | InitiatedBy(PoliceReportPublished)
3        | Sent(GenerateSystemNumber)        ← Command as event
4        | Received(SystemNumberGenerated)
5        | Sent(GenerateManualCode)
6        | Received(ManualCodeGenerated)
7        | Sent(IssueTrafficFine)
8        | Completed
```

**Command Execution Tracking:**

From workflow.txt:347:
> "It's important that... keeping track of the position you've reached in a catch-up subscription in a durable fashion matters."

**How it works:**
1. Background processor maintains checkpoint (e.g., "last processed position: 3")
2. Reads events from checkpoint onwards
3. Executes commands found in events (e.g., `Sent(GenerateSystemNumber)`)
4. Updates checkpoint after successful execution
5. Uses `message_id` for idempotency at handler level

**Idempotency Mechanism:**

From workflow.txt:347:
> "the workflow command's message identity is your friend. It can help deal with idempotency."

```fsharp
// Processor adds message_id to envelope
let private envelop workflow_id message_id m =
    envelope.Attributes.Add("workflow_id", workflow_id)
    envelope.Attributes.Add("message_id", message_id)  // ← Idempotency key
```

**Handler checks message_id:**
```fsharp
// Command handler
let handle message =
    if alreadyProcessed(message.message_id) then
        return // Skip - already executed

    executeCommand(message)
    recordProcessed(message.message_id)
```

**Checkpoint Storage:**

Separate from event stream:
```
Checkpoint Table:
processor_id          | last_position
----------------------|---------------
IssueTrafficFineProc | 3
GroupCheckoutProc     | 42
```

---

### Oskar Dudycz's Approach (RFC.txt)

**Also no explicit "processed" flag.**

**Stream Structure (RFC.txt:277):**
```
Pos | Kind    | Direction | Message
----|---------|-----------|------------------------------------------
1   | Command | Input     | InitiateGroupCheckout {groupId: '123'}
2   | Event   | Output    | GroupCheckoutInitiated
3   | Command | Output    | CheckOut {guestId: 'g1'}
4   | Command | Output    | CheckOut {guestId: 'g2'}
5   | Command | Output    | CheckOut {guestId: 'g3'}
6   | Event   | Input     | GuestCheckedOut {guestId: 'g1'}
```

**Command Execution (RFC.txt:297):**
```typescript
// Workflow event processor carries out the workflow commands
module IssueTrafficFineForSpeedingViolationWorkflowProcessor =
    let handle clients workflow_id message_id command =
        task {
            match command with
            | Send(GenerateTrafficFineSystemNumber m) ->
                let envelope = envelop workflow_id message_id m
                do! clients.SystemNumberTopic.PublishAsync(envelope)
```

**Tracking Mechanism:**

1. **Subscription checkpoints** (RFC.txt:347):
   - Track last processed position per subscription
   - Resume from checkpoint after crash

2. **Message ID for idempotency** (RFC.txt:312):
```fsharp
let private envelop workflow_id message_id m =
    let json = JsonSerializer.Serialize(m, options)
    let envelope = PubsubMessage(Data = ByteString.CopyFromUtf8(json))
    envelope.Attributes.Add("workflow_id", workflow_id)
    envelope.Attributes.Add("message_id", message_id)  // ← For deduplication
    envelope
```

**Recovery After Crash:**
```
1. Read last checkpoint (e.g., position 3)
2. Read events from position 4 onwards
3. Process each command event
4. Rely on message_id to prevent duplicate execution
5. Update checkpoint after successful processing
```

---

## Our Innovation: The Processed Flag

### Why We Added It

Our design stores commands explicitly WITH execution status:

```csharp
public record WorkflowMessage<TInput, TOutput>(
    ...
    bool? Processed  // null = Event, false = Pending Command, true = Executed Command
);
```

**Stream Structure:**
```
Pos | Kind    | Direction | Message              | Processed
----|---------|-----------|----------------------|----------
1   | Command | Input     | InitiateGroupCheckout| N/A
2   | Event   | Output    | GroupCheckoutInitiated| N/A
3   | Command | Output    | CheckOut(guest-1)    | false     ← Not executed yet
4   | Command | Output    | CheckOut(guest-2)    | false     ← Not executed yet
5   | Event   | Input     | GuestCheckedOut(g1)  | N/A
6   | Command | Output    | CheckOut(guest-1)    | true      ← Already executed
7   | Command | Output    | CheckOut(guest-2)    | true      ← Already executed
```

### Advantages Over Original Patterns

#### 1. Explicit Visibility

**Original:** Need to infer what's pending
```csharp
// Must track externally
var checkpoint = await GetCheckpoint("GroupCheckoutProcessor");
var events = await ReadStream(workflowId, fromPosition: checkpoint);
var commands = events.Where(e => e is Sent or Published or Scheduled);
// Still need to check message_id against execution log
```

**Ours:** Direct query
```csharp
// Simple, explicit query
var pending = await persistence.GetPendingCommandsAsync();
// Returns all commands where Processed = false
```

#### 2. Simpler Recovery

**Original:** Replay from checkpoint with idempotency checks
```
1. Crash occurs after processing position 5
2. Restart, read checkpoint: position 4
3. Replay positions 5, 6, 7...
4. For each command, check message_id against execution log
5. Skip if already executed, execute if new
6. Update checkpoint
```

**Ours:** Find unprocessed commands
```csharp
// After crash, just find what wasn't finished
var allMessages = await persistence.ReadStreamAsync(workflowId);
var pending = allMessages.Where(m => m.IsPendingCommand);
// Execute only these, mark as processed
```

#### 3. Better Observability

**Original:** Execution status external to stream
```
Event Stream:           Execution Log:
- Sent(Command1)       message_id_1 → executed at 10:00
- Sent(Command2)       message_id_2 → executed at 10:05
- Sent(Command3)       (not in log - pending)

Need to join two data sources to see full picture
```

**Ours:** Status visible in stream
```
Workflow Stream:
Pos 3: Command(CheckOut) | Processed: true  | ExecutedAt: 10:00
Pos 4: Command(CheckOut) | Processed: false | (pending)
Pos 5: Command(CheckOut) | Processed: true  | ExecutedAt: 10:05

Single source of truth for complete lifecycle
```

#### 4. No Separate Outbox Table

**Original Patterns:**
```
Event Store:
- Events stored here

Outbox/Execution Table:
- message_id | executed | executed_at
- abc-123    | true     | 2025-11-23 10:00
- def-456    | true     | 2025-11-23 10:05

Checkpoint Table:
- processor_id | last_position
- ProcessorA   | 42
```

**Our Approach:**
```
Workflow Stream (single table):
- position | kind    | message           | processed
- 1        | Command | CheckOut(g1)      | false
- 2        | Command | CheckOut(g2)      | true
- 3        | Event   | GuestCheckedOut   | null

Stream IS the outbox. Processed flag IS the tracking.
```

---

## Trade-offs Analysis

### Original Patterns (No Processed Flag)

**Pros:**
- ✅ **Immutable stream** - Pure event sourcing, append-only
- ✅ **Standard pattern** - Well-documented in industry
- ✅ **EventStoreDB compatible** - Works with immutable event stores
- ✅ **Separate concerns** - Events in event store, execution tracking separate

**Cons:**
- ❌ **Complex recovery** - Need checkpoint management + idempotency checks
- ❌ **External tracking** - Execution state stored separately from stream
- ❌ **Harder queries** - "What commands are pending?" requires joins
- ❌ **More infrastructure** - Need checkpoint storage + execution log
- ❌ **Message ID management** - Must track message IDs for idempotency

**Example Complexity:**
```csharp
// Original pattern recovery
public async Task RecoverAndProcess()
{
    // 1. Load checkpoint
    var checkpoint = await checkpointStore.GetCheckpoint(processorId);

    // 2. Read events from checkpoint
    var events = await eventStore.ReadStream(workflowId, fromPosition: checkpoint);

    // 3. Filter for command events
    var commands = events.Where(e => e is Sent or Published or Scheduled);

    // 4. For each command, check if already executed
    foreach (var cmd in commands)
    {
        var messageId = cmd.Metadata.MessageId;

        if (await executionLog.IsExecuted(messageId))
            continue; // Skip - already done

        // 5. Execute and record
        await ExecuteCommand(cmd);
        await executionLog.MarkExecuted(messageId);

        // 6. Update checkpoint
        await checkpointStore.UpdateCheckpoint(processorId, cmd.Position);
    }
}
```

---

### Our Approach (With Processed Flag)

**Pros:**
- ✅ **Explicit tracking** - Execution status in stream itself
- ✅ **Simple queries** - `WHERE processed = false` to find pending
- ✅ **Easy recovery** - Just find and process unprocessed commands
- ✅ **Self-contained** - No external checkpoint/execution tables needed
- ✅ **Better observability** - See complete command lifecycle in one place
- ✅ **Simpler codebase** - Fewer moving parts, less infrastructure

**Cons:**
- ⚠️ **Stream mutation** - Updates `Processed` flag (not pure append-only)
- ⚠️ **Not pure event sourcing** - Events should be immutable
- ⚠️ **Race condition potential** - Two processors might mark same command
- ⚠️ **EventStoreDB incompatible** - Cannot update existing events
- ⚠️ **Schema requirement** - Need mutable storage (SQL databases)

**Example Simplicity:**
```csharp
// Our approach recovery
public async Task RecoverAndProcess()
{
    // 1. Get all pending commands (single query)
    var pending = await persistence.GetPendingCommandsAsync(workflowId);

    // 2. Process each
    foreach (var cmd in pending)
    {
        await ExecuteCommand(cmd);

        // 3. Mark as processed (UPDATE operation)
        await persistence.MarkCommandProcessedAsync(
            cmd.WorkflowId,
            cmd.Position
        );
    }
}
```

---

## Design Decision

### Decision: Keep the `Processed` Flag ✅

**Rationale:**

1. **Target Infrastructure: Relational Databases**
   - PostgreSQL, SQLite, SQL Server (planned)
   - All support UPDATE operations efficiently
   - Mutation is acceptable and well-supported

2. **Not Using EventStoreDB**
   - No immutability constraint from event store
   - Relational DB patterns are more familiar to team
   - Can always add EventStoreDB support later with adapter pattern

3. **Simpler Implementation**
   - Fewer tables (no separate checkpoint/execution tables)
   - Straightforward queries
   - Easier to understand and maintain

4. **Better Developer Experience**
   - Easy to debug (see command status directly)
   - Simple mental model (flag = execution state)
   - Less infrastructure to manage

5. **Good Observability**
   - Complete audit trail in stream
   - Can answer: "Which commands executed? When? Which are pending?"
   - Single source of truth

### When This Decision Should Be Reconsidered

**Reconsider if:**
- ✗ Need to support EventStoreDB or other immutable event stores
- ✗ Stream mutation becomes performance bottleneck
- ✗ Need strict event sourcing compliance (e.g., regulatory requirements)
- ✗ Multiple independent processors need to execute same commands
- ✗ Temporal consistency requirements (read-your-writes guarantees)

**In these cases, switch to:**
- Separate execution tracking table
- OR: Execution events (append-only approach)
- OR: Hybrid model (both patterns available)

---

## Implementation Guidelines

### 1. Database Schema

**PostgreSQL:**
```sql
CREATE TABLE workflow_messages (
    workflow_id         TEXT        NOT NULL,
    position            BIGINT      NOT NULL,
    kind                CHAR(1)     NOT NULL,  -- 'C' = Command, 'E' = Event
    direction           CHAR(1)     NOT NULL,  -- 'I' = Input, 'O' = Output
    message_type        TEXT        NOT NULL,
    message_data        JSONB       NOT NULL,
    message_metadata    JSONB       NOT NULL,
    processed           BOOLEAN,               -- NULL for events, bool for commands
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    processed_at        TIMESTAMPTZ,           -- When command was executed

    PRIMARY KEY (workflow_id, position)
);

-- Index for finding pending commands
CREATE INDEX idx_pending_commands
ON workflow_messages (workflow_id, processed)
WHERE kind = 'C' AND direction = 'O' AND processed = false;
```

**SQLite:**
```sql
CREATE TABLE workflow_messages (
    workflow_id         TEXT    NOT NULL,
    position            INTEGER NOT NULL,
    kind                TEXT    NOT NULL,  -- 'Command' | 'Event'
    direction           TEXT    NOT NULL,  -- 'Input' | 'Output'
    message_type        TEXT    NOT NULL,
    message_data        TEXT    NOT NULL,  -- JSON string
    message_metadata    TEXT    NOT NULL,  -- JSON string
    processed           INTEGER,            -- NULL for events, 0/1 for commands
    created_at          TEXT    NOT NULL DEFAULT (datetime('now')),
    processed_at        TEXT,

    PRIMARY KEY (workflow_id, position)
);

CREATE INDEX idx_pending_commands
ON workflow_messages (workflow_id, processed)
WHERE kind = 'Command' AND direction = 'Output' AND processed = 0;
```

### 2. Interface Design

```csharp
public interface IWorkflowPersistence<TInput, TOutput>
{
    /// <summary>
    /// Appends messages to workflow stream.
    /// Commands are inserted with Processed = false by default.
    /// </summary>
    Task<long> AppendAsync(
        string workflowId,
        IReadOnlyList<WorkflowMessage<TInput, TOutput>> messages
    );

    /// <summary>
    /// Reads entire stream for state rebuilding.
    /// </summary>
    Task<IReadOnlyList<WorkflowMessage<TInput, TOutput>>> ReadStreamAsync(
        string workflowId,
        long fromPosition = 0
    );

    /// <summary>
    /// Gets all pending commands (Processed = false).
    /// Can filter by workflowId or get all pending commands across all workflows.
    /// </summary>
    Task<IReadOnlyList<WorkflowMessage<TInput, TOutput>>> GetPendingCommandsAsync(
        string? workflowId = null
    );

    /// <summary>
    /// Marks command as processed (Processed = false → true).
    /// Uses optimistic concurrency to prevent double-execution.
    /// Returns true if updated, false if already processed.
    /// </summary>
    Task<bool> MarkCommandProcessedAsync(
        string workflowId,
        long position
    );
}
```

### 3. Usage Pattern

```csharp
// 1. Workflow generates commands
var result = orchestrator.Process(workflow, snapshot, message, begins: false);

// 2. Store commands in stream (Processed = false)
await persistence.AppendAsync(workflowId, result.OutputMessages);

// 3. Background processor polls for pending
var pending = await persistence.GetPendingCommandsAsync();

// 4. Execute each command
foreach (var cmd in pending)
{
    await commandExecutor.ExecuteAsync((TOutput)cmd.Message);

    // 5. Mark as processed (atomic update)
    await persistence.MarkCommandProcessedAsync(cmd.WorkflowId, cmd.Position);
}
```

### 4. Documentation

```csharp
/// <summary>
/// Tracks command execution status within the workflow stream.
///
/// DESIGN DECISION:
/// This field makes the workflow stream mutable, which differs from pure
/// event sourcing patterns where streams are append-only.
///
/// RATIONALE:
/// - Optimized for relational databases (PostgreSQL, SQLite) where UPDATE
///   operations are efficient and well-supported
/// - Provides explicit command execution tracking without separate tables
/// - Simpler recovery and observability compared to checkpoint-based approaches
///
/// TRADE-OFF:
/// This approach is NOT compatible with EventStoreDB or other immutable
/// event stores. For those systems, use a separate execution tracking table.
///
/// VALUES:
/// - null: Message is an Event (no execution tracking needed)
/// - false: Message is a Command that has NOT been executed yet
/// - true: Message is a Command that HAS been executed successfully
/// </summary>
public bool? Processed { get; init; }
```

---

## Concurrency Safeguards

### Problem: Race Condition

Two processors might try to execute the same command:

```
Time | Processor A                    | Processor B
-----|--------------------------------|--------------------------------
T1   | Query: GetPendingCommands()    |
T2   |   → Returns command at pos 3   |
T3   |                                | Query: GetPendingCommands()
T4   |                                |   → Returns command at pos 3 (same!)
T5   | Execute command                |
T6   |                                | Execute command (duplicate!)
T7   | Mark processed                 |
T8   |                                | Mark processed
```

### Solution 1: Optimistic Concurrency (Recommended)

**PostgreSQL:**
```sql
-- Only update if still unprocessed
UPDATE workflow_messages
SET
    processed = true,
    processed_at = NOW()
WHERE
    workflow_id = $1
    AND position = $2
    AND processed = false  -- ← Critical: only update if still pending
RETURNING processed;

-- Returns:
-- - Row with processed=true if we successfully marked it
-- - No rows if already processed (another processor got it first)
```

**Implementation:**
```csharp
public async Task<bool> MarkCommandProcessedAsync(string workflowId, long position)
{
    var sql = @"
        UPDATE workflow_messages
        SET processed = true, processed_at = NOW()
        WHERE workflow_id = @workflowId
          AND position = @position
          AND processed = false
        RETURNING processed";

    var result = await connection.QuerySingleOrDefaultAsync<bool?>(sql, new { workflowId, position });

    return result.HasValue; // true if we updated it, false if already processed
}
```

**Usage:**
```csharp
foreach (var cmd in pending)
{
    // Execute command first
    await commandExecutor.ExecuteAsync((TOutput)cmd.Message);

    // Try to mark as processed
    var wasProcessed = await persistence.MarkCommandProcessedAsync(cmd.WorkflowId, cmd.Position);

    if (!wasProcessed)
    {
        // Another processor already executed and marked it
        // This is OK - command execution should be idempotent anyway
        logger.LogInformation(
            "Command {Position} already processed by another processor",
            cmd.Position
        );
    }
}
```

### Solution 2: Advisory Locks (PostgreSQL-specific)

```csharp
public async Task<bool> TryExecuteCommandWithLock(WorkflowMessage cmd)
{
    // Generate deterministic lock ID from workflow + position
    var lockId = HashCode.Combine(cmd.WorkflowId, cmd.Position);

    // Try to acquire advisory lock (non-blocking)
    var sql = "SELECT pg_try_advisory_lock(@lockId)";
    var acquired = await connection.QuerySingleAsync<bool>(sql, new { lockId });

    if (!acquired)
    {
        // Another processor is working on this command
        return false;
    }

    try
    {
        // We have the lock - execute command
        await commandExecutor.ExecuteAsync((TOutput)cmd.Message);

        // Mark as processed
        await MarkCommandProcessedAsync(cmd.WorkflowId, cmd.Position);

        return true;
    }
    finally
    {
        // Release lock
        await connection.ExecuteAsync("SELECT pg_advisory_unlock(@lockId)", new { lockId });
    }
}
```

### Solution 3: Idempotent Commands (Defense in Depth)

Even with concurrency controls, make commands idempotent:

```csharp
// Command handler should check if already executed
public class CheckOutGuestHandler : ICommandHandler<CheckOut>
{
    public async Task HandleAsync(CheckOut command)
    {
        var guest = await repository.GetAsync(command.GuestId);

        // Idempotency check
        if (guest.Status == GuestStatus.CheckedOut)
        {
            logger.LogInformation("Guest {GuestId} already checked out", command.GuestId);
            return; // Already done - no-op
        }

        // Execute checkout
        guest.CheckOut();
        await repository.SaveAsync(guest);
    }
}
```

### Recommended Approach: Layered Defense

```csharp
public async Task ProcessPendingCommandsAsync()
{
    var pending = await persistence.GetPendingCommandsAsync();

    foreach (var cmd in pending)
    {
        // Layer 1: Optimistic concurrency (mark before execute)
        var wasMarked = await persistence.MarkCommandProcessedAsync(
            cmd.WorkflowId,
            cmd.Position
        );

        if (!wasMarked)
        {
            // Another processor already claimed it
            continue;
        }

        try
        {
            // Layer 2: Idempotent command handler
            await commandExecutor.ExecuteAsync((TOutput)cmd.Message);
        }
        catch (Exception ex)
        {
            // Execution failed - unmark for retry
            await persistence.MarkCommandUnprocessedAsync(cmd.WorkflowId, cmd.Position);

            logger.LogError(ex,
                "Command execution failed for {WorkflowId} position {Position}",
                cmd.WorkflowId,
                cmd.Position
            );

            throw;
        }
    }
}
```

**Mark Before Execute vs Execute Before Mark:**

| Approach | Pros | Cons |
|----------|------|------|
| **Mark → Execute** | No duplicate execution | If execute fails, need to unmark |
| **Execute → Mark** | Simpler (no unmark) | Possible duplicate execution |

**Recommendation:** Mark → Execute with idempotent handlers
- Best of both worlds
- Prevents duplicates at DB level
- Idempotency handles edge cases

---

## Summary

### Key Points

1. **Processed flag is our innovation** - not in original patterns
2. **Original patterns use checkpoints + message IDs** - more complex
3. **Our approach is simpler for relational DBs** - single source of truth
4. **Trade-off: stream mutability** - acceptable for SQL, not for EventStoreDB
5. **Decision: Keep it** - fits our architecture and requirements

### Design Principles

- ✅ **Explicit over implicit** - Command execution status is visible
- ✅ **Simplicity over purity** - Pragmatic approach for relational DBs
- ✅ **Self-contained** - No external tracking tables needed
- ✅ **Observability first** - Easy to see what's pending/executed
- ✅ **Concurrency safe** - Optimistic concurrency prevents race conditions

### When to Use Each Approach

**Use Processed Flag (Our Approach):**
- ✅ PostgreSQL, SQLite, SQL Server
- ✅ Need simple queries for pending commands
- ✅ Want single source of truth
- ✅ Prefer explicit execution tracking

**Use Checkpoint Pattern (Original):**
- ✅ EventStoreDB or immutable event stores
- ✅ Need pure event sourcing compliance
- ✅ Multiple processor types need different checkpoints
- ✅ Prefer append-only streams

**Use Execution Events (Hybrid):**
- ✅ Need immutability AND explicit tracking
- ✅ Audit trail of execution timestamps important
- ✅ Willing to accept larger stream size

---

## References

- **workflow.txt** - Yves Reynhout's original Workflow Pattern
- **RFC.txt** - Oskar Dudycz's Emmett workflow RFC
- **ARCHITECTURE_EVENT_STORAGE.md** - Emmett's storage implementation details
- **ARCHITECTURE.md** - Our unified stream architecture design
- **IMPLEMENTATION_STATE.md** - Current implementation status

---

**Last Updated:** 2025-11-23
**Decision Status:** ✅ Approved - Keep Processed Flag
**Review Date:** Before adding EventStoreDB support (if ever needed)
