# Reply Command Patterns: A Practical Guide

**Last Updated:** 2025-11-21

---

## Overview

The `Reply` command enables request-response patterns in workflows. This guide helps you decide when Reply is the right choice vs alternatives.

**Key Principle:** Choose the pattern that best fits your use case - there's no one-size-fits-all answer.

---

## Decision Tree

```
Do you need to query workflow state?
│
├─ Is it workflow-to-workflow communication?
│  ├─ YES → Use Reply ✅
│  │   • Async coordination
│  │   • Waiting states
│  │   • Timeouts required
│  │
│  └─ NO → It's an external query (HTTP, UI, etc.)
│           │
│           ├─ Does query involve business logic/computations?
│           │  ├─ YES → Reply is fine ✅
│           │  │   • Business logic stays in workflow
│           │  │   • Single source of truth
│           │  │   • Testable
│           │  │
│           │  └─ NO → Simple state read
│           │      │
│           │      ├─ High volume? → Use Projection ✅
│           │      └─ Low volume? → Direct stream read ✅
```

---

## Pattern 1: Workflow-to-Workflow Communication

**Use Case:** One workflow needs information from another workflow

### When to Use
- ✅ Saga pattern coordination
- ✅ Cross-service queries (message bus only)
- ✅ External system integration
- ✅ Human approval tasks

### Example: Order Queries Inventory

```csharp
// Order Workflow
public override IReadOnlyList<WorkflowCommand<OrderOutput>> Decide(OrderInput input, OrderState state)
{
    return (input, state) switch
    {
        // Need to check inventory before proceeding
        (PlaceOrder order, OrderCreated) => [
            Send(new CheckInventory(
                ProductId: order.ProductId,
                Quantity: order.Quantity,
                ReplyTo: $"order-{order.OrderId}"  // Correlation ID
            )),
            Schedule(new TimeoutInventoryCheck(), after: TimeSpan.FromSeconds(30))  // ⚠️ Always add timeout!
        ],

        // Inventory replied with availability
        (InventoryCheckResult result, WaitingForInventory) =>
            result.Available
                ? [ Send(new ConfirmOrder(...)) ]
                : [ Send(new RejectOrder(...)) ],

        // Timeout occurred
        (TimeoutInventoryCheck, WaitingForInventory) => [
            Send(new RejectOrder("Inventory check timed out"))
        ],

        _ => []
    };
}

// State transitions
OrderCreated → WaitingForInventory → (OrderConfirmed | OrderRejected | TimedOut)
```

```csharp
// Inventory Workflow
public override IReadOnlyList<WorkflowCommand<InventoryOutput>> Decide(InventoryInput input, InventoryState state)
{
    return (input, state) switch
    {
        // Another workflow is asking about inventory
        (CheckInventory check, _) => [
            Reply(new InventoryCheckResult(
                Available: state.Stock[check.ProductId] >= check.Quantity,
                ReservationId: state.Stock[check.ProductId] >= check.Quantity
                    ? ReserveInventory(check.ProductId, check.Quantity)
                    : null
            ))
        ],

        _ => []
    };
}
```

**Why Reply is correct here:**
- ✅ No HTTP between services (message bus only)
- ✅ Order workflow must wait for response
- ✅ Async coordination between workflows
- ✅ Timeout handling built-in

---

## Pattern 2: HTTP Query with Business Logic

**Use Case:** HTTP endpoint needs workflow to compute/validate result

### When to Use
- ✅ Query involves business rules
- ✅ Computation based on state
- ✅ Validation logic
- ✅ Business logic should live in workflow

### Example: Check Order Cancellation Eligibility

```csharp
// HTTP Controller
[HttpGet("order/{id}/can-cancel")]
public async Task<IActionResult> CanCancelOrder(string id)
{
    var snapshot = await LoadSnapshot(id);

    var result = _orchestrator.Run(
        _workflow,
        snapshot,
        new CheckCancellationEligibility(id),
        begins: false
    );

    // Get Reply command (business logic computed in workflow)
    var reply = result.Commands.OfType<Reply<OrderOutput>>().First();
    var eligibility = (CancellationEligibility)reply.Message;

    return Ok(eligibility);
}
```

```csharp
// Workflow
public override IReadOnlyList<WorkflowCommand<OrderOutput>> Decide(OrderInput input, OrderState state)
{
    return (input, state) switch
    {
        // Query with business logic
        (CheckCancellationEligibility q, OrderCreated order) => [
            Reply(new CancellationEligibility(
                CanCancel: true,
                Reason: "Order not yet shipped",
                RefundAmount: order.TotalAmount
            ))
        ],

        (CheckCancellationEligibility q, Shipped shipped) => [
            Reply(new CancellationEligibility(
                CanCancel: false,
                Reason: "Order already shipped",
                RefundAmount: 0
            ))
        ],

        (CheckCancellationEligibility q, Delivered delivered) => [
            Reply(new CancellationEligibility(
                CanCancel: true,
                Reason: "Can return within 30 days",
                RefundAmount: CalculateRefund(delivered)  // Complex business rule
            ))
        ],

        _ => []
    };
}

protected override OrderState InternalEvolve(OrderState state, WorkflowEvent<OrderInput, OrderOutput> evt)
{
    return (state, evt) switch
    {
        // Query doesn't change state
        (var s, Events.Received { Message: CheckCancellationEligibility }) => s,

        // ... other state transitions
        _ => state
    };
}
```

**Why Reply is correct here:**
- ✅ Business logic stays in workflow (single source of truth)
- ✅ Testable via workflow unit tests
- ✅ State-dependent logic (different rules per state)
- ✅ Evolve returns state unchanged (no side effects)
- ✅ Audit trail: reply stored in stream

**Alternative (less maintainable):**
```csharp
// ❌ Duplicates business logic in controller
[HttpGet("order/{id}/can-cancel")]
public async Task<IActionResult> CanCancelOrder(string id)
{
    var state = RebuildState(await LoadStream(id));

    // Business logic duplicated here - harder to maintain
    var canCancel = state switch
    {
        OrderCreated => true,
        Shipped => false,
        Delivered d => (DateTime.Now - d.DeliveredAt).Days < 30,
        _ => false
    };

    return Ok(new { CanCancel = canCancel });
}
```

---

## Pattern 3: Simple State Read (No Business Logic)

**Use Case:** Just need current state without computation

### When to Use
- ✅ Simple CRUD "get by id"
- ✅ No business rules to apply
- ✅ Just return current state

### Example: Get Order Status

```csharp
[HttpGet("order/{id}")]
public async Task<IActionResult> GetOrder(string id)
{
    var messages = await _persistence.ReadStreamAsync($"order-{id}");

    // Rebuild state from events
    var state = RebuildState(messages);

    if (state is NoOrder)
        return NotFound();

    // Map state to DTO
    return Ok(new OrderDTO
    {
        OrderId = GetOrderId(state),
        Status = state switch
        {
            OrderCreated => "Created",
            PaymentConfirmed => "PaymentConfirmed",
            Shipped s => "Shipped",
            Delivered => "Delivered",
            Cancelled => "Cancelled",
            _ => "Unknown"
        },
        TrackingNumber = state is Shipped s ? s.TrackingNumber : null
    });
}

private OrderState RebuildState(IReadOnlyList<WorkflowMessage> messages)
{
    var state = _workflow.InitialState;

    foreach (var msg in messages.Where(m => m.Kind == MessageKind.Event))
    {
        state = _workflow.Evolve(state, (WorkflowEvent)msg.Message);
    }

    return state;
}
```

**Why direct read is better here:**
- ✅ Simple, fast, direct
- ✅ No business logic to test
- ✅ No overhead of Decide/Translate
- ✅ Just state mapping

---

## Pattern 4: High-Volume Queries (Projections)

**Use Case:** Dashboard, reporting, list queries with high traffic

### When to Use
- ✅ Read-heavy operations
- ✅ Multiple queries per second
- ✅ Complex aggregations
- ✅ Denormalized views needed

### Example: Order Dashboard

```csharp
// Projection (maintained by event subscriber)
public class OrderProjection
{
    public string OrderId { get; set; }
    public string CustomerId { get; set; }
    public string Status { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
}

// Event Subscriber
public class OrderProjectionBuilder
{
    public async Task Handle(WorkflowEvent evt)
    {
        if (evt is Events.InitiatedBy { Message: PlaceOrder order })
        {
            await _db.InsertAsync(new OrderProjection
            {
                OrderId = order.OrderId,
                CustomerId = order.CustomerId,
                Status = "Created",
                CreatedAt = DateTime.UtcNow
            });
        }
        else if (evt is Events.Received { Message: OrderShipped shipped })
        {
            await _db.UpdateAsync(order => order.OrderId == shipped.OrderId,
                new { Status = "Shipped" });
        }
        // ... handle other events
    }
}

// Controller reads from projection (super fast)
[HttpGet("orders")]
public async Task<IActionResult> ListOrders([FromQuery] string customerId)
{
    var orders = await _readDb.GetOrdersAsync(customerId);
    return Ok(orders);
}
```

**Why projection is better here:**
- ✅ Extremely fast reads (no stream replay)
- ✅ Optimized for queries (indexes, filters)
- ✅ CQRS pattern (separate read/write models)
- ✅ Can denormalize data for UI needs

---

## Pattern 5: External System Integration

**Use Case:** Workflow waits for external service response

### Example: Payment Gateway

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
            Schedule(new PaymentTimeout(), after: TimeSpan.FromSeconds(30))
        ],

        // Gateway replied successfully
        (PaymentConfirmed confirmed, WaitingForConfirmation) => [
            Send(new NotifyOrderService(confirmed.TransactionId)),
            Complete()
        ],

        // Gateway replied with failure
        (PaymentFailed failed, WaitingForConfirmation) => [
            Send(new NotifyOrderService(failed.Reason)),
            Complete()
        ],

        // Timeout waiting for gateway
        (PaymentTimeout, WaitingForConfirmation) => [
            Send(new NotifyOrderService("Payment gateway timeout")),
            Complete()
        ],

        _ => []
    };
}

// State machine
New → WaitingForConfirmation → (PaymentSucceeded | PaymentFailed | TimedOut)
```

**Why Reply is correct here:**
- ✅ External system takes time (5-30 seconds)
- ✅ Workflow must wait for response
- ✅ Response arrives as event
- ✅ Async by nature

---

## Pattern 6: Human Approval Tasks

**Use Case:** Workflow waits for human decision (hours/days)

### Example: Expense Approval

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

        // Manager approved
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

**Why Reply is correct here:**
- ✅ Workflow waits for human decision (hours/days)
- ✅ Decision comes back as event
- ✅ Long-running coordination
- ✅ Timeout handling needed

---

## Summary: When to Use What

| Use Case | Pattern | Reason |
|----------|---------|--------|
| Workflow → Workflow query | Reply | Async coordination, waiting states |
| HTTP query with business logic | Reply | Keep logic in workflow, testable |
| Simple HTTP "get by id" | Direct Read | Fast, simple, no computation |
| High-volume dashboard | Projection | Performance, CQRS |
| External system integration | Reply | Async, wait for response |
| Human approval (hours/days) | Reply | Long-running, timeout handling |

---

## Key Principles

### 1. Always Add Timeouts with Reply

```csharp
// ✅ ALWAYS do this
Send(new QueryInventory(...)),
Schedule(new TimeoutInventoryCheck(), after: TimeSpan.FromSeconds(30))

// ❌ NEVER do this (no timeout)
Send(new QueryInventory(...))  // What if reply never comes?
```

### 2. Reply for Queries Doesn't Change State

```csharp
protected override State InternalEvolve(State state, WorkflowEvent evt)
{
    return (state, evt) switch
    {
        // Query events return state unchanged
        (var s, Events.Received { Message: CheckEligibility }) => s,

        // Only mutation events change state
        (OrderCreated, Events.Received { Message: PaymentReceived }) => new PaymentConfirmed(...),

        _ => state
    };
}
```

### 3. Business Logic in Workflow = Single Source of Truth

**Good:**
```csharp
// Business logic in one place (workflow)
(CheckEligibility, OrderCreated o) => [
    Reply(new Eligibility(
        CanCancel: o.Amount < 1000,  // Rule
        RefundAmount: CalculateRefund(o)  // Logic
    ))
]
```

**Bad:**
```csharp
// Business logic duplicated in controller
[HttpGet]
public IActionResult Check(string id)
{
    var state = LoadState(id);
    var canCancel = state.Amount < 1000;  // ❌ Duplicated rule
    return Ok(canCancel);
}
```

### 4. Queries are Auditable

When you use Reply, queries are stored in stream:
```
Position 1: OrderCreated
Position 2: Received(CheckEligibility) ← Query executed
Position 3: Replied(Eligibility) ← Result returned
```

Audit trail: "At time T, user X queried eligibility and got result Y"

---

## Migration Guide

### If You Used Reply for Simple Reads

**Before:**
```csharp
var result = orchestrator.Run(workflow, snapshot, new GetOrder(id), false);
var reply = result.Commands.OfType<Reply>().First();
return Ok(reply.Message);
```

**After (simpler):**
```csharp
var state = RebuildState(await persistence.ReadStreamAsync(id));
return Ok(MapToDTO(state));
```

### If You Used Direct Read for Business Logic

**Before:**
```csharp
var state = RebuildState(await LoadStream(id));
var canCancel = /* business logic here */;  // ❌ Logic in controller
return Ok(canCancel);
```

**After (better):**
```csharp
var result = orchestrator.Run(workflow, snapshot, new CheckCancellation(id), false);
var reply = result.Commands.OfType<Reply>().First();
return Ok(reply.Message);  // ✅ Logic in workflow
```

---

## Further Reading

- **ARCHITECTURE.md** - Overall stream architecture
- **PATTERNS.md** - Reliability and other patterns
- **IMPLEMENTATION_STATE.md** - Current implementation status
- Saga Pattern: https://microservices.io/patterns/data/saga.html
- CQRS: https://martinfowler.com/bliki/CQRS.html

---

**Last Updated:** 2025-11-21
**Key Change:** Added nuance - Reply is fine for HTTP queries with business logic
**Status:** Practical guidance, not dogmatic rules
