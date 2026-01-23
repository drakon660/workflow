# Workflow Patterns Guide

**Last Updated:** 2025-11-21

---

## Table of Contents

1. [Overview](#overview)
2. [Reliability Patterns](#reliability-patterns)
3. [Reply Command Patterns](#reply-command-patterns)
4. [Decision Matrices](#decision-matrices)
5. [Code Examples](#code-examples)

---

## Overview

This guide covers two critical pattern areas for workflow implementation:

1. **Reliability Patterns** - How to handle failures, retries, idempotency, and ensure reliable command execution
2. **Reply Command Patterns** - When and how to use Reply commands for async workflow-to-workflow communication

These patterns ensure workflows are resilient, predictable, and properly coordinated in distributed systems.

---

## Reliability Patterns

### Problem 1: How Do We Know If Command Execution Failed?

#### Solutions

##### 1. Synchronous Acknowledgment (Not Recommended for Distributed Systems)
- Wait for response from Pub/Sub
- Doesn't tell us if the consumer processed it successfully
- Blocks the workflow

##### 2. Event-Driven Confirmation (Recommended)
**Pattern:** Consumer publishes a result event after processing

**Flow:**
```
Send(GenerateSystemNumber)
  → Consumer processes it
  → Consumer publishes SystemNumberGenerated event
  → Workflow receives SystemNumberGenerated
  → Workflow continues
```

**Implementation:**
```csharp
// Workflow sends command
(RequestSystemNumber, New) => [
    Send(new GenerateSystemNumber(orderId)),
    Schedule(new TimeoutSystemNumber(), after: TimeSpan.FromMinutes(5))
],

// Workflow receives confirmation
(SystemNumberGenerated result, WaitingForSystemNumber) => [
    Send(new ProcessOrder(result.SystemNumber))
],

// Workflow handles timeout
(TimeoutSystemNumber, WaitingForSystemNumber) => [
    Send(new RejectOrder("System number generation timed out"))
]
```

**Benefits:**
- ✅ Non-blocking
- ✅ Clear success/failure paths
- ✅ Built-in timeout handling
- ✅ Audit trail of results

##### 3. Saga Pattern with Compensation
**Pattern:** Each step can be compensated/undone if later steps fail

**Flow:**
```
Send(IssueTrafficFine) + CompensateWith(CancelTrafficFine)
```

**Implementation:**
```csharp
public record SagaStep<TCommand, TCompensation>(
    TCommand Forward,
    TCompensation Compensation
);

// Store compensation commands alongside forward commands
(ProcessPayment payment, New) => [
    Send(new ChargeCustomer(payment.Amount)),
    StoreCompensation(new RefundCustomer(payment.Amount))
]
```

**Benefits:**
- ✅ Handles partial failures gracefully
- ✅ Can undo completed steps
- ✅ Maintains consistency across services

##### 4. Timeout Pattern
**Pattern:** Schedule a timeout when sending command

**Flow:**
```
Send(GenerateSystemNumber)
Schedule(after: 5 minutes, TimeoutGenerateSystemNumber)
```

**Implementation:**
```csharp
(PlaceOrder order, New) => [
    Send(new GenerateSystemNumber(order.OrderId)),
    Schedule(new TimeoutSystemNumber(), after: TimeSpan.FromMinutes(5))
],

(SystemNumberGenerated result, WaitingForSystemNumber) => [
    // Success - continue processing
    Send(new ProcessOrder(result.SystemNumber))
],

(TimeoutSystemNumber, WaitingForSystemNumber) => [
    // Timeout - handle failure
    Send(new NotifyOrderFailed("System number generation timed out"))
]
```

**Benefits:**
- ✅ Prevents workflows from waiting indefinitely
- ✅ Clear failure handling
- ✅ User notification of issues

---

### Problem 2: How Do We Prevent Duplicate Execution (Idempotency)?

#### Solutions

##### 1. Message Deduplication ID
**Pattern:** Include unique `message_id` in every message

**Implementation:**
```csharp
// In Processor
var messageId = Guid.NewGuid().ToString();
envelope.Attributes.Add("message_id", messageId);
await publisher.PublishAsync(topic, envelope);

// In Consumer
public async Task ProcessMessage(Message message)
{
    var messageId = message.Attributes["message_id"];

    if (await _processedMessages.Contains(messageId)) {
        return; // Already processed, skip
    }

    await ExecuteCommand(message.Data);
    await _processedMessages.Add(messageId);
}
```

**Benefits:**
- ✅ Works across any command type
- ✅ Simple to implement
- ✅ Infrastructure-level solution

##### 2. Idempotency Key
**Pattern:** Use business key instead of technical message_id

**Implementation:**
```csharp
// Generate idempotency key
string idempotencyKey = $"{workflowId}:GenerateSystemNumber";

// Check before executing
if (await _cache.Exists(idempotencyKey)) {
    return await _cache.Get(idempotencyKey); // Return cached result
}

// Execute and cache result
var result = await GenerateSystemNumber();
await _cache.Set(idempotencyKey, result, expiry: TimeSpan.FromHours(24));
return result;
```

**Benefits:**
- ✅ Business-level semantics
- ✅ Can return cached results
- ✅ More meaningful than message IDs

##### 3. Natural Idempotency
**Pattern:** Design operations to be naturally idempotent

**Examples:**
```sql
-- SET operations (naturally idempotent)
UPDATE orders SET status = 'ISSUED' WHERE order_id = 'XYZ';

-- CREATE IF NOT EXISTS (naturally idempotent)
CREATE TABLE IF NOT EXISTS orders (...);

-- UPSERT operations (naturally idempotent)
INSERT INTO orders (id, status) VALUES ('XYZ', 'ISSUED')
ON CONFLICT (id) DO UPDATE SET status = 'ISSUED';
```

**Benefits:**
- ✅ No additional infrastructure needed
- ✅ Simplest solution
- ✅ Best performance

##### 4. Version Numbers / Optimistic Locking
**Pattern:** Include version number with state updates

**Implementation:**
```sql
UPDATE workflows
SET state = 'AwaitingManualCode', version = version + 1
WHERE workflow_id = 'XYZ' AND version = 3;

-- If affected rows = 0, version conflict (already updated)
```

**Benefits:**
- ✅ Prevents concurrent updates
- ✅ Detects conflicts
- ✅ Common pattern in databases

---

### Problem 3: How Do We Handle Failures and Retries?

#### Solutions

##### 1. Dead Letter Queue (DLQ)
**Pattern:** Failed messages go to DLQ after N retries

**Implementation:**
```csharp
// Pub/Sub configuration
subscription.DeadLetterPolicy = new DeadLetterPolicy
{
    DeadLetterTopic = "projects/my-project/topics/my-dlq",
    MaxDeliveryAttempts = 5
};
```

**Manual Investigation:**
```csharp
// Read from DLQ
var dlqMessages = await _dlqSubscription.PullAsync(100);

foreach (var message in dlqMessages)
{
    // Investigate failure
    // Fix issue
    // Republish to original topic or discard
}
```

**Benefits:**
- ✅ Prevents infinite retries
- ✅ Preserves failed messages for investigation
- ✅ Supported natively by message brokers

##### 2. Exponential Backoff
**Pattern:** Retry with increasing delays

**Implementation:**
```csharp
// Pub/Sub subscription configuration
subscription.RetryPolicy = new RetryPolicy
{
    MinimumBackoff = TimeSpan.FromSeconds(1),
    MaximumBackoff = TimeSpan.FromMinutes(10),
    BackoffMultiplier = 2.0
};

// Results in delays: 1s, 2s, 4s, 8s, 16s, 32s, 64s, ...
```

**Custom implementation:**
```csharp
public async Task<T> ExecuteWithRetry<T>(Func<Task<T>> operation, int maxRetries = 5)
{
    for (int attempt = 0; attempt < maxRetries; attempt++)
    {
        try
        {
            return await operation();
        }
        catch (Exception ex) when (attempt < maxRetries - 1)
        {
            var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
            await Task.Delay(delay);
        }
    }

    throw new Exception($"Failed after {maxRetries} attempts");
}
```

**Benefits:**
- ✅ Gives downstream services time to recover
- ✅ Reduces load during outages
- ✅ Often succeeds on retry

##### 3. Circuit Breaker
**Pattern:** After N consecutive failures, stop trying temporarily

**Implementation:**
```csharp
public class CircuitBreaker
{
    private int _failureCount = 0;
    private DateTime? _openedAt = null;
    private readonly int _threshold = 5;
    private readonly TimeSpan _timeout = TimeSpan.FromMinutes(1);

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation)
    {
        // Check if circuit is open
        if (_openedAt.HasValue)
        {
            if (DateTime.UtcNow - _openedAt.Value < _timeout)
            {
                throw new CircuitBreakerOpenException("Circuit breaker is open");
            }

            // Try to close circuit (half-open state)
            _openedAt = null;
            _failureCount = 0;
        }

        try
        {
            var result = await operation();
            _failureCount = 0; // Reset on success
            return result;
        }
        catch (Exception)
        {
            _failureCount++;

            if (_failureCount >= _threshold)
            {
                _openedAt = DateTime.UtcNow;
            }

            throw;
        }
    }
}
```

**Benefits:**
- ✅ Prevents cascading failures
- ✅ Gives services time to recover
- ✅ Fast failure when service is down

##### 4. Outbox Pattern (Transaction + Publish)
**Pattern:** Write events to database in same transaction as state change

**Implementation:**
```sql
BEGIN TRANSACTION;
  -- Update workflow state
  UPDATE workflows SET state = 'AwaitingSystemNumber' WHERE id = 'XYZ';

  -- Store event in outbox
  INSERT INTO outbox (workflow_id, event, created_at)
  VALUES ('XYZ', '{"type":"Sent","command":"GenerateSystemNumber"}', NOW());
COMMIT;
```

**Separate process publishes from outbox:**
```csharp
public class OutboxPublisher
{
    public async Task ProcessOutboxAsync()
    {
        var unpublishedEvents = await _db.Query(
            "SELECT * FROM outbox WHERE published = false ORDER BY created_at LIMIT 100"
        );

        foreach (var evt in unpublishedEvents)
        {
            await _messageBus.PublishAsync(evt.Event);
            await _db.Execute(
                "UPDATE outbox SET published = true WHERE id = @id",
                new { id = evt.Id }
            );
        }
    }
}
```

**Benefits:**
- ✅ Guarantees: if state changed, event will be published
- ✅ Atomic consistency
- ✅ Survives crashes

---

### Recommended Architecture

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

### Key Insights

1. **Commands are published with metadata** (message_id, workflow_id, idempotency_key)
2. **Consumers check idempotency** before processing
3. **Consumers publish result events** to confirm success
4. **Workflow continues** when it receives the result event
5. **Failures are handled** via retries, DLQ, and timeouts
6. **Event sourcing ensures** we can always replay and see what happened

---

## Reply Command Patterns

### TL;DR

**Reply commands are for async workflow-to-workflow communication, NOT for HTTP API queries.**

❌ **Don't use Reply for:**
- HTTP API status endpoints
- UI dashboards polling for state
- Admin tools checking workflow status

✅ **Do use Reply for:**
- Workflow-to-workflow queries (saga pattern)
- Cross-service queries in message-based systems
- Long-running operations where workflow waits for response

### What is Reply Actually For?

`Reply` is designed for **request-response patterns in asynchronous, message-based systems** where:

1. **No HTTP available** - Services communicate only via message bus/events
2. **Workflow waits for response** - The requesting workflow pauses its state machine
3. **Response arrives as event** - The reply becomes the next input to continue processing
4. **Async coordination** - Could take milliseconds, seconds, or even days

**Key insight:** Reply is about **workflow coordination**, not about exposing current state to external callers.

---

### Real-World Scenarios Where Reply is Correct

#### Scenario 1: Workflow-to-Workflow Communication (Saga Pattern)

**Use Case:** Order workflow needs to check inventory before confirming order

```csharp
// Order Workflow (order-123)
public override IReadOnlyList<WorkflowCommand<OrderOutput>> Decide(OrderInput input, OrderState state)
{
    return (input, state) switch
    {
        // Need to check inventory before proceeding
        (PlaceOrder order, New) => [
            Send(new CheckInventory(
                ProductId: order.ProductId,
                Quantity: order.Quantity,
                ReplyTo: "order-123"  // Important: correlation ID
            )),
            // Workflow transitions to waiting state
        ],

        // Inventory service replied with result
        (InventoryCheckResult result, WaitingForInventory) =>
            result.Available
                ? [ Send(new ConfirmOrder(...)) ]
                : [ Send(new RejectOrder(...)) ],

        _ => []
    };
}

// State transitions
New → WaitingForInventory → (OrderConfirmed | OrderRejected)
```

```csharp
// Inventory Workflow (inventory-system)
public override IReadOnlyList<WorkflowCommand<InventoryOutput>> Decide(InventoryInput input, InventoryState state)
{
    return (input, state) switch
    {
        // Another workflow is asking about inventory
        (CheckInventory check, _) => [
            Reply(new InventoryCheckResult(
                Available: state.Stock[check.ProductId] >= check.Quantity,
                ReservationId: GenerateReservationId()
            ))
        ],

        _ => []
    };
}
```

**Flow:**
1. Order workflow sends `CheckInventory` command (with `ReplyTo`)
2. Order workflow state: `New` → `WaitingForInventory`
3. Inventory workflow receives `CheckInventory` as input
4. Inventory workflow generates `Reply(InventoryCheckResult)`
5. Reply command executed → sends message back to Order workflow
6. Order workflow receives `InventoryCheckResult` as next input
7. Order workflow state: `WaitingForInventory` → `OrderConfirmed`

**Why Reply is correct:**
- ✅ Workflows communicate via messages (no HTTP)
- ✅ Order workflow pauses and waits
- ✅ Reply comes back as an event/input
- ✅ Async coordination between services

---

#### Scenario 2: External System Integration

**Use Case:** Payment workflow needs to charge customer via external payment gateway

```csharp
// Payment Workflow
public override IReadOnlyList<WorkflowCommand<PaymentOutput>> Decide(PaymentInput input, PaymentState state)
{
    return (input, state) switch
    {
        // Initiate payment with external gateway
        (InitiatePayment payment, New) => [
            Send(new ChargeCustomer(
                Amount: payment.Amount,
                Card: payment.CardToken,
                ReplyTo: $"payment-{payment.PaymentId}"
            )),
        ],

        // External gateway replied (could be 5-30 seconds later)
        (PaymentConfirmed confirmed, WaitingForConfirmation) => [
            Send(new NotifyOrderService(confirmed.TransactionId)),
            Complete()
        ],

        (PaymentFailed failed, WaitingForConfirmation) => [
            Send(new NotifyOrderService(failed.Reason)),
            Complete()
        ],

        _ => []
    };
}

// State machine
New → WaitingForConfirmation → (PaymentSucceeded | PaymentFailed)
```

**Why Reply is correct:**
- ✅ External system takes time (5-30 seconds)
- ✅ Workflow must wait for response
- ✅ Response arrives as event
- ✅ Async by nature

---

#### Scenario 3: Human Task / Approval Flow

**Use Case:** Expense approval workflow waits for manager decision

```csharp
// Expense Workflow
public override IReadOnlyList<WorkflowCommand<ExpenseOutput>> Decide(ExpenseInput input, ExpenseState state)
{
    return (input, state) switch
    {
        // Request approval from manager
        (SubmitExpense expense, New) => [
            Send(new RequestApproval(
                ManagerId: expense.ManagerId,
                Amount: expense.Amount,
                ReplyTo: $"expense-{expense.ExpenseId}"
            )),
            Schedule(new TimeoutApproval(), after: TimeSpan.FromDays(7))
        ],

        // Manager approved (could be hours or days later)
        (ApprovalDecision { Approved: true }, WaitingForApproval) => [
            Send(new ProcessReimbursement(...)),
            Complete()
        ],

        // Manager rejected
        (ApprovalDecision { Approved: false, Reason: var reason }, WaitingForApproval) => [
            Send(new NotifyEmployee(reason)),
            Complete()
        ],

        // Timeout - no response after 7 days
        (TimeoutApproval, WaitingForApproval) => [
            Send(new EscalateToSeniorManager(...))
        ],

        _ => []
    };
}

// State machine
New → WaitingForApproval → (Approved | Rejected | Escalated)
```

**Why Reply is correct:**
- ✅ Workflow waits for human decision (hours/days)
- ✅ Decision comes back as event
- ✅ Long-running coordination
- ✅ Timeout handling needed

---

#### Scenario 4: Cross-Service Queries in Microservices

**Use Case:** Order service needs customer credit limit from customer service (no HTTP - message bus only)

```csharp
// Order Workflow
public override IReadOnlyList<WorkflowCommand<OrderOutput>> Decide(OrderInput input, OrderState state)
{
    return (input, state) switch
    {
        // Need credit limit before processing order
        (ProcessOrder order, New) => [
            Send(new GetCreditLimit(
                CustomerId: order.CustomerId,
                ReplyTo: $"order-{order.OrderId}"
            ))
        ],

        // Credit service replied with limit
        (CreditLimitResult result, CheckingCredit) =>
            state.OrderAmount <= result.CreditLimit
                ? [ Send(new ConfirmOrder(...)) ]
                : [ Send(new RejectOrder("Insufficient credit")) ],

        _ => []
    };
}

// State machine
New → CheckingCredit → (OrderConfirmed | OrderRejected)
```

```csharp
// Customer Service Workflow
public override IReadOnlyList<WorkflowCommand<CustomerOutput>> Decide(CustomerInput input, CustomerState state)
{
    return (input, state) switch
    {
        // Order service asking for credit limit
        (GetCreditLimit query, _) => [
            Reply(new CreditLimitResult(
                CreditLimit: state.Customers[query.CustomerId].CreditLimit
            ))
        ],

        _ => []
    };
}
```

**Why Reply is correct:**
- ✅ Services communicate via message bus (not HTTP)
- ✅ Order workflow waits for response
- ✅ Response enables workflow to continue
- ✅ Microservices coordination pattern

---

### When Reply is WRONG: HTTP API Queries

#### Anti-Pattern: Using Reply for HTTP Status Endpoints

**BAD APPROACH:**
```csharp
// ❌ DON'T DO THIS
[HttpGet("group-checkout/{id}/status")]
public async Task<IActionResult> GetStatus(string id)
{
    // Process GetCheckoutStatus through workflow
    var result = await _orchestrator.Process(
        _workflow,
        snapshot,
        new GetCheckoutStatus(id),
        begins: false
    );

    // Extract Reply command from result
    var replyCommand = result.Commands.OfType<Reply<GroupCheckoutOutputMessage>>().First();
    var status = (CheckoutStatus)replyCommand.Message;

    return Ok(status);
}
```

**Why this is wrong:**
1. ❌ HTTP is synchronous - no need for async Reply pattern
2. ❌ Reply command stored in stream but never "executed" (inconsistent)
3. ❌ Adds complexity for no benefit
4. ❌ Bypasses the purpose of Reply (workflow coordination)

**GOOD APPROACH:**
```csharp
// ✅ DO THIS INSTEAD
[HttpGet("group-checkout/{id}/status")]
public async Task<IActionResult> GetStatus(string id)
{
    // Read stream directly
    var messages = await _persistence.ReadStreamAsync($"group-checkout-{id}");

    // Rebuild state from events
    var state = RebuildState(messages);

    // Return current state
    return Ok(new
    {
        GroupCheckoutId = id,
        Status = state switch
        {
            NotExisting => "NotFound",
            Pending p => "Pending",
            Finished => "Finished",
            _ => "Unknown"
        },
        TotalGuests = state is Pending p ? p.Guests.Count : 0,
        CompletedGuests = state is Pending p ? p.Guests.Count(g => g.GuestStayStatus == GuestStayStatus.Completed) : 0,
        // ...
    });
}

private GroupCheckoutState RebuildState(IReadOnlyList<WorkflowMessage> messages)
{
    var state = _workflow.InitialState;

    foreach (var msg in messages.Where(m => m.Kind == MessageKind.Event))
    {
        state = _workflow.Evolve(state, (WorkflowEvent)msg.Message);
    }

    return state;
}
```

**Why this is correct:**
1. ✅ Simple and direct - reads current state synchronously
2. ✅ Fast - no workflow processing overhead
3. ✅ No unnecessary Reply commands in stream
4. ✅ Uses HTTP's synchronous nature appropriately

---

### Alternative: Projections / Read Models

For frequently accessed queries (dashboards, monitoring), consider **projections**:

```csharp
// Projection updated whenever workflow events occur
public class GroupCheckoutProjection
{
    public string GroupCheckoutId { get; set; }
    public string Status { get; set; }
    public int TotalGuests { get; set; }
    public int CompletedGuests { get; set; }
    public int FailedGuests { get; set; }
    public int PendingGuests { get; set; }
    public DateTime LastUpdated { get; set; }
}

// Projection builder (subscribes to workflow events)
public class GroupCheckoutProjectionBuilder
{
    public async Task Handle(WorkflowEvent @event)
    {
        if (@event is InitiatedBy<GroupCheckoutInputMessage, GroupCheckoutOutputMessage> initiated)
        {
            var msg = (InitiateGroupCheckout)initiated.Message;
            await _db.InsertAsync(new GroupCheckoutProjection
            {
                GroupCheckoutId = msg.GroupCheckoutId,
                Status = "Pending",
                TotalGuests = msg.Guests.Count,
                // ...
            });
        }
        else if (@event is Received<GroupCheckoutInputMessage, GroupCheckoutOutputMessage> received)
        {
            // Update projection based on guest checkout events
            // ...
        }
    }
}

// Controller reads from projection (super fast)
[HttpGet("group-checkout/{id}/status")]
public async Task<IActionResult> GetStatus(string id)
{
    var projection = await _db.GetProjectionAsync(id);
    return Ok(projection);
}
```

**Benefits:**
- ✅ Extremely fast reads (no stream replay)
- ✅ Optimized for queries
- ✅ CQRS pattern (separate read/write models)
- ✅ Can denormalize data for UI needs

---

### Implementation Patterns

#### Pattern 1: Reply with Correlation ID

For workflow-to-workflow queries:

```csharp
// Requesting workflow
Send(new QueryInventory(
    ProductId: "ABC",
    ReplyTo: $"order-{orderId}"  // Correlation ID for routing reply back
))

// Responding workflow
Reply(new InventoryResult(...))
```

The `ReplyTo` field tells the message bus where to route the reply.

#### Pattern 2: Reply with Timeout

Always include timeouts for replies:

```csharp
(PlaceOrder order, New) => [
    Send(new CheckInventory(order.ProductId, replyTo: workflowId)),
    Schedule(new TimeoutInventoryCheck(), after: TimeSpan.FromSeconds(30))
],

// If no reply in 30 seconds, handle timeout
(TimeoutInventoryCheck, WaitingForInventory) => [
    Send(new RejectOrder("Inventory check timed out"))
]
```

#### Pattern 3: State Machine with Waiting States

Workflows using Reply typically have "waiting" states:

```
New → WaitingForX → Continued
       ↓ (timeout)
       → TimedOut
```

---

## Decision Matrices

### Reliability Pattern Selection

| Problem | Pattern | When to Use |
|---------|---------|-------------|
| Don't know if command succeeded | Event-Driven Confirmation | Always (recommended) |
| Need to undo steps on failure | Saga with Compensation | Multi-step transactions |
| Prevent waiting indefinitely | Timeout Pattern | Any async operation |
| Prevent duplicate execution | Message Deduplication ID | Generic solution |
| Prevent duplicate execution | Idempotency Key | Business-level semantics |
| Prevent duplicate execution | Natural Idempotency | When operations are naturally idempotent |
| Prevent duplicate execution | Optimistic Locking | Concurrent state updates |
| Handle transient failures | Exponential Backoff | Always (default) |
| Handle persistent failures | Dead Letter Queue | Always (fallback) |
| Prevent cascading failures | Circuit Breaker | External service calls |
| Ensure state/event consistency | Outbox Pattern | Critical consistency needs |

### Reply Command Selection

| Scenario | Use Reply? | Use Direct Read? | Notes |
|----------|-----------|------------------|-------|
| HTTP API status query | ❌ | ✅ | Synchronous, no workflow coordination |
| UI dashboard polling | ❌ | ✅ (projection) | Use read model for performance |
| Admin checking status | ❌ | ✅ | Simple state inspection |
| Workflow querying another workflow | ✅ | ❌ | Async coordination, message bus |
| External system integration | ✅ | ❌ | Long-running, async response |
| Human approval task | ✅ | ❌ | Days of waiting, async reply |
| Cross-service query (no HTTP) | ✅ | ❌ | Message bus communication |
| Background job checking status | ❌ | ✅ | No coordination needed |

---

## Code Examples

### Reliability: Complete Command Flow with Confirmation

```csharp
// Step 1: Send command with timeout
(PlaceOrder order, New) => [
    Send(new GenerateSystemNumber(order.OrderId)),
    Schedule(new TimeoutSystemNumber(), after: TimeSpan.FromMinutes(5))
],

// Step 2: Receive confirmation (success)
(SystemNumberGenerated result, WaitingForSystemNumber) => [
    Send(new ProcessOrder(result.SystemNumber)),
    Complete()
],

// Step 3: Handle timeout (failure)
(TimeoutSystemNumber, WaitingForSystemNumber) => [
    Send(new RejectOrder("System number generation timed out")),
    Complete()
]
```

### Idempotency: Natural vs Key-Based

```csharp
// Natural Idempotency (preferred)
public async Task UpdateOrderStatus(string orderId, string status)
{
    // This operation is naturally idempotent
    await _db.ExecuteAsync(
        "UPDATE orders SET status = @status WHERE order_id = @orderId",
        new { orderId, status }
    );
}

// Key-Based Idempotency (when natural isn't possible)
public async Task<string> GenerateSystemNumber(string orderId)
{
    var idempotencyKey = $"system-number:{orderId}";

    if (await _cache.Exists(idempotencyKey))
    {
        return await _cache.Get(idempotencyKey);
    }

    var systemNumber = await _generator.GenerateAsync();
    await _cache.Set(idempotencyKey, systemNumber, TimeSpan.FromHours(24));

    return systemNumber;
}
```

### Reply: Workflow-to-Workflow with Timeout

```csharp
// Requesting Workflow
public override IReadOnlyList<WorkflowCommand<OrderOutput>> Decide(OrderInput input, OrderState state)
{
    return (input, state) switch
    {
        (PlaceOrder order, New) => [
            Send(new CheckInventory(order.ProductId, replyTo: $"order-{order.OrderId}")),
            Schedule(new TimeoutInventoryCheck(), after: TimeSpan.FromSeconds(30))
        ],

        (InventoryCheckResult { Available: true }, WaitingForInventory) => [
            Send(new ConfirmOrder(...))
        ],

        (InventoryCheckResult { Available: false }, WaitingForInventory) => [
            Send(new RejectOrder("Out of stock"))
        ],

        (TimeoutInventoryCheck, WaitingForInventory) => [
            Send(new RejectOrder("Inventory check timed out"))
        ],

        _ => []
    };
}

// Responding Workflow
public override IReadOnlyList<WorkflowCommand<InventoryOutput>> Decide(InventoryInput input, InventoryState state)
{
    return (input, state) switch
    {
        (CheckInventory check, _) => [
            Reply(new InventoryCheckResult(
                Available: state.Stock[check.ProductId] >= check.Quantity
            ))
        ],

        _ => []
    };
}
```

---

## Summary

### Reliability Patterns: Use When...

- **Event-Driven Confirmation**: Always (best practice for distributed systems)
- **Saga/Compensation**: Multi-step transactions that may need rollback
- **Timeout**: Any async operation (always include)
- **Idempotency**: Every command handler (prevents duplicates)
- **Exponential Backoff**: Default retry strategy
- **DLQ**: Fallback for persistent failures
- **Circuit Breaker**: External service integrations
- **Outbox Pattern**: Critical consistency requirements

### Reply Commands: Use When...

✅ **Use Reply For:**
- Async workflow-to-workflow communication
- Long-running operations with response
- Message bus environments (no HTTP)
- Saga pattern coordination
- External system integration
- Human approval tasks

❌ **Don't Use Reply For:**
- HTTP API status endpoints
- UI dashboards
- Admin tools
- Simple state inspection
- When you have synchronous HTTP available

### Alternative Approaches

- **Direct stream reads** - For HTTP APIs
- **Projections/Read models** - For performance-critical queries
- **HTTP endpoints** - When services can communicate via HTTP

---

## Further Reading

- **ARCHITECTURE.md** - Overall stream architecture and component design
- **IMPLEMENTATION_STATE.md** - Current implementation status
- Saga Pattern: https://microservices.io/patterns/data/saga.html
- CQRS: https://martinfowler.com/bliki/CQRS.html
- Idempotency: https://stripe.com/docs/api/idempotent_requests

---

**Last Updated:** 2025-11-21
**Status:** Patterns documented and validated with GroupCheckoutWorkflow implementation
