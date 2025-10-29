# Workflow Reliability Patterns

## Problem 1: How do we know if command execution failed?

### Solutions:

1. **Synchronous Acknowledgment (Not Recommended for Distributed Systems)**
   - Wait for response from Pub/Sub
   - Doesn't tell us if the consumer processed it successfully
   - Blocks the workflow

2. **Event-Driven Confirmation (Recommended)**
   - Consumer publishes a result event after processing
   - Workflow receives the result event as the next input
   - Example:
     ```
     Send(GenerateSystemNumber)
       → Consumer processes it
       → Consumer publishes SystemNumberGenerated event
       → Workflow receives SystemNumberGenerated
       → Workflow continues
     ```

3. **Saga Pattern with Compensation**
   - Each step can be compensated/undone if later steps fail
   - Store compensation commands alongside forward commands
   - Example:
     ```
     Send(IssueTrafficFine) + CompensateWith(CancelTrafficFine)
     ```

4. **Timeout Pattern**
   - Schedule a timeout when sending command
   - If result doesn't arrive in time, trigger timeout handler
   - Example:
     ```
     Send(GenerateSystemNumber)
     Schedule(after: 5 minutes, TimeoutGenerateSystemNumber)
     ```

## Problem 2: How do we prevent duplicate execution (idempotency)?

### Solutions:

1. **Message Deduplication ID**
   - Include unique `message_id` in every message
   - Consumer tracks processed message IDs
   - If same message_id arrives again, skip processing

   ```csharp
   // In Processor
   envelope.Attributes.Add("message_id", messageId);

   // In Consumer
   if (await _processedMessages.Contains(messageId)) {
       return; // Already processed, skip
   }
   await ProcessMessage(message);
   await _processedMessages.Add(messageId);
   ```

2. **Idempotency Key**
   - Use business key instead of technical message_id
   - Example: `workflow_id + command_type + sequence_number`
   - Consumer checks: "Did I already generate system number for workflow XYZ?"

   ```csharp
   string idempotencyKey = $"{workflowId}:GenerateSystemNumber";
   if (await _cache.Exists(idempotencyKey)) {
       return await _cache.Get(idempotencyKey); // Return cached result
   }
   ```

3. **Natural Idempotency**
   - Design operations to be naturally idempotent
   - Example: "SET status = 'ISSUED'" (can be executed multiple times safely)
   - Example: "CREATE IF NOT EXISTS" instead of "CREATE"

4. **Version Numbers / Optimistic Locking**
   - Include version number with state updates
   - Only apply update if version matches
   - Prevents concurrent updates from causing issues

   ```csharp
   UPDATE workflows
   SET state = 'AwaitingManualCode', version = version + 1
   WHERE workflow_id = 'XYZ' AND version = 3
   ```

## Problem 3: How do we handle failures and retries?

### Solutions:

1. **Dead Letter Queue (DLQ)**
   - Failed messages go to DLQ after N retries
   - Manual investigation and replay
   - Pub/Sub supports this natively

2. **Exponential Backoff**
   - Retry with increasing delays: 1s, 2s, 4s, 8s, 16s...
   - Prevents overwhelming failed services
   - Pub/Sub supports this via subscription configuration

3. **Circuit Breaker**
   - After N consecutive failures, stop trying
   - Give the downstream service time to recover
   - Periodically test if service is back

4. **Outbox Pattern (Transaction + Publish)**
   - Write events to database in same transaction as state change
   - Separate process reads from outbox and publishes
   - Guarantees: if state changed, event will be published

   ```sql
   BEGIN TRANSACTION
     UPDATE workflows SET state = 'AwaitingSystemNumber'
     INSERT INTO outbox (workflow_id, event) VALUES ('XYZ', 'Sent(GenerateSystemNumber)')
   COMMIT

   -- Separate process:
   SELECT * FROM outbox WHERE published = false
   FOR EACH event:
     Publish to Pub/Sub
     Mark as published
   ```

## Recommended Architecture for This Workflow

```
┌─────────────────────────────────────────────────────────────┐
│ Workflow Instance (Event Sourced)                           │
│                                                              │
│ 1. Decide(input, state) → Commands                          │
│ 2. Persist Events (Commands → Events) in DB                 │
│ 3. Update State based on Events                             │
│ 4. Publish Commands to Pub/Sub with:                        │
│    - message_id (for deduplication)                         │
│    - workflow_id (for correlation)                          │
│    - idempotency_key (for safe retries)                     │
└─────────────────────────────────────────────────────────────┘
                         ↓
                    [Pub/Sub Topic]
                         ↓
┌─────────────────────────────────────────────────────────────┐
│ Command Handler (Consumer)                                   │
│                                                              │
│ 1. Check idempotency: Already processed this command?       │
│ 2. Execute command (e.g., generate system number)           │
│ 3. Store result + mark message as processed (same txn)      │
│ 4. Publish result event                                     │
└─────────────────────────────────────────────────────────────┘
                         ↓
                    [Pub/Sub Topic]
                         ↓
┌─────────────────────────────────────────────────────────────┐
│ Workflow Instance                                            │
│                                                              │
│ Receives result event → Continues workflow                  │
└─────────────────────────────────────────────────────────────┘
```

## Key Insights

1. **Commands are published with metadata** (message_id, workflow_id, idempotency_key)
2. **Consumers check idempotency** before processing
3. **Consumers publish result events** to confirm success
4. **Workflow continues** when it receives the result event
5. **Failures are handled** via retries, DLQ, and timeouts
6. **Event sourcing ensures** we can always replay and see what happened

## Code Example

See `WorkflowOrchestratorWithReliability.cs` for implementation.
