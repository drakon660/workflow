# Reply Command Patterns: When and How to Use

**Last Updated:** 2025-11-17

---

## TL;DR

**Reply commands are for async workflow-to-workflow communication, NOT for HTTP API queries.**

❌ **Don't use Reply for:**
- HTTP API status endpoints
- UI dashboards polling for state
- Admin tools checking workflow status

✅ **Do use Reply for:**
- Workflow-to-workflow queries (saga pattern)
- Cross-service queries in message-based systems
- Long-running operations where workflow waits for response

---

## The Fundamental Question

### What is Reply Actually For?

`Reply` is designed for **request-response patterns in asynchronous, message-based systems** where:

1. **No HTTP available** - Services communicate only via message bus/events
2. **Workflow waits for response** - The requesting workflow pauses its state machine
3. **Response arrives as event** - The reply becomes the next input to continue processing
4. **Async coordination** - Could take milliseconds, seconds, or even days

**Key insight:** Reply is about **workflow coordination**, not about exposing current state to external callers.

---

## Real-World Scenarios Where Reply is Correct

### Scenario 1: Workflow-to-Workflow Communication (Saga Pattern)

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

### Scenario 2: External System Integration

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

**External Payment Gateway Service:**
```csharp
// This could be a separate service that integrates with Stripe, PayPal, etc.
public class PaymentGatewayService
{
    public async Task ProcessCharge(ChargeCustomer charge)
    {
        // Call external API (Stripe, PayPal, etc.)
        var result = await _stripeClient.ChargeAsync(charge.Amount, charge.Card);

        // Send reply back to workflow
        if (result.Success)
            await _messageBus.PublishAsync(new PaymentConfirmed(
                TransactionId: result.Id,
                WorkflowId: charge.ReplyTo  // Route back to waiting workflow
            ));
        else
            await _messageBus.PublishAsync(new PaymentFailed(
                Reason: result.ErrorMessage,
                WorkflowId: charge.ReplyTo
            ));
    }
}
```

**Why Reply is correct:**
- ✅ External system takes time (5-30 seconds)
- ✅ Workflow must wait for response
- ✅ Response arrives as event
- ✅ Async by nature

---

### Scenario 3: Human Task / Approval Flow

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

**Approval UI/Service:**
```csharp
// Manager clicks "Approve" or "Reject" in UI
public class ApprovalService
{
    public async Task SubmitDecision(string expenseId, bool approved, string reason)
    {
        // Send reply back to workflow
        await _messageBus.PublishAsync(new ApprovalDecision(
            ExpenseId: expenseId,
            Approved: approved,
            Reason: reason,
            WorkflowId: $"expense-{expenseId}"  // Route to waiting workflow
        ));
    }
}
```

**Why Reply is correct:**
- ✅ Workflow waits for human decision (hours/days)
- ✅ Decision comes back as event
- ✅ Long-running coordination
- ✅ Timeout handling needed

---

### Scenario 4: Cross-Service Queries in Microservices

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

## When Reply is WRONG: HTTP API Queries

### Anti-Pattern: Using Reply for HTTP Status Endpoints

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
        FailedGuests = state is Pending p ? p.Guests.Count(g => g.GuestStayStatus == GuestStayStatus.Failed) : 0,
        PendingGuests = state is Pending p ? p.Guests.Count(g => g.GuestStayStatus == GuestStayStatus.Pending) : 0
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

## Alternative: Projections / Read Models

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

## Decision Matrix: Reply vs Direct Read

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

## Implementation Patterns

### Pattern 1: Reply with Correlation ID

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

### Pattern 2: Reply with Timeout

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

### Pattern 3: State Machine with Waiting States

Workflows using Reply typically have "waiting" states:

```
New → WaitingForX → Continued
       ↓ (timeout)
       → TimedOut
```

---

## Why GetCheckoutStatus Was Commented Out

In our `GroupCheckoutWorkflow`, we initially implemented `GetCheckoutStatus` with Reply:

```csharp
// This was a learning example, but NOT the right pattern
(GetCheckoutStatus m, Pending p) => [
    Reply(new CheckoutStatus(...))
]
```

**Why we commented it out:**
1. ❌ Intended for HTTP API - should use direct stream reads
2. ❌ No workflow coordination happening
3. ❌ Reply command stored but never truly "executed"
4. ❌ Adds unnecessary complexity

**Kept the code commented** (not deleted) because:
- ✅ Demonstrates what NOT to do
- ✅ Shows Reply syntax for learning
- ✅ May be useful if we add cross-workflow queries later

---

## When to Add Reply Back

Consider adding Reply when you have:

1. **Saga coordination** - Multiple workflows need to coordinate
2. **External integrations** - Calling external services asynchronously
3. **Human tasks** - Approvals that take hours/days
4. **Message-based architecture** - No HTTP between services

**Example future scenarios:**
- Order workflow queries GroupCheckout workflow for status before proceeding
- Housekeeping workflow waits for GroupCheckout completion
- Billing workflow requests checkout summary for invoice

---

## Summary

### ✅ Use Reply For:
- **Async workflow-to-workflow communication**
- **Long-running operations with response**
- **Message bus environments (no HTTP)**
- **Saga pattern coordination**
- **External system integration**
- **Human approval tasks**

### ❌ Don't Use Reply For:
- **HTTP API status endpoints**
- **UI dashboards**
- **Admin tools**
- **Simple state inspection**
- **When you have synchronous HTTP available**

### Alternative Approaches:
- **Direct stream reads** - For HTTP APIs
- **Projections/Read models** - For performance-critical queries
- **HTTP endpoints** - When services can communicate via HTTP

---

## Code Examples in This Codebase

### Reply Command (Framework)
```csharp
// Workflow/Workflow/WorkflowCommand.cs
public record Reply<TOutput>(TOutput Message) : WorkflowCommand<TOutput>;
```

### Commented Out Example (Learning)
```csharp
// Workflow/Workflow.Tests/GroupCheckoutWorkflow.cs
// Lines 29-42: Commented out GetCheckoutStatus Evolve handling
// Lines 82-94: Commented out GetCheckoutStatus Decide handling
// Lines 195-196: Commented out GetCheckoutStatus message type
// Lines 205-215: Commented out CheckoutStatus response type
```

### Tests (Also Commented Out)
```csharp
// Workflow/Workflow.Tests/GroupCheckoutWorkflowTests.cs
// Lines 445-488: Commented out GetCheckoutStatus Reply tests
```

---

## Further Reading

- **UNIFIED_STREAM_ARCHITECTURE.md** - Overall architecture
- **CONSUMER_AND_PERSISTENCE_DISCUSSION.md** - Consumer patterns
- **ReliabilityPatterns.md** - Reliability and idempotency
- Saga Pattern: https://microservices.io/patterns/data/saga.html
- CQRS: https://martinfowler.com/bliki/CQRS.html

---

**Last Updated:** 2025-11-17
**Test Status:** 45 tests passing (GetCheckoutStatus tests commented out)
**Framework Status:** Reply command type remains in framework for future use
