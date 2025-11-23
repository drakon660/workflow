# Emmett Event and Command Storage Architecture

## Table of Contents
- [Overview](#overview)
- [Storage Schema](#storage-schema)
- [Command vs Event Handling](#command-vs-event-handling)
- [Exactly-Once Execution Guarantees](#exactly-once-execution-guarantees)
- [Failure Handling](#failure-handling)
- [Storage Backends](#storage-backends)

---

## Overview

Emmett follows the **Event Sourcing** pattern where:
- **Commands** represent intent to perform operations (ephemeral, not stored)
- **Events** represent facts that have occurred (persisted permanently)
- **Aggregate state** is derived by replaying events

### Key Principle
Commands are NOT tracked directly. Instead, the presence of events in the event store IS the proof that commands were successfully executed.

---

## Storage Schema

### PostgreSQL/SQLite: Table Structure

#### Main Table: `emt_messages`
Stores both events AND commands (though commands are typically not persisted).

**Location:** `src/packages/emmett-postgresql/src/eventStore/schema/tables.ts:8`

```sql
CREATE TABLE IF NOT EXISTS emt_messages(
    stream_id              TEXT                      NOT NULL,
    stream_position        BIGINT                    NOT NULL,
    partition              TEXT                      NOT NULL DEFAULT 'global',
    message_kind           CHAR(1)                   NOT NULL DEFAULT 'E',  -- 'E' = Event, 'C' = Command
    message_data           JSONB                     NOT NULL,              -- Event/command payload
    message_metadata       JSONB                     NOT NULL,              -- Metadata (messageId, positions, etc.)
    message_schema_version TEXT                      NOT NULL,
    message_type           TEXT                      NOT NULL,              -- Type name (e.g., 'ProductItemAdded')
    message_id             TEXT                      NOT NULL,              -- UUID
    is_archived            BOOLEAN                   NOT NULL DEFAULT FALSE,
    global_position        BIGINT                    DEFAULT nextval('emt_global_message_position'),
    transaction_id         XID8                      NOT NULL,              -- PostgreSQL transaction ID
    created                TIMESTAMPTZ               NOT NULL DEFAULT now(),
    PRIMARY KEY (stream_id, stream_position, partition, is_archived)
) PARTITION BY LIST (partition);
```

**Key Columns:**
- `message_data` (JSONB) - The actual event/command payload as JSON
- `message_metadata` (JSONB) - Metadata like messageId, streamName, positions
- `message_type` (TEXT) - Type discriminator (e.g., 'ProductItemAddedToShoppingCart')
- `message_kind` (CHAR) - Differentiates Events ('E') vs Commands ('C')
- `message_id` (TEXT) - Unique identifier (UUID)
- `stream_position` (BIGINT) - Position within the specific stream (per aggregate)
- `global_position` (BIGINT) - Auto-incrementing global sequence across all streams
- `transaction_id` (XID8) - PostgreSQL transaction identifier for debugging

#### Supporting Table: `emt_streams`
Stores stream metadata and current version.

```sql
CREATE TABLE IF NOT EXISTS emt_streams(
    stream_id         TEXT                      NOT NULL,
    stream_position   BIGINT                    NOT NULL,
    partition         TEXT                      NOT NULL DEFAULT 'global',
    stream_type       TEXT                      NOT NULL,
    stream_metadata   JSONB                     NOT NULL,
    is_archived       BOOLEAN                   NOT NULL DEFAULT FALSE,
    PRIMARY KEY (stream_id, stream_position, partition, is_archived),
    UNIQUE (stream_id, partition, is_archived)
) PARTITION BY LIST (partition);
```

#### Supporting Table: `emt_subscriptions`
Stores consumer checkpoints for exactly-once processing.

```sql
CREATE TABLE IF NOT EXISTS emt_subscriptions(
    subscription_id                 TEXT                   NOT NULL,
    version                         INT                    NOT NULL DEFAULT 1,
    partition                       TEXT                   NOT NULL DEFAULT 'global',
    last_processed_position         BIGINT                 NOT NULL,
    last_processed_transaction_id   XID8                   NOT NULL,
    PRIMARY KEY (subscription_id, partition, version)
) PARTITION BY LIST (partition);
```

### MongoDB: Document Structure

**Location:** `src/packages/emmett-mongodb/src/eventStore/mongoDBEventStore.ts:142`

MongoDB uses a **document-per-stream** approach with events stored as arrays.

**Collection naming:** `emt:{streamType}` (e.g., `emt:shopping_cart`)

**Document Schema:**
```typescript
{
  streamName: "shopping_cart:123",
  messages: [
    {
      type: "ProductItemAddedToShoppingCart",
      data: { shoppingCartId: "123", productItem: {...} },
      metadata: {
        messageId: "uuid-here",
        streamName: "shopping_cart:123",
        streamPosition: 1n,
        // ... additional metadata
      }
    },
    // ... more events
  ],
  metadata: {
    streamId: "123",
    streamType: "shopping_cart",
    streamPosition: 5n,
    createdAt: Date,
    updatedAt: Date
  },
  projections: {
    // Inline read models
  }
}
```

**Key Differences:**
- Events stored as **array within single document** per stream
- Uses MongoDB's `$push` operator to append new events
- Supports inline projections within the same document
- Unique index on `streamName`

---

## Command vs Event Handling

### Command Flow

**Location:** `src/packages/emmett/src/commandHandling/handleCommand.ts:94-204`

```typescript
// 1. Read existing events and build current state
const aggregationResult = await eventStore.aggregateStream(streamName, {
  evolve,
  initialState,
  read: { expectedStreamVersion }
});

let state = aggregationResult.state;

// 2. Execute business logic - Command becomes Events
for (const handler of handlers) {
  const result = await handler(state);
  const newEvents = Array.isArray(result) ? result : [result];

  if (newEvents.length > 0) {
    state = newEvents.reduce(evolve, state);
  }

  eventsToAppend = [...eventsToAppend, ...newEvents];
}

// 3. If no events produced, return early (no changes)
if (eventsToAppend.length === 0) {
  return { /* no changes */ };
}

// 4. Persist events to event store
const appendResult = await eventStore.appendToStream(
  streamName,
  eventsToAppend,
  { expectedStreamVersion }
);

// 5. Return proof of execution
return {
  nextExpectedStreamVersion,
  newEvents: eventsToAppend,
  newState: state,
  createdNewStream
};
```

### Business Logic Example

**Location:** `src/docs/snippets/gettingStarted/businessLogic.ts:26`

```typescript
// Command IN (not stored)
type AddProductItemToShoppingCart = Command<{
  shoppingCartId: string;
  productItem: ProductItem;
}>;

// Event OUT (will be stored)
type ProductItemAddedToShoppingCart = Event<{
  shoppingCartId: string;
  productItem: ProductItem;
  addedAt: Date;
}>;

export const addProductItem = (
  command: AddProductItemToShoppingCart,
  state: ShoppingCart,
): ProductItemAddedToShoppingCart => {

  if (state.status === 'Closed')
    throw new IllegalStateError('Shopping Cart already closed');

  const { data: { shoppingCartId, productItem }, metadata } = command;

  return {
    type: 'ProductItemAddedToShoppingCart',
    data: { shoppingCartId, productItem, addedAt: metadata?.now ?? new Date() },
  };
};
```

### Event Serialization

**PostgreSQL/SQLite:** `src/packages/emmett-postgresql/src/eventStore/schema/appendToStream.ts:156`

```typescript
const messagesToAppend: RecordedMessage[] = messages.map((e) => ({
  ...e,
  kind: e.kind ?? 'Event',
  metadata: {
    messageId: uuid(),  // Unique ID generated here
    ...e.metadata,
  },
}));

// Inserted as:
// message_data: JSONParser.stringify(e.data)
// message_metadata: JSONParser.stringify(metadata)
// message_type: e.type
// message_kind: e.kind === 'Event' ? 'E' : 'C'
```

### Event Metadata Enrichment

**Location:** `src/packages/emmett/src/eventStore/inMemoryEventStore.ts:156-224`

```typescript
const newEvents: ReadEvent<EventType>[] = events.map((event, index) => {
  const metadata: ReadEventMetadataWithGlobalPosition = {
    streamName,
    messageId: uuid(),
    streamPosition: BigInt(currentEvents.length + index + 1),
    globalPosition: BigInt(getAllEventsCount() + index + 1),
  };
  return {
    ...event,
    kind: event.kind ?? 'Event',
    metadata: { ...event.metadata, ...metadata },
  };
});
```

---

## Exactly-Once Execution Guarantees

### 1. Optimistic Concurrency Control (OCC)

**Location:** `src/packages/emmett-postgresql/src/eventStore/schema/appendToStream.ts:64-73`

PostgreSQL uses atomic UPDATE with version checking:

```sql
UPDATE emt_streams
SET stream_position = next_stream_position
WHERE stream_id = 'shopping_cart:123'
  AND stream_position = 5  -- Expected version (OCC check)
  AND partition = 'global'
  AND is_archived = FALSE;

GET DIAGNOSTICS updated_rows = ROW_COUNT;

IF updated_rows = 0 THEN
    -- Concurrency conflict detected
    RETURN QUERY SELECT FALSE, NULL::bigint, NULL::bigint[], NULL::xid8;
    RETURN;
END IF;
```

**How it prevents duplicates:**

```
Time  │ Thread A                    │ Thread B
──────┼─────────────────────────────┼──────────────────────────
T1    │ Read stream version: 5      │
T2    │                             │ Read stream version: 5
T3    │ Execute business logic      │
T4    │                             │ Execute business logic
T5    │ Write with version 5 ✓      │
      │ → Success (now version 6)   │
T6    │                             │ Write with version 5 ✗
      │                             │ → FAILS (current is 6)
T7    │                             │ AUTO RETRY: Read version 6
T8    │                             │ Re-run business logic
T9    │                             │ Write with version 6 ✓
```

### 2. Automatic Retry on Version Conflicts

**Location:** `src/packages/emmett/src/commandHandling/handleCommand.ts:16-22`

```typescript
export const CommandHandlerStreamVersionConflictRetryOptions: AsyncRetryOptions = {
  retries: 3,              // Retry up to 3 times
  minTimeout: 100,         // Start with 100ms delay
  factor: 1.5,             // Exponential backoff (100ms, 150ms, 225ms)
  shouldRetryError: isExpectedVersionConflictError,  // Only retry version conflicts
};
```

**Retry Implementation:** `src/packages/emmett/src/utils/retry.ts:5`

```typescript
export const asyncRetry = async <T>(
  fn: () => Promise<T>,
  opts?: AsyncRetryOptions<T>,
): Promise<T> => {
  if (opts === undefined || opts.retries === 0) return fn();

  return retry(
    async (bail) => {
      try {
        const result = await fn();
        if (opts?.shouldRetryResult && opts.shouldRetryResult(result)) {
          throw new EmmettError(`Retrying because of result`);
        }
        return result;
      } catch (error) {
        if (opts?.shouldRetryError && !opts.shouldRetryError(error)) {
          bail(error);  // Don't retry - bail immediately
          return undefined as unknown as T;
        }
        throw error;  // Retry
      }
    },
    opts ?? { retries: 0 },
  );
};
```

### 3. Unique Constraint Protection

**SQLite:** `src/packages/emmett-sqlite/src/eventStore/schema/tables.ts:8`

```sql
CREATE TABLE IF NOT EXISTS emt_messages(
    stream_id       TEXT   NOT NULL,
    stream_position BIGINT NOT NULL,
    partition       TEXT   NOT NULL DEFAULT 'global',
    ...,
    UNIQUE (stream_id, stream_position, partition, is_archived)
);
```

Even if OCC somehow fails, the database **physically prevents** duplicate positions:
- Attempting to insert `(cart:123, position 6)` when it already exists → **CONSTRAINT ERROR**

### 4. Transactional Checkpointing

**Location:** `src/packages/emmett-sqlite/src/eventStore/consumers/sqliteProcessor.ts:107-147`

For event processors (subscribers), Emmett uses **transactional checkpointing**:

```typescript
return connection.withTransaction(async () => {
  let lastProcessedPosition: bigint | null = null;

  for (const message of messages) {
    // 1. Process the event (e.g., update read model)
    const result = await eachMessage(message, { connection, fileName });

    const newPosition: bigint | null = getCheckpoint(message);

    // 2. Store checkpoint IN SAME TRANSACTION
    await storeProcessorCheckpoint(connection, {
      processorId: options.processorId,
      version: options.version,
      lastProcessedPosition,  // Previous position for OCC
      newPosition,            // New position to store
      partition: options.partition,
    });

    lastProcessedPosition = message.metadata.globalPosition;

    if (result && result.type === 'STOP') {
      isActive = false;
      break;
    }
  }
  // ← COMMIT: Both processing AND checkpoint are atomic
});
```

**Checkpoint Storage with OCC:** `src/packages/emmett-postgresql/src/eventStore/schema/storeProcessorCheckpoint.ts:1-60`

```sql
CREATE OR REPLACE FUNCTION store_subscription_checkpoint(
  p_subscription_id VARCHAR(100),
  p_version BIGINT,
  p_position BIGINT,
  p_check_position BIGINT,  -- Last known position (for optimistic locking)
  p_transaction_id xid8,
  p_partition TEXT DEFAULT 'default_partition'
) RETURNS INT AS $$
DECLARE
  current_position BIGINT;
BEGIN
  -- Try to update if the position matches p_check_position
  UPDATE emt_subscriptions
  SET
    last_processed_position = p_position,
    last_processed_transaction_id = p_transaction_id
  WHERE subscription_id = p_subscription_id
    AND last_processed_position = p_check_position  -- OCC for checkpoint
    AND partition = p_partition;

  IF FOUND THEN
      RETURN 1;  -- Successfully updated
  END IF;

  -- Retrieve the current position
  SELECT last_processed_position INTO current_position
  FROM emt_subscriptions
  WHERE subscription_id = p_subscription_id AND partition = p_partition;

  -- Return appropriate codes based on current position
  IF current_position = p_position THEN
      RETURN 0;  -- Idempotent: position already set (safe duplicate)
  ELSIF current_position > p_check_position THEN
      RETURN 2;  -- Failure: current position is greater (message skipped)
  ELSE
      RETURN 2;  -- Default failure case
  END IF;
END;
$$ LANGUAGE plpgsql;
```

**Return codes:**
- `1` = Success (checkpoint updated)
- `0` = Idempotent (already at this position - safe to ignore)
- `2` = Conflict (manual intervention needed)

---

## Failure Handling

### Server Crash Scenarios

#### Scenario A: Crash BEFORE Transaction Commits

```typescript
pool.withTransaction(async (tx) => {
  const events = businessLogic(command);
  await appendToStream(tx, streamName, events);
  // 💥 SERVER CRASHES HERE (before COMMIT)
});
```

**Result:**
- ✅ Transaction automatically **ROLLS BACK**
- ✅ **No events stored** in database
- ✅ Client receives connection error
- ✅ **Safe to retry** - command will execute again from scratch

#### Scenario B: Crash AFTER Transaction Commits

```typescript
const result = await appendToStream(pool, streamName, events);
// ✅ TRANSACTION COMMITTED (events stored in database)

// 💥 SERVER CRASHES HERE (after COMMIT, before response sent to client)
```

**Result:**
- ✅ Events **ARE stored** in database
- ❌ Client receives connection error (doesn't know if it succeeded)
- ✅ **Safe to retry** - OCC will prevent duplicate execution

**Why safe to retry:**

```typescript
// Client retries the same command
const result = await handleCommand(eventStore, 'cart:123', addItem, {
  expectedStreamVersion: 5  // Client still thinks version is 5
});

// But database now has version 6 (from the committed-but-unacknowledged write)
// OCC check fails: expected 5, current 6
// ❌ Returns ExpectedVersionConflictError

// Client can then:
// 1. Read current state (version 6)
// 2. Check if the item was already added
// 3. Decide whether to retry with new version or consider it done
```

#### Scenario C: Event Processor Crash

```
Crash during processing:
├─ BEGIN TRANSACTION
├─ UPDATE read_model SET qty = qty - 1
├─ 💥 CRASH
└─ ROLLBACK (checkpoint not updated)
   → Event will be reprocessed ✓

Crash after checkpoint:
├─ BEGIN TRANSACTION
├─ UPDATE read_model SET qty = qty - 1
├─ UPDATE emt_subscriptions SET position = 42
├─ COMMIT ✓
└─ 💥 CRASH (after commit)
   → Both processing and checkpoint saved ✓
   → Event NOT reprocessed ✓
```

### Transaction Handling

**PostgreSQL:** `src/packages/emmett-postgresql/src/eventStore/schema/appendToStream.ts:135-222`

```typescript
export const appendToStream = (
  pool: NodePostgresPool,
  streamName: string,
  streamType: string,
  messages: Message[],
  options?: AppendToStreamOptions,
): Promise<AppendToStreamResult> =>
  pool.withTransaction<AppendToStreamResult>(async (transaction) => {
    const { execute } = transaction;

    try {
      // 1. Append events with OCC
      const { success, next_stream_position, global_positions, transaction_id } =
        await appendEventsRaw(execute, streamName, streamType, messagesToAppend, {
          expectedStreamVersion,
        });

      if (!success || next_stream_position === null) {
        return { success: false, result: { success: false } };
      }

      // 2. Run before-commit hooks (e.g., inline projections)
      if (options?.beforeCommitHook)
        await options.beforeCommitHook(messagesToAppend, { transaction });

      return {
        success: true,
        result: {
          nextExpectedStreamVersion: next_stream_position,
          lastEventGlobalPosition: global_positions[global_positions.length - 1],
          createdNewStream: next_stream_position === 1n,
        }
      };
    } catch (error) {
      if (!isOptimisticConcurrencyError(error)) throw error;
      return { success: false, result: { success: false } };
    }
  });
  // ← COMMIT happens here: either ALL succeed or ALL rollback
```

**SQLite:** `src/packages/emmett-sqlite/src/connection/sqliteConnection.ts:91-109`

```typescript
withTransaction: async <T>(fn: () => Promise<T>) => {
  try {
    if (transactionNesting++ == 0) {
      await beginTransaction(db);  // BEGIN IMMEDIATE TRANSACTION
    }
    const result = await fn();

    if (transactionNesting === 1)
      await commitTransaction(db);  // COMMIT
    transactionNesting--;

    return result;
  } catch (err) {
    console.log(err);
    if (--transactionNesting === 0)
      await rollbackTransaction(db);  // ROLLBACK on error
    throw err;
  }
}
```

**Transaction isolation:**
```typescript
const beginTransaction = (db: sqlite3.Database) =>
  new Promise<void>((resolve, reject) => {
    db.run('BEGIN IMMEDIATE TRANSACTION', (err: Error | null) => {
      // BEGIN IMMEDIATE prevents reader-writer conflicts
    });
  });
```

### Inbox/Outbox Patterns

#### Before-Commit Hooks (Transactional Inbox)

**Location:** `src/packages/emmett-postgresql/src/eventStore/postgreSQLEventStore.ts:183-197`

```typescript
const beforeCommitHook: AppendToStreamBeforeCommitHook | undefined =
  inlineProjections.length > 0
    ? (events, { transaction }) =>
        handleProjections({
          projections: inlineProjections,
          connection: { connectionString, pool, transaction },
          events: events as ReadEvent<Event, PostgresReadEventMetadata>[],
        })
    : undefined;

const appendResult = await appendToStream(pool, streamName, streamType, events, {
  ...options,
  beforeCommitHook,  // Executes in same transaction
});
```

**Guarantee:** Exactly-once (rolls back if hook fails)

#### After-Commit Hooks (Best-Effort Outbox)

**Location:** `src/packages/emmett/src/eventStore/eventStore.ts:216-238`

```typescript
export type DefaultEventStoreOptions<Store extends EventStore> = {
  hooks?: {
    /**
     * This hook will be called **AFTER** events were stored in the event store.
     *
     * **WARNINGS:**
     * 1. It will be called **EXACTLY ONCE** if append succeeded.
     * 2. If the hook fails, its append **will still silently succeed**.
     * 3. When process crashes after events were committed, but before the hook was called,
     *    delivery won't be retried. That can lead to state inconsistencies.
     * 4. In the case of high concurrent traffic, **race conditions may cause ordering issues**.
     */
    onAfterCommit?: AfterEventStoreCommitHandler<Store>;
  };
};
```

**Implementation:** `src/packages/emmett/src/eventStore/afterCommit/afterEventStoreCommitHandler.ts:1`

```typescript
export async function tryPublishMessagesAfterCommit<Store extends EventStore>(
  messages: ReadEvent<Event, EventStoreReadEventMetadata<Store>>[],
  options: TryPublishMessagesAfterCommitOptions<Store> | undefined,
): Promise<boolean> {
  if (options?.onAfterCommit === undefined) return false;

  try {
    await options?.onAfterCommit(messages, context);
    return true;
  } catch (error) {
    console.error(`Error in on after commit hook`, error);
    return false;  // Swallows error - events are already committed
  }
}
```

**Guarantee:** At-most-once (events stored, but hook might not execute)

---

## Storage Backends

### PostgreSQL

**Event Store:** `src/packages/emmett-postgresql/src/eventStore/postgreSQLEventStore.ts`

**Features:**
- Partitioning support (multi-tenancy)
- Transaction ID tracking (`XID8`)
- Global position sequence
- Stored procedures for atomic operations
- Session support for multi-operation transactions
- Both inline (before-commit) and async (after-commit) hooks

**Append Operation:**
```typescript
const appendResult = await pool.withTransaction(async (tx) => {
  // Atomic: version check + message insert + hooks
  return await appendEventsRaw(tx, streamName, streamType, events, {
    expectedStreamVersion,
  });
});
```

### SQLite

**Event Store:** `src/packages/emmett-sqlite/src/eventStore/sqliteEventStore.ts`

**Features:**
- Lightweight, file-based storage
- Nested transaction support
- `BEGIN IMMEDIATE` for write locking
- Integer auto-increment for global position
- No partitioning support
- Similar hook support as PostgreSQL

**Differences from PostgreSQL:**
- `global_position` is `INTEGER PRIMARY KEY` (auto-increment)
- No `transaction_id` column
- Simpler schema (no partitioning)
- Manual JSON parsing required

### MongoDB

**Event Store:** `src/packages/emmett-mongodb/src/eventStore/mongoDBEventStore.ts`

**Features:**
- Document-per-stream approach
- Events stored as arrays
- Inline projections as subdocuments
- Uses `$push` for appending events
- Optimistic concurrency via document version

**Append Operation:**
```typescript
const updates: UpdateFilter<EventStream> = {
  $push: { messages: { $each: eventsToAppend } },
  $set: { 'metadata.streamPosition': streamOffset, 'metadata.updatedAt': new Date() }
};

await collection.updateOne(
  { streamName, 'metadata.streamPosition': expectedVersion },  // OCC
  updates
);
```

### In-Memory

**Event Store:** `src/packages/emmett/src/eventStore/inMemoryEventStore.ts`

**Features:**
- For testing and development
- No persistence
- Simple Map-based storage
- Supports projections and subscriptions
- Same interface as persistent stores

**Use case:** Unit tests, prototyping, demos

---

## Summary Table: Exactly-Once Guarantees

| Failure Scenario | What Happens | Safe to Retry? | Mechanism |
|------------------|--------------|----------------|-----------|
| **Crash before COMMIT** | Transaction rolls back, no events stored | ✅ Yes - command will execute | Database transaction |
| **Crash after COMMIT, before response** | Events stored, client doesn't know | ✅ Yes - OCC prevents duplicate | Optimistic concurrency |
| **Concurrent commands** | OCC detects conflict, one succeeds, others retry | ✅ Auto-retry (3x) | Stream version check |
| **Network timeout** | Unknown if committed | ✅ Yes - OCC prevents duplicate | Optimistic concurrency |
| **Duplicate request** | Version check fails | ✅ Yes - idempotent | Stream version check |
| **Event processor crash** | Transaction rollback, checkpoint not updated | ✅ Yes - event reprocessed | Transactional checkpoint |
| **After-commit hook fails** | Events stored, hook not executed | ⚠️ Events persist, hook lost | Best-effort delivery |

---

## Client Best Practices

### 1. Use Expected Versions for Critical Operations

```typescript
// Read current state first
const { state, currentStreamVersion } = await eventStore.aggregateStream('cart:123');

// Use version for optimistic locking
const result = await handleCommand(
  eventStore,
  'cart:123',
  addItem,
  { expectedStreamVersion: currentStreamVersion }  // ← Prevents race conditions
);
```

### 2. Handle Version Conflicts Gracefully

```typescript
try {
  await handleCommand(eventStore, id, command);
} catch (error) {
  if (isExpectedVersionConflictError(error)) {
    // Command handler already retried 3 times automatically

    // Option A: Read fresh state and check if command already succeeded
    const { state } = await eventStore.aggregateStream(id);
    if (state.hasItem(productId)) {
      return; // Already added - idempotent success
    }

    // Option B: Retry with fresh version
    const { currentStreamVersion } = await eventStore.aggregateStream(id);
    await handleCommand(eventStore, id, command, {
      expectedStreamVersion: currentStreamVersion
    });
  }
}
```

### 3. Design Idempotent Commands

```typescript
export const addProductItem = (
  command: AddProductItemToShoppingCart,
  state: ShoppingCart,
): ProductItemAddedToShoppingCart | [] => {

  // Check if item already exists
  if (state.items.some(item => item.productId === command.data.productItem.productId)) {
    return [];  // ← Idempotent: no events produced if already added
  }

  return {
    type: 'ProductItemAddedToShoppingCart',
    data: { ...command.data }
  };
};
```

### 4. Use Transactional Projections for Critical Read Models

```typescript
const eventStore = getPostgreSQLEventStore(connectionString, {
  projections: {
    inline: [inventoryProjection],  // ← Executes in same transaction (exactly-once)
  },
});
```

### 5. Handle After-Commit Hook Failures

```typescript
const eventStore = getPostgreSQLEventStore(connectionString, {
  hooks: {
    onAfterCommit: async (events) => {
      // This might fail without rollback!
      await publishToMessageBus(events);
    },
  },
});

// For critical integrations, use polling instead:
// 1. Store events (transactional)
// 2. Background process polls for unpublished events
// 3. Publish and mark as published (atomic)
```

---

## Key Files Reference

| Component | Location |
|-----------|----------|
| PostgreSQL Schema | `src/packages/emmett-postgresql/src/eventStore/schema/tables.ts:8` |
| PostgreSQL Append | `src/packages/emmett-postgresql/src/eventStore/schema/appendToStream.ts:64` |
| SQLite Schema | `src/packages/emmett-sqlite/src/eventStore/schema/tables.ts:8` |
| SQLite Append | `src/packages/emmett-sqlite/src/eventStore/schema/appendToStream.ts:182` |
| MongoDB Event Store | `src/packages/emmett-mongodb/src/eventStore/mongoDBEventStore.ts:142` |
| Command Handler | `src/packages/emmett/src/commandHandling/handleCommand.ts:94` |
| Retry Logic | `src/packages/emmett/src/utils/retry.ts:5` |
| Expected Version | `src/packages/emmett/src/eventStore/expectedVersion.ts:1` |
| Checkpoint Storage | `src/packages/emmett-postgresql/src/eventStore/schema/storeProcessorCheckpoint.ts:1` |
| SQLite Processor | `src/packages/emmett-sqlite/src/eventStore/consumers/sqliteProcessor.ts:107` |
| Transaction Handling | `src/packages/emmett-sqlite/src/connection/sqliteConnection.ts:91` |
| After-Commit Hooks | `src/packages/emmett/src/eventStore/afterCommit/afterEventStoreCommitHandler.ts:1` |

---

## Glossary

- **Command**: Intent to perform an operation (ephemeral, not stored)
- **Event**: Fact that an operation occurred (persisted permanently)
- **Stream**: Ordered sequence of events for a single aggregate
- **Stream Position**: Position of an event within its stream (1, 2, 3, ...)
- **Global Position**: Position of an event across all streams (monotonically increasing)
- **Expected Version**: OCC mechanism to prevent concurrent modifications
- **Aggregate**: Domain entity whose state is derived from event replay
- **Projection**: Read model derived from events
- **Inline Projection**: Projection updated in same transaction as event append (exactly-once)
- **Async Projection**: Projection updated after event commit (at-least-once)
- **Checkpoint**: Last processed event position for a consumer/processor
- **Idempotency**: Property where executing operation multiple times has same effect as once
- **OCC (Optimistic Concurrency Control)**: Conflict detection via version checking
