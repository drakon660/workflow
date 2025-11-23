# Wolverine Hybrid Architecture Design

**Date:** 2025-11-23

**Topic:** Using Wolverine as Infrastructure Layer for Our Workflow Engine

---

## Table of Contents

1. [Overview](#overview)
2. [What is Wolverine](#what-is-wolverine)
3. [Hybrid Architecture Design](#hybrid-architecture-design)
4. [Integration Points](#integration-points)
5. [Complete Flow Example](#complete-flow-example)
6. [Benefits Analysis](#benefits-analysis)
7. [Challenges and Solutions](#challenges-and-solutions)
8. [Recommended Implementation](#recommended-implementation)
9. [Configuration Example](#configuration-example)
10. [Decision Summary](#decision-summary)

---

## Overview

**Proposed Approach:** Use Wolverine as the messaging infrastructure layer while keeping our custom workflow orchestration engine.

**Division of Responsibilities:**

```
┌─────────────────────────────────────────────────────────────────┐
│ OUR WORKFLOW ENGINE (Core Business Logic)                       │
│                                                                  │
│  - Workflow<TInput, TState, TOutput>                            │
│  - Decide(input, state) → commands                              │
│  - Evolve(state, event) → state                                 │
│  - WorkflowOrchestrator (pure orchestration)                    │
│  - Unified stream storage (our design)                          │
│  - Processed flag pattern                                       │
└────────────────┬────────────────────────────────────────────────┘
                 │
                 │ Delegates infrastructure to...
                 │
┌────────────────▼────────────────────────────────────────────────┐
│ WOLVERINE (Infrastructure Layer)                                │
│                                                                  │
│  ✅ Inbox: Read events from source streams                      │
│  ✅ Outbox: Queue commands for execution                        │
│  ✅ Command Handlers: Execute commands via handlers             │
│  ✅ Message Bus: Send to queues/topics                          │
│  ✅ Durability: Transactional inbox/outbox pattern              │
│  ✅ Retry/DLQ: Error handling infrastructure                    │
└─────────────────────────────────────────────────────────────────┘
```

---

## What is Wolverine

**Wolverine** is a .NET library for building asynchronous, message-driven applications.

**Created by:** Jeremy D. Miller (author of Marten, Jasper, StructureMap/Lamar)

**Key Features:**

### 1. Message Handling
```csharp
// Handler discovery via convention
public class OrderHandler
{
    // Wolverine finds this automatically
    public void Handle(PlaceOrder command)
    {
        // Handle command
    }

    // Can return events
    public OrderPlaced Handle(PlaceOrder command)
    {
        return new OrderPlaced(command.OrderId);
    }
}
```

### 2. Transactional Outbox Pattern
```csharp
// Messages stored in DB, then published (exactly-once)
public async Task Handle(PlaceOrder command, IMessageContext context)
{
    // Save to DB + enqueue message in same transaction
    await context.Publish(new OrderPlaced(command.OrderId));
}
```

### 3. Saga Support (Stateful Workflows)
```csharp
public class OrderSaga : Saga
{
    public string Id { get; set; }

    // Saga state
    public OrderState State { get; set; }

    public void Handle(OrderPlaced evt)
    {
        State = OrderState.Placed;
    }

    public void Handle(PaymentReceived evt)
    {
        State = OrderState.Paid;
    }
}
```

### 4. Durable Messaging
- Messages persisted before execution
- Automatic retry on failure
- Dead letter queue
- Scheduled/delayed messages

### 5. Integration
- **Marten** - Event sourcing integration
- **RabbitMQ** - External messaging
- **Azure Service Bus** - Cloud messaging
- **Kafka** - Event streaming

**Resources:**
- GitHub: https://github.com/JasperFx/wolverine
- Docs: https://wolverine.netlify.app/
- Sagas: https://wolverine.netlify.app/guide/durability/sagas.html

---

## Hybrid Architecture Design

### We Own (Core Domain)

✅ **Workflow orchestration logic**
- decide/evolve pattern
- Pure functions (no side effects)
- Business rules and decision making

✅ **State management and transitions**
- State rebuilding from events
- State validation
- State evolution

✅ **Workflow stream storage**
- Unified stream architecture (RFC Option C)
- WorkflowMessage with Kind/Direction
- Processed flag pattern

✅ **Command generation**
- What commands to issue
- When to issue them
- Business logic for decisions

### Wolverine Owns (Infrastructure)

✅ **Message delivery**
- Inbox/outbox tables
- Transactional guarantees
- Message routing

✅ **Command execution**
- Handler discovery
- Handler execution
- Result publishing

✅ **Queue integration**
- RabbitMQ, Azure Service Bus, Kafka
- Local queues for development
- External system integration

✅ **Retry/error handling**
- Automatic retry with backoff
- Dead letter queues
- Error tracking and monitoring

✅ **Durability guarantees**
- Exactly-once processing
- At-least-once delivery
- Transactional outbox

### Comparison Table

| Aspect | Our Implementation | Wolverine |
|--------|-------------------|-----------|
| **Architecture** | Pure functions (decide/evolve) | OOP (class-based handlers) |
| **State Management** | Explicit state types | Mutable saga properties |
| **Commands** | Explicit return (`List<WorkflowCommand>`) | Implicit via `IMessageContext` |
| **Event Sourcing** | Custom stream storage | Marten integration |
| **Testing** | Pure functions (easy) | Requires Wolverine context |
| **Storage** | Custom (SQL/EventStoreDB) | Marten/PostgreSQL primary |
| **Learning Curve** | Pattern-based | Framework-based |

---

## Integration Points

### 1. Input Side: Wolverine → Our Workflow

**Purpose:** Wolverine reads from source streams and routes messages to workflow instances

```csharp
// Wolverine handler - processes events from source streams
public class WorkflowInputRouter
{
    private readonly IWorkflowPersistence _persistence;
    private readonly IMessageContext _wolverineContext;

    // Wolverine discovers and invokes this handler
    public async Task Handle(GuestCheckedOut evt)
    {
        // 1. Determine which workflow instance
        var workflowId = $"group-checkout-{evt.GroupCheckoutId}";

        // 2. Store in workflow stream (OUR persistence)
        await _persistence.AppendAsync(workflowId, new[]
        {
            new WorkflowMessage(
                WorkflowId: workflowId,
                Position: 0, // Will be assigned by persistence
                Kind: MessageKind.Event,
                Direction: MessageDirection.Input,
                Message: evt,
                Timestamp: DateTime.UtcNow,
                Processed: null // Events don't have Processed flag
            )
        });

        // 3. Trigger workflow processing (via Wolverine)
        await _wolverineContext.PublishAsync(
            new ProcessWorkflow(workflowId)
        );
    }
}
```

**Wolverine Configuration:**
```csharp
builder.Services.AddWolverine(opts =>
{
    // Subscribe to source event streams
    opts.PublishMessage<GuestCheckedOut>()
        .ToLocalQueue("workflow-input-router");

    opts.PublishMessage<GuestCheckoutFailed>()
        .ToLocalQueue("workflow-input-router");
});
```

### 2. Processing Side: Our Workflow Engine

**Purpose:** Process workflow inputs using our decide/evolve pattern

```csharp
// Triggered by Wolverine
public class WorkflowProcessor
{
    private readonly IWorkflowOrchestrator _orchestrator;
    private readonly IWorkflowPersistence _persistence;
    private readonly IMessageContext _wolverineContext;

    public async Task Handle(ProcessWorkflow command)
    {
        var workflowId = command.WorkflowId;

        // 1. Read workflow stream (OUR storage)
        var messages = await _persistence.ReadStreamAsync(workflowId);

        // 2. Rebuild state from events (OUR evolve function)
        var snapshot = RebuildState(messages);

        // 3. Get new input messages
        var newInputs = messages
            .Where(m => m.Direction == MessageDirection.Input)
            .Where(m => m.Position > snapshot.LastProcessedPosition);

        foreach (var input in newInputs)
        {
            // 4. Run orchestrator (OUR pure logic)
            var result = _orchestrator.Process(
                workflow,
                snapshot,
                (TInput)input.Message,
                begins: false
            );

            // 5. Store output events in workflow stream (OUR storage)
            await _persistence.AppendAsync(workflowId, result.OutputMessages);

            // 6. Send commands to Wolverine outbox
            foreach (var cmd in result.Commands)
            {
                await PublishCommandToWolverine(cmd, workflowId);
            }

            snapshot = result.NewSnapshot;
        }
    }

    private async Task PublishCommandToWolverine(
        WorkflowCommand<TOutput> cmd,
        string workflowId)
    {
        switch (cmd)
        {
            case Send<TOutput> send:
                // Wolverine handles delivery + retry + DLQ
                await _wolverineContext.SendAsync(send.Message);

                // Mark as processed in OUR stream
                await _persistence.MarkCommandProcessedAsync(
                    workflowId,
                    send.Position
                );
                break;

            case Publish<TOutput> publish:
                await _wolverineContext.PublishAsync(publish.Message);
                break;

            case Schedule<TOutput> schedule:
                await _wolverineContext.ScheduleAsync(
                    schedule.Message,
                    schedule.After
                );
                break;

            case Reply<TOutput> reply:
                // Could use Wolverine's request/response
                await _wolverineContext.RespondToSenderAsync(reply.Message);
                break;
        }
    }
}
```

### 3. Output Side: Wolverine Executes Commands

**Purpose:** Execute commands via Wolverine handlers

```csharp
// Wolverine command handler (domain handler)
public class CheckOutHandler
{
    private readonly IGuestRepository _repository;

    // Wolverine finds and executes this automatically
    public async Task<GuestCheckedOut> Handle(CheckOut command)
    {
        var guest = await _repository.GetAsync(command.GuestId);

        // Execute business logic
        guest.CheckOut();

        await _repository.SaveAsync(guest);

        // Return event - Wolverine publishes it automatically
        return new GuestCheckedOut(
            command.GuestId,
            command.GroupCheckoutId
        );
    }
}
```

**What Wolverine handles:**
- ✅ Storing command in outbox (transactional)
- ✅ Executing handler
- ✅ Publishing result event
- ✅ Retry on failure (with backoff)
- ✅ Dead letter on exhaustion

---

## Complete Flow Example

### Step-by-Step: Group Checkout Workflow

```
┌─────────────────────────────────────────────────────────────────┐
│ 1. Guest checks out (external event)                            │
└────────────────────────────────┬────────────────────────────────┘
                                 ▼
┌─────────────────────────────────────────────────────────────────┐
│ 2. Wolverine Inbox                                              │
│    - Stores GuestCheckedOut event                               │
│    - Ensures exactly-once delivery                              │
└────────────────────────────────┬────────────────────────────────┘
                                 ▼
┌─────────────────────────────────────────────────────────────────┐
│ 3. WorkflowInputRouter (Wolverine Handler)                      │
│    - Determines workflow ID: "group-checkout-123"               │
│    - Stores event in OUR workflow stream                        │
│    - Publishes ProcessWorkflow command to Wolverine             │
└────────────────────────────────┬────────────────────────────────┘
                                 ▼
┌─────────────────────────────────────────────────────────────────┐
│ 4. Wolverine Inbox                                              │
│    - Receives ProcessWorkflow command                           │
└────────────────────────────────┬────────────────────────────────┘
                                 ▼
┌─────────────────────────────────────────────────────────────────┐
│ 5. WorkflowProcessor (Wolverine Handler, OUR Code)              │
│    - Reads OUR workflow stream                                  │
│    - Rebuilds state via OUR evolve function                     │
│    - Calls OUR decide function                                  │
│    - Gets commands: [Send(CheckOut(guest-2))]                   │
│    - Stores in OUR stream (Processed = false)                   │
│    - Publishes commands to Wolverine outbox                     │
└────────────────────────────────┬────────────────────────────────┘
                                 ▼
┌─────────────────────────────────────────────────────────────────┐
│ 6. Wolverine Outbox                                             │
│    - Stores CheckOut commands transactionally                   │
│    - Background agent processes outbox                          │
└────────────────────────────────┬────────────────────────────────┘
                                 ▼
┌─────────────────────────────────────────────────────────────────┐
│ 7. CheckOutHandler (Wolverine Handler)                          │
│    - Executes CheckOut command                                  │
│    - Updates guest aggregate                                    │
│    - Returns GuestCheckedOut event                              │
└────────────────────────────────┬────────────────────────────────┘
                                 ▼
┌─────────────────────────────────────────────────────────────────┐
│ 8. Wolverine publishes GuestCheckedOut                          │
│    - Back to step 2 (loop until workflow completes)             │
└─────────────────────────────────────────────────────────────────┘
```

### Detailed Message Flow

**Initial State:**
```
Workflow Stream (group-checkout-123):
Position | Kind    | Direction | Message                    | Processed
---------|---------|-----------|----------------------------|----------
1        | Command | Input     | InitiateGroupCheckout      | N/A
2        | Event   | Output    | GroupCheckoutInitiated     | N/A
3        | Command | Output    | CheckOut(guest-1)          | false
4        | Command | Output    | CheckOut(guest-2)          | false
```

**After guest-1 checks out:**
```
Source Event:
  GuestCheckedOut { GuestId: "guest-1", GroupCheckoutId: "group-123" }
    ↓
Wolverine Inbox:
  wolverine_incoming_envelopes
    id: abc-123
    status: 'Incoming'
    body: GuestCheckedOut {...}
    ↓
WorkflowInputRouter stores in OUR stream:
Position | Kind    | Direction | Message                    | Processed
---------|---------|-----------|----------------------------|----------
...
5        | Event   | Input     | GuestCheckedOut(guest-1)   | N/A
    ↓
WorkflowProcessor:
  - Reads positions 1-5
  - Rebuilds state via evolve
  - Calls decide → No new commands (waiting for guest-2)
  - Stores any output events
```

**After guest-2 checks out:**
```
Source Event:
  GuestCheckedOut { GuestId: "guest-2", GroupCheckoutId: "group-123" }
    ↓
Stored in OUR stream:
Position | Kind    | Direction | Message                    | Processed
---------|---------|-----------|----------------------------|----------
...
6        | Event   | Input     | GuestCheckedOut(guest-2)   | N/A
    ↓
WorkflowProcessor:
  - Reads positions 1-6
  - Rebuilds state via evolve
  - Calls decide → [Complete, Publish(GroupCheckoutCompleted)]
  - Stores output events and commands
Position | Kind    | Direction | Message                         | Processed
---------|---------|-----------|--------------------------------|----------
...
7        | Event   | Output    | GroupCheckoutCompleted         | N/A
8        | Command | Output    | Publish(GroupCheckoutCompleted)| false
    ↓
Wolverine Outbox:
  wolverine_outgoing_envelopes
    id: xyz-789
    destination: 'domain-events'
    body: GroupCheckoutCompleted {...}
    ↓
Wolverine publishes to RabbitMQ/Azure Service Bus/etc.
```

---

## Benefits Analysis

### ✅ Best of Both Worlds

| We Keep | Wolverine Provides |
|---------|-------------------|
| Pure decide/evolve pattern | Message durability |
| Custom workflow storage | Inbox/outbox tables |
| Full control over state | Retry/error handling |
| Processed flag pattern | Queue integration |
| Our innovation | Battle-tested infrastructure |
| Learning value | Production readiness |
| Business logic purity | Infrastructure complexity |

### ✅ Separation of Concerns

**Pure Business Logic (No Infrastructure Concerns):**
```csharp
public IReadOnlyList<WorkflowCommand<Output>> Decide(Input input, State state)
{
    return (input, state) switch
    {
        (InitiateGroupCheckout m, NotExisting) => [
            Send(new CheckOut(m.GuestIds[0])),
            Send(new CheckOut(m.GuestIds[1]))
        ],

        (GuestCheckedOut m, Pending p) when AllGuestsProcessed(p) => [
            Complete(),
            Publish(new GroupCheckoutCompleted(...))
        ],

        // ... pure business logic only, no:
        // - Queue names
        // - Retry policies
        // - Connection strings
        // - Error handling
        // - Serialization
    };
}
```

**Infrastructure (Wolverine Handles):**
- Where to send commands
- How to retry failures
- When to give up (DLQ)
- Idempotency keys
- Transaction boundaries
- Message serialization
- Queue configuration
- Error monitoring

### ✅ Easy Testing

**Test OUR Logic (No Wolverine Needed):**
```csharp
[Fact]
public void Decide_InitiateGroupCheckout_GeneratesCheckOutCommands()
{
    // Arrange
    var state = new NotExisting();
    var input = new InitiateGroupCheckout(
        GroupId: "123",
        GuestIds: ["g1", "g2"]
    );

    // Act
    var commands = workflow.Decide(input, state);

    // Assert - Pure function testing
    commands.Should().HaveCount(2);
    commands[0].Should().BeOfType<Send<CheckOut>>();
    commands[0].As<Send<CheckOut>>().Message.GuestId.Should().Be("g1");
}
```

**Test Integration (With Wolverine):**
```csharp
[Fact]
public async Task Integration_WorkflowProcessesGuestCheckout()
{
    // Arrange
    var host = await Host.CreateDefaultBuilder()
        .UseWolverine()
        .StartAsync();

    // Act
    await host.InvokeMessageAndWaitAsync(
        new GuestCheckedOut("g1", "group-123")
    );

    // Assert - Full integration test
    var workflowState = await _persistence.ReadStreamAsync("group-checkout-123");
    workflowState.Should().ContainEvent<GuestCheckedOut>();
}
```

### ✅ Incremental Adoption

Start simple, add complexity as needed:

**Phase 1: Local Queues Only**
```csharp
builder.Services.AddWolverine(opts =>
{
    // Everything in-process
    opts.PublishAllMessages().ToLocalQueue("workflows");
});
```

**Phase 2: Add RabbitMQ**
```csharp
builder.Services.AddWolverine(opts =>
{
    opts.UseRabbitMq("rabbitmq://localhost")
        .AutoProvision();

    opts.PublishMessage<CheckOut>()
        .ToRabbitExchange("checkout-commands");
});
```

**Phase 3: Add Azure Service Bus**
```csharp
builder.Services.AddWolverine(opts =>
{
    opts.UseAzureServiceBus("connection-string");

    opts.PublishMessage<GroupCheckoutCompleted>()
        .ToAzureServiceBusQueue("completed-checkouts");
});
```

**Phase 4: Add Kafka**
```csharp
builder.Services.AddWolverine(opts =>
{
    opts.UseKafka("kafka://localhost:9092");

    opts.PublishMessage<GroupCheckoutCompleted>()
        .ToKafkaTopic("domain-events");
});
```

### ✅ Production Features Out-of-the-Box

**What we DON'T have to build:**

1. **Retry Logic**
```csharp
// Wolverine provides automatically
opts.OnException<SqlException>()
    .RetryTimes(3)
    .WithMaximumDelay(TimeSpan.FromSeconds(10));
```

2. **Dead Letter Queue**
```csharp
// Wolverine provides
opts.OnException<Exception>()
    .MoveToErrorQueue();
```

3. **Scheduled Messages**
```csharp
// Wolverine handles
await context.ScheduleAsync(
    new TimeoutGroupCheckout(),
    TimeSpan.FromMinutes(30)
);
```

4. **Message Serialization**
```csharp
// Wolverine handles JSON/System.Text.Json/Newtonsoft
```

5. **Idempotency**
```csharp
// Wolverine tracks message IDs
```

6. **Telemetry**
```csharp
// Wolverine integrates with OpenTelemetry
opts.Services.AddOpenTelemetry();
```

### ✅ Avoid Reinventing Wheels

**What we were going to build:**

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

**What Wolverine provides:**

- ✅ All of the above, battle-tested in production

---

## Challenges and Solutions

### Challenge 1: Two Sources of Truth

**Problem:**
- OUR workflow stream: `workflow_messages` table
- Wolverine inbox/outbox: `wolverine_incoming_envelopes`, `wolverine_outgoing_envelopes`

**Solution:** Keep them separate but coordinated

```csharp
// OUR stream = business logic history + state
workflow_messages (
    workflow_id,
    position,
    kind,          // Command | Event
    direction,     // Input | Output
    message,
    processed      // OUR tracking
)

// Wolverine tables = message delivery infrastructure
wolverine_incoming_envelopes (
    id,
    status,        // Wolverine's tracking
    destination,
    body,
    attempts
)

wolverine_outgoing_envelopes (
    id,
    destination,
    body,
    attempts,
    status
)
```

**They serve different purposes:**

| Our Stream | Wolverine Tables |
|-----------|-----------------|
| **Purpose:** Business logic audit trail | **Purpose:** Message delivery guarantees |
| **Contains:** Workflow decisions and events | **Contains:** Messages in transit |
| **Query:** "What did the workflow decide?" | **Query:** "Was message delivered?" |
| **Lifetime:** Permanent (audit trail) | **Lifetime:** Until delivered successfully |
| **Concerns:** Domain logic | **Concerns:** Infrastructure |

### Challenge 2: Processed Flag Duplication?

**Question:** Do we need `Processed` flag if Wolverine has outbox?

**Answer:** YES - They track different things

**Option A: Keep Both (Recommended)**

```csharp
// OUR stream tracks command DECISION
workflow_messages.processed = true
// Meaning: "Workflow decided to send this command"

// Wolverine tracks command DELIVERY
wolverine_outgoing_envelopes.status = 'Sent'
// Meaning: "Message delivered to queue successfully"
```

**Why both?**

```
Scenario: Command decided but delivery failed

OUR stream:
Position | Message         | Processed
3        | CheckOut(g1)    | false      ← Workflow decided to send

Wolverine outbox:
id       | destination     | status
abc-123  | checkout-queue  | Failed     ← Delivery failed (will retry)

Result: We know workflow made decision, Wolverine retrying delivery
```

**Benefits:**
- ✅ OUR flag: Domain-level tracking (part of workflow state)
- ✅ Wolverine status: Infrastructure-level tracking (message lifecycle)
- ✅ Different concerns, different purposes
- ✅ Complete observability

**Alternative Option B: Remove Our Processed Flag**

```csharp
// Only Wolverine tracks execution
// Our stream just stores commands (no tracking)

// Simpler, but less visibility in workflow stream
```

**Downsides:**
- ❌ Can't query our stream for "what commands are pending?"
- ❌ Workflow stream less self-contained
- ❌ Must query Wolverine to understand workflow state

**Alternative Option C: Use Wolverine Outbox as Our Processed Flag**

```csharp
// Query Wolverine to see if command executed
var executed = await wolverineOutbox.IsExecutedAsync(commandId);
```

**Downsides:**
- ❌ Tight coupling to Wolverine
- ❌ Can't switch infrastructure later
- ❌ Harder to understand workflow state

**Recommendation:** **Option A** - Keep both flags for clear separation of concerns.

### Challenge 3: Transaction Boundaries

**Problem:** Our persistence + Wolverine persistence in same transaction?

**Option 1: Separate Transactions (Eventual Consistency)**

```csharp
// Transaction 1: Update OUR stream
await _persistence.AppendAsync(workflowId, outputMessages);

// Transaction 2: Publish to Wolverine
await _wolverineContext.SendAsync(command);

// Risk: Could crash between 1 and 2
// Result: Command decided but not published (need recovery)
```

**Option 2: Use Wolverine's Transactional Middleware**

```csharp
public class WorkflowProcessor
{
    // Wolverine wraps this in transaction
    [Transactional]
    public async Task Handle(ProcessWorkflow cmd, IDbConnection db)
    {
        // Use same connection/transaction for both
        await _persistence.AppendAsync(workflowId, messages, db);
        await _wolverineContext.SendAsync(command); // Same tx

        // Both commit or rollback together
    }
}
```

**Benefits:**
- ✅ Atomic: Both succeed or both fail
- ✅ No orphaned data

**Requirements:**
- Must use same database (PostgreSQL/SQL Server)
- Wolverine and our persistence share connection

**Option 3: Our Stream IS the Outbox (Recommended)**

```csharp
// Don't publish immediately to Wolverine
// Let background service read our stream and publish

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
                    cmd.WorkflowId,
                    cmd.Position
                );
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

**Trade-offs:**
- ⚠️ Slight delay (polling interval)
- ⚠️ Need background service

**Recommendation:** **Option 3** - Our stream as outbox with background publisher.

---

## Recommended Implementation

### Architecture Overview

```
┌─────────────────────────────────────────────────────────────────┐
│ 1. Source Events (RabbitMQ/Kafka/Event Store)                   │
└────────────────────────────────┬────────────────────────────────┘
                                 │
                                 ▼
┌─────────────────────────────────────────────────────────────────┐
│ 2. Wolverine Inbox                                              │
│    - Durably stores incoming messages                           │
│    - Ensures exactly-once delivery to handlers                  │
└────────────────────────────────┬────────────────────────────────┘
                                 │
                                 ▼
┌─────────────────────────────────────────────────────────────────┐
│ 3. WorkflowInputRouter (Wolverine Handler)                      │
│    - Determines workflow ID via GetWorkflowId()                 │
│    - Stores event in OUR workflow stream                        │
│    - Publishes ProcessWorkflow command                          │
└────────────────────────────────┬────────────────────────────────┘
                                 │
                                 ▼
┌─────────────────────────────────────────────────────────────────┐
│ 4. OUR Workflow Stream (PostgreSQL)                             │
│    workflow_messages table                                      │
│    - Stores all inputs, outputs, commands, events               │
│    - Processed flag tracks command execution                    │
└────────────────────────────────┬────────────────────────────────┘
                                 │
                                 ▼
┌─────────────────────────────────────────────────────────────────┐
│ 5. WorkflowProcessor (Wolverine Handler)                        │
│    - Reads OUR stream                                           │
│    - Rebuilds state via OUR evolve                              │
│    - Calls OUR decide                                           │
│    - Stores outputs in OUR stream (Processed = false)           │
└────────────────────────────────┬────────────────────────────────┘
                                 │
                                 ▼
┌─────────────────────────────────────────────────────────────────┐
│ 6. WorkflowOutboxPublisher (Background Service)                 │
│    - Polls OUR stream for pending commands                      │
│    - Publishes to Wolverine                                     │
│    - Marks as processed in OUR stream                           │
└────────────────────────────────┬────────────────────────────────┘
                                 │
                                 ▼
┌─────────────────────────────────────────────────────────────────┐
│ 7. Wolverine Outbox                                             │
│    - Stores commands for delivery                               │
│    - Background agent processes outbox                          │
│    - Retries on failure, DLQ on exhaustion                      │
└────────────────────────────────┬────────────────────────────────┘
                                 │
                                 ▼
┌─────────────────────────────────────────────────────────────────┐
│ 8. Command Handlers (Wolverine Handlers)                        │
│    - Execute domain logic                                       │
│    - Return events                                              │
│    - Wolverine publishes events automatically                   │
└────────────────────────────────┬────────────────────────────────┘
                                 │
                                 ▼
┌─────────────────────────────────────────────────────────────────┐
│ 9. Events Published (Back to Step 1)                            │
│    - Events flow back to source streams                         │
│    - Workflow continues until complete                          │
└─────────────────────────────────────────────────────────────────┘
```

### Implementation Code

#### Step 1: Wolverine Input Router

```csharp
// Handles events from source streams, routes to workflow instances
public class WorkflowInputRouter
{
    private readonly IWorkflowPersistence _persistence;
    private readonly IMessageContext _context;

    public async Task Handle(GuestCheckedOut evt)
    {
        // 1. Determine workflow instance
        var workflowId = $"group-checkout-{evt.GroupCheckoutId}";

        // 2. Store in OUR stream
        await _persistence.AppendAsync(workflowId, new[]
        {
            new WorkflowMessage(
                WorkflowId: workflowId,
                Kind: MessageKind.Event,
                Direction: MessageDirection.Input,
                Message: evt,
                Processed: null
            )
        });

        // 3. Trigger processing
        await _context.SendAsync(new ProcessWorkflow(workflowId));
    }

    // Handle other input events...
    public async Task Handle(GuestCheckoutFailed evt) { /* similar */ }
    public async Task Handle(TimeoutGroupCheckout evt) { /* similar */ }
}
```

#### Step 2: Workflow Processor

```csharp
// Processes workflow inputs using OUR decide/evolve pattern
public class WorkflowProcessor
{
    private readonly IWorkflowOrchestrator _orchestrator;
    private readonly IWorkflowPersistence _persistence;
    private readonly GroupCheckoutWorkflow _workflow;

    public async Task Handle(ProcessWorkflow command)
    {
        var workflowId = command.WorkflowId;

        // 1. Read OUR stream
        var messages = await _persistence.ReadStreamAsync(workflowId);

        // 2. Rebuild state via OUR evolve
        var snapshot = RebuildStateFromStream(messages);

        // 3. Get new inputs since last processing
        var newInputs = messages
            .Where(m => m.Direction == MessageDirection.Input)
            .Where(m => m.Position > snapshot.LastProcessedPosition)
            .ToList();

        foreach (var input in newInputs)
        {
            // 4. Run OUR orchestrator (pure logic)
            var result = _orchestrator.Process(
                _workflow,
                snapshot,
                (GroupCheckoutInputMessage)input.Message,
                begins: snapshot.State is GroupCheckoutState.NotExisting
            );

            // 5. Store outputs in OUR stream
            var outputMessages = ConvertToWorkflowMessages(
                workflowId,
                result.Commands,
                result.Events
            );

            await _persistence.AppendAsync(workflowId, outputMessages);

            snapshot = result.NewSnapshot;
        }
    }

    private WorkflowSnapshot RebuildStateFromStream(
        IReadOnlyList<WorkflowMessage> messages)
    {
        var state = _workflow.InitialState;
        var events = messages
            .Where(m => m.Kind == MessageKind.Event)
            .Select(m => (WorkflowEvent)m.Message);

        foreach (var evt in events)
        {
            state = _workflow.Evolve(state, evt);
        }

        var lastPosition = messages.Any() ? messages.Max(m => m.Position) : 0;

        return new WorkflowSnapshot(state, lastPosition);
    }
}
```

#### Step 3: Outbox Publisher (Background Service)

```csharp
// Reads pending commands from OUR stream and publishes to Wolverine
public class WorkflowOutboxPublisher : BackgroundService
{
    private readonly IWorkflowPersistence _persistence;
    private readonly IMessageContext _wolverineContext;
    private readonly ILogger<WorkflowOutboxPublisher> _logger;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // 1. Get pending commands from OUR stream
                var pending = await _persistence.GetPendingCommandsAsync();

                foreach (var cmd in pending)
                {
                    // 2. Publish to Wolverine (handles delivery)
                    await PublishCommandAsync(cmd.Message);

                    // 3. Mark as processed in OUR stream
                    var marked = await _persistence.MarkCommandProcessedAsync(
                        cmd.WorkflowId,
                        cmd.Position
                    );

                    if (marked)
                    {
                        _logger.LogInformation(
                            "Published command {Position} for workflow {WorkflowId}",
                            cmd.Position,
                            cmd.WorkflowId
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing workflow outbox");
            }

            // Poll interval
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }
    }

    private async Task PublishCommandAsync(object message)
    {
        // Wolverine handles routing based on message type
        await _wolverineContext.PublishAsync(message);
    }
}
```

#### Step 4: Command Handlers

```csharp
// Domain command handlers executed by Wolverine
public class CheckOutHandler
{
    private readonly IGuestRepository _repository;

    // Wolverine discovers and invokes this
    public async Task<GuestCheckedOut> Handle(CheckOut command)
    {
        var guest = await _repository.GetAsync(command.GuestId);

        // Idempotency check
        if (guest.Status == GuestStatus.CheckedOut)
        {
            // Already checked out - return existing result
            return new GuestCheckedOut(
                command.GuestId,
                command.GroupCheckoutId
            );
        }

        // Execute business logic
        guest.CheckOut();

        await _repository.SaveAsync(guest);

        // Return event - Wolverine publishes automatically
        return new GuestCheckedOut(
            command.GuestId,
            command.GroupCheckoutId
        );
    }
}
```

---

## Configuration Example

### Complete Startup Configuration

```csharp
var builder = WebApplication.CreateBuilder(args);

// 1. OUR Workflow Infrastructure
builder.Services.AddSingleton<IWorkflowPersistence, PostgreSQLWorkflowPersistence>();
builder.Services.AddSingleton<IWorkflowOrchestrator, WorkflowOrchestrator>();
builder.Services.AddSingleton<GroupCheckoutWorkflow>();

// 2. Background Services
builder.Services.AddHostedService<WorkflowOutboxPublisher>();

// 3. Wolverine for Messaging Infrastructure
builder.Services.AddWolverine(opts =>
{
    // Database for Wolverine's inbox/outbox
    opts.PersistMessagesWithPostgresql(
        builder.Configuration.GetConnectionString("Workflow"),
        "wolverine"
    );

    // Or use Marten
    // opts.UseMarten(builder.Configuration.GetConnectionString("Workflow"));

    // Input Side: Subscribe to source events
    opts.ListenToRabbitQueue("guest-events")
        .ProcessInline(); // WorkflowInputRouter handles these

    // Processing Side: Local queue for workflow processing
    opts.PublishMessage<ProcessWorkflow>()
        .ToLocalQueue("workflow-processor");

    // Output Side: Route commands to handlers
    opts.PublishMessage<CheckOut>()
        .ToLocalQueue("command-handlers");

    // External: Publish domain events to external systems
    opts.PublishMessage<GroupCheckoutCompleted>()
        .ToRabbitExchange("domain-events");

    opts.PublishMessage<GroupCheckoutFailed>()
        .ToRabbitExchange("domain-events");

    // Error Handling
    opts.OnException<SqlException>()
        .RetryTimes(3)
        .WithMaximumDelay(TimeSpan.FromSeconds(10));

    opts.OnException<Exception>()
        .MoveToErrorQueue();

    // Scheduled Messages
    opts.Durability.ScheduledJobPollingTime = TimeSpan.FromSeconds(5);

    // Telemetry
    opts.Services.AddOpenTelemetry();
});

// 4. Domain Services
builder.Services.AddScoped<IGuestRepository, GuestRepository>();

var app = builder.Build();

app.Run();
```

### Database Schema

**Our Workflow Tables:**
```sql
-- Workflow messages stream
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
    processed_at        TIMESTAMPTZ,

    PRIMARY KEY (workflow_id, position)
);

-- Index for pending commands query
CREATE INDEX idx_pending_commands
ON workflow_messages (workflow_id, processed)
WHERE kind = 'C' AND direction = 'O' AND processed = false;
```

**Wolverine Tables** (created automatically):
```sql
-- Wolverine incoming messages (inbox)
CREATE TABLE wolverine.wolverine_incoming_envelopes (
    id                  UUID        NOT NULL PRIMARY KEY,
    status              TEXT        NOT NULL,
    owner_id            INT         NOT NULL,
    execution_time      TIMESTAMPTZ,
    attempts            INT         NOT NULL DEFAULT 0,
    body                BYTEA       NOT NULL,
    message_type        TEXT        NOT NULL,
    received_at         TIMESTAMPTZ NOT NULL
);

-- Wolverine outgoing messages (outbox)
CREATE TABLE wolverine.wolverine_outgoing_envelopes (
    id                  UUID        NOT NULL PRIMARY KEY,
    owner_id            INT         NOT NULL,
    destination         TEXT        NOT NULL,
    deliver_by          TIMESTAMPTZ,
    body                BYTEA       NOT NULL,
    attempts            INT         NOT NULL DEFAULT 0,
    message_type        TEXT        NOT NULL
);

-- Wolverine dead letter queue
CREATE TABLE wolverine.wolverine_dead_letters (
    id                  UUID        NOT NULL PRIMARY KEY,
    execution_time      TIMESTAMPTZ,
    body                BYTEA       NOT NULL,
    message_type        TEXT        NOT NULL,
    exception_type      TEXT,
    exception_message   TEXT,
    sent_at             TIMESTAMPTZ NOT NULL
);
```

### Environment Configuration

```json
// appsettings.json
{
  "ConnectionStrings": {
    "Workflow": "Host=localhost;Database=workflow;Username=postgres;Password=***"
  },

  "Wolverine": {
    "Durability": {
      "Mode": "Solo",  // Or "Balanced" for multi-node
      "ScheduledJobPollingTime": "00:00:05"
    },

    "RabbitMq": {
      "ConnectionString": "rabbitmq://localhost",
      "AutoProvision": true,
      "AutoPurgeOnStartup": false
    }
  },

  "Workflow": {
    "OutboxPublisher": {
      "PollingInterval": "00:00:01",
      "BatchSize": 100
    }
  }
}
```

---

## Decision Summary

### ✅ **APPROVED: Use Wolverine as Infrastructure Layer**

**Rationale:**

1. **Leverage Strengths of Both**
   - OUR design: Workflow orchestration, state management, pure business logic
   - Wolverine: Message delivery, error handling, integrations, battle-tested infrastructure

2. **Avoid Reinventing Wheels**
   - Don't build inbox/outbox from scratch
   - Don't build retry/DLQ logic
   - Don't build queue integrations
   - Don't build message serialization

3. **Keep Our Innovation**
   - decide/evolve pattern remains ours
   - Processed flag still valuable (workflow-level tracking)
   - Unified stream architecture preserved
   - Complete control over business logic

4. **Production Ready Faster**
   - Wolverine handles edge cases we haven't thought of
   - Battle-tested in production systems
   - Great error handling out of the box
   - Active community and support

5. **Easy to Explain**
   - "We built the workflow orchestration engine"
   - "We use Wolverine for messaging infrastructure"
   - Clear separation of concerns
   - Easy to onboard new developers

### Implementation Plan

**Phase 1: Foundation (Weeks 1-2)**
- ✅ Install Wolverine NuGet packages
- ✅ Configure Wolverine with PostgreSQL persistence
- ✅ Set up local queues for development
- ✅ Implement WorkflowInputRouter
- ✅ Implement WorkflowProcessor
- ✅ Implement WorkflowOutboxPublisher

**Phase 2: Integration (Weeks 3-4)**
- ✅ Implement command handlers
- ✅ Wire up GroupCheckoutWorkflow
- ✅ End-to-end testing
- ✅ Error handling and DLQ
- ✅ Monitoring and telemetry

**Phase 3: Production Features (Weeks 5-6)**
- ✅ Add RabbitMQ integration
- ✅ Configure retry policies
- ✅ Set up health checks
- ✅ Performance testing
- ✅ Documentation

**Phase 4: Additional Workflows (Ongoing)**
- ✅ Implement OrderProcessingWorkflow
- ✅ Implement InventoryReservationWorkflow
- ✅ Implement DocumentApprovalWorkflow

### Success Criteria

- ✅ All existing tests pass (47 tests)
- ✅ GroupCheckoutWorkflow works end-to-end
- ✅ Messages delivered exactly-once
- ✅ Failed messages go to DLQ after retries
- ✅ Scheduled messages delivered at correct time
- ✅ Workflow state correctly rebuilt from stream
- ✅ Commands executed with proper error handling
- ✅ Telemetry shows full message flow

---

## References

**Wolverine:**
- GitHub: https://github.com/JasperFx/wolverine
- Docs: https://wolverine.netlify.app/
- Sagas: https://wolverine.netlify.app/guide/durability/sagas.html

**Our Documentation:**
- ARCHITECTURE.md - Unified stream architecture design
- IMPLEMENTATION_STATE.md - Current implementation status
- PROCESSED_FLAG_DESIGN_DECISION.md - Processed flag rationale
- PATTERNS.md - Reliability and Reply patterns

**External References:**
- RFC.txt - Oskar Dudycz's Emmett workflow RFC
- workflow.txt - Yves Reynhout's original Workflow Pattern
- ARCHITECTURE_EVENT_STORAGE.md - Emmett's storage implementation

---

**Last Updated:** 2025-11-23
**Decision Status:** ✅ APPROVED
**Next Steps:** Begin Phase 1 implementation
