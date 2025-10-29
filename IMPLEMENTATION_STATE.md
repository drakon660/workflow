# Workflow Implementation State

**Last Updated:** 2025-10-28

## Overview

This document captures the current state of our C# workflow implementation, based on the Decider Pattern and inspired by Emmett's workflow design (see `rfc.md`).

## Current Implementation Status

### ✅ Completed

#### 1. Core Workflow Framework

**Location:** `Workflow/Workflow/` (Library Project)

**Files:**
- `IWorkflow.cs` - Generic workflow interface
- `Workflow.cs` - Abstract base class with template method pattern
- `WorkflowCommand.cs` - Command pattern types (Reply, Send, Publish, Schedule, Complete)
- `WorkflowEvent.cs` - Event sourcing types (Began, InitiatedBy, Received, Sent, Published, Scheduled, Completed, Replied)
- `WorkflowOrchestrator.cs` - Placeholder for future orchestration

**Design Pattern:**
```csharp
public interface IWorkflow<TInput, TState, TOutput>
{
    TState InitialState { get; }
    TState Evolve(TState state, WorkflowEvent<TInput, TOutput> workflowEvent);
    IReadOnlyList<WorkflowCommand<TOutput>> Decide(TInput input, TState state);
    IReadOnlyList<WorkflowEvent<TInput, TOutput>> Translate(bool begins, TInput message, IReadOnlyList<WorkflowCommand<TOutput>> commands);
}
```

**Key Features:**
- **Template Method Pattern** in `Evolve()`: Base class handles generic events (Began, Sent, Published, etc.), delegates domain events to `InternalEvolve()`
- **Pure Functions**: All business logic is deterministic and testable
- **Immutable State**: State passed as parameters, never stored in workflow instance
- **Event Sourcing Ready**: Complete event history can be replayed to reconstruct state

#### 2. Domain Implementation

**Location:** `Workflow/Workflow.Tests/IssueFineForSpeedingViolationWorkflow.cs`

**Domain Types:**
- `State`: Initial, AwaitingSystemNumber, AwaitingManualIdentificationCode, Final
- `InputMessage`: PoliceReportPublished, TrafficFineSystemNumberGenerated, TrafficFineManualIdentificationCodeGenerated
- `OutputMessage`: GenerateTrafficFineSystemNumber, GenerateTrafficFineManualIdentificationCode, IssueTrafficFine
- `Offense`: SpeedingViolation, ParkingViolation

**Workflow Logic:**
```
Initial State
    ↓ PoliceReportPublished (SpeedingViolation)
AwaitingSystemNumber
    ↓ TrafficFineSystemNumberGenerated
AwaitingManualIdentificationCode
    ↓ TrafficFineManualIdentificationCodeGenerated
Final State
```

#### 3. Comprehensive Test Suite

**Location:** `Workflow/Workflow.Tests/WorkflowTests.cs`

**Tests (9 total, all passing):**
1. `Check_ParkingViolation_Is_Starting_From_Initial_State` - Initial state validation
2. `Check_ParkingViolation_Police_Report_Published` - Basic workflow step
3. `SpeedingViolation_Step1_Generates_Correct_Command` - Command generation validation
4. `SpeedingViolation_Step2_Generates_Correct_Command` - Mid-workflow command validation
5. `SpeedingViolation_Step3_Generates_Correct_Commands` - Final step with multiple commands
6. `SpeedingViolation_Full_Workflow_Happy_Path` - **Complete workflow with event store**
7. `ParkingViolation_Completes_Immediately` - Alternative workflow path
8. `Translate_Generates_Correct_Events_For_Begin` - Event translation for workflow start
9. `Translate_Generates_Correct_Events_For_Receive` - Event translation for workflow continuation

**Event Store Demonstration:**
The happy path test demonstrates event sourcing:
- Captures all 8 events in a list
- Validates event types and order
- Verifies event replay can reconstruct final state

```csharp
// Event sequence for speeding violation workflow
[0] Began
[1] InitiatedBy (PoliceReportPublished)
[2] Sent (GenerateTrafficFineSystemNumber)
[3] Received (TrafficFineSystemNumberGenerated)
[4] Sent (GenerateTrafficFineManualIdentificationCode)
[5] Received (TrafficFineManualIdentificationCodeGenerated)
[6] Sent (IssueTrafficFine)
[7] Completed
```

## Architecture Decisions

### 1. Framework/Domain Separation

**Decision:** Separate reusable framework from domain implementation

**Rationale:**
- Framework code (Workflow library) is domain-agnostic
- Domain types live in test project (could move to separate domain project)
- Easy to create new workflows without modifying framework
- NuGet-ready for distribution

### 2. Template Method Pattern for Event Handling

**Decision:** Base class handles generic events, delegates domain events to abstract method

**Implementation:**
```csharp
public TState Evolve(TState state, WorkflowEvent<TInput, TOutput> workflowEvent)
{
    return workflowEvent switch
    {
        // Base class handles generic events
        Began<TInput, TOutput> => state,
        Sent<TInput, TOutput> => state,
        Published<TInput, TOutput> => state,
        Scheduled<TInput, TOutput> => state,
        Replied<TInput, TOutput> => state,
        Completed<TInput, TOutput> => state,

        // Delegate domain events to concrete implementation
        _ => InternalEvolve(state, workflowEvent),
    };
}

protected abstract TState InternalEvolve(TState state, WorkflowEvent<TInput, TOutput> workflowEvent);
```

**Rationale:**
- DRY principle: Generic events handled once
- Foolproof: Concrete workflows can't forget to handle generic events
- Hollywood Principle: Framework calls implementation, not vice versa
- Clean separation: Domain code only contains business logic

### 3. Functional/Immutable Approach

**Decision:** State as parameters, not properties

**Previous Approach (Rejected):**
```csharp
public State CurrentState { get; private set; } // Mutable
public void Evolve(WorkflowEvent event) { ... }
```

**Current Approach:**
```csharp
public abstract TState InitialState { get; } // Metadata
public TState Evolve(TState state, WorkflowEvent event); // Pure function
```

**Rationale:**
- Matches F# design in codebase
- Thread-safe by default
- Event sourcing compatible (can replay)
- Easier to test (pure functions)
- Industry standard (Decider pattern)

**Reference:** See `WORKFLOW_REFACTORING_DISCUSSION.md` for detailed analysis

### 4. Event-First Design

**Decision:** All state changes must flow through events processed by `Evolve`

**Rationale:**
- Follows Decider pattern: "All events returned by decide must be processable by evolve"
- Complete audit trail
- Event replay capability
- Time travel debugging

## Comparison with Emmett RFC

### Pattern Alignment

| Aspect | Emmett (TypeScript) | Our C# Implementation | Status |
|--------|---------------------|----------------------|--------|
| Core Pattern | `decide`, `evolve`, `initialState` | `Decide`, `Evolve`, `InitialState` | ✅ Identical |
| Pure Functions | Yes | Yes | ✅ Identical |
| Event Sourcing | Yes | Yes | ✅ Identical |
| Immutable State | Yes | Yes | ✅ Identical |
| Testing | Pure function tests | Pure function tests | ✅ Identical |
| Commands/Events | Separate types | Separate types | ✅ Identical |

### What Emmett Adds (Infrastructure)

The RFC describes infrastructure we haven't built yet:

1. **Event Store Integration** (Lines 293-316 of RFC)
   - PostgreSQL/SQLite persistence
   - Stream-per-workflow-instance
   - Position tracking
   - Each workflow instance has one stream (inbox + outbox)

2. **Message Routing** (Lines 127-158)
   - `getWorkflowId` function to route messages to instances
   - Double-hop pattern for durability
   - Background processors

3. **Orchestration** (Lines 127-158)
   - Input message arrival handling
   - State rebuilding from stream
   - Output processing
   - Checkpoint management

4. **Delivery Guarantees** (Line 160)
   - At-least-once delivery by default
   - Exactly-once for transactional stores
   - Advisory locks for concurrency

5. **Observable Streams** (Lines 348-351)
   - Full workflow history in one stream
   - OpenTelemetry integration
   - Business process analytics

## Key Files Reference

### Framework Files
- `Workflow/Workflow/IWorkflow.cs` - Interface definition (lines 3-15)
- `Workflow/Workflow/Workflow.cs` - Base class with template method (lines 3-61)
- `Workflow/Workflow/WorkflowCommand.cs` - Command types (lines 3-8)
- `Workflow/Workflow/WorkflowEvent.cs` - Event types (lines 3-11)

### Domain Files
- `Workflow/Workflow.Tests/IssueFineForSpeedingViolationWorkflow.cs` - Concrete workflow (lines 3-85)
- `Workflow/Workflow.Tests/WorkflowTests.cs` - Test suite (lines 5-216)

### Documentation
- `WORKFLOW_REFACTORING_DISCUSSION.md` - Design decisions and evolution
- `rfc.md` - Emmett workflow RFC (inspiration)
- `IMPLEMENTATION_STATE.md` - This file

## Next Steps (Optional)

### 1. Workflow ID Routing

Add workflow instance identification:

```csharp
public interface IWorkflow<TInput, TState, TOutput>
{
    // Existing methods...

    string? GetWorkflowId(TInput input); // NEW: Route to workflow instance
}
```

**Use Case:** Multiple group checkouts running simultaneously, each with its own stream.

### 2. Event Store Implementation

Persist events to database:

```csharp
public class WorkflowEventStore<TInput, TOutput>
{
    public async Task AppendAsync(
        string workflowId,
        IReadOnlyList<WorkflowEvent<TInput, TOutput>> events);

    public async Task<IReadOnlyList<WorkflowEvent<TInput, TOutput>>> GetEventsAsync(
        string workflowId);

    public async Task<long> GetLastCheckpointAsync(string workflowId);
}
```

**Technologies:**
- PostgreSQL with JSONB columns
- Entity Framework Core
- Marten (event sourcing library for .NET)
- EventStoreDB

### 3. Complete WorkflowOrchestrator

Implement the processing loop:

```csharp
public class WorkflowOrchestrator<TInput, TState, TOutput>
{
    public async Task<TState> ProcessAsync(string workflowId, TInput input)
    {
        // 1. Load events from store
        var events = await _eventStore.GetEventsAsync(workflowId);

        // 2. Rebuild state by replaying events
        var state = _workflow.InitialState;
        foreach (var evt in events)
            state = _workflow.Evolve(state, evt);

        // 3. Make decision
        var commands = _workflow.Decide(input, state);

        // 4. Translate commands to events
        var newEvents = _workflow.Translate(
            begins: events.Count == 0,
            input,
            commands);

        // 5. Store new events
        await _eventStore.AppendAsync(workflowId, newEvents);

        // 6. Apply new events to get final state
        foreach (var evt in newEvents)
            state = _workflow.Evolve(state, evt);

        // 7. Process outputs (send commands, publish events)
        await _outputProcessor.ProcessAsync(commands);

        return state;
    }
}
```

### 4. Background Processing

Poll for new messages and process:

```csharp
public class WorkflowBackgroundProcessor : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // 1. Get unprocessed messages from event store
            var messages = await _eventStore.GetUnprocessedMessagesAsync();

            // 2. Process each message
            foreach (var message in messages)
            {
                var workflowId = _workflow.GetWorkflowId(message);
                await _orchestrator.ProcessAsync(workflowId, message);

                // 3. Update checkpoint
                await _eventStore.UpdateCheckpointAsync(workflowId, message.Position);
            }

            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }
}
```

### 5. Message Metadata

Add metadata to track workflow instance information:

```csharp
public record WorkflowMetadata(
    string WorkflowId,
    long Position,
    DateTime Timestamp,
    MessageDirection Direction // Input or Output
);

public abstract record WorkflowEvent<TInput, TOutput>
{
    public WorkflowMetadata? Metadata { get; init; }
}

public enum MessageDirection
{
    Input,
    Output
}
```

### 6. Concurrency Control

Prevent concurrent execution of same workflow instance:

```csharp
public class WorkflowConcurrencyGuard
{
    // PostgreSQL advisory locks
    public async Task<IAsyncDisposable> AcquireLockAsync(string workflowId)
    {
        await _connection.ExecuteAsync(
            "SELECT pg_advisory_lock(hashtext($1))",
            new { workflowId });

        return new WorkflowLock(_connection, workflowId);
    }
}
```

Or optimistic concurrency with expected version:

```csharp
public async Task AppendAsync(
    string workflowId,
    long expectedVersion,
    IReadOnlyList<WorkflowEvent<TInput, TOutput>> events)
{
    // Throws if version doesn't match
}
```

## Benefits Achieved

### 1. Event Sourcing
- ✅ Complete audit trail
- ✅ State reconstruction from events
- ✅ Time travel debugging capability
- ✅ No data loss (events are immutable facts)

### 2. Testability
- ✅ Pure functions (no side effects)
- ✅ No infrastructure dependencies for unit tests
- ✅ Easy to test all workflow paths
- ✅ Event replay verification

### 3. Maintainability
- ✅ Clear separation of concerns
- ✅ Framework/domain decoupling
- ✅ DRY principle (no boilerplate in concrete workflows)
- ✅ Self-documenting through types

### 4. Scalability (Ready for)
- ✅ Horizontal scaling (each workflow instance independent)
- ✅ Natural sharding by workflow ID
- ✅ No shared state between instances
- ✅ Thread-safe by design (immutable)

## Design Principles

1. **Pure Functions Over Side Effects** - Business logic is deterministic
2. **Events Over State** - State is derived from events, not stored directly
3. **Composition Over Inheritance** - Workflows compose commands/events
4. **Explicit Over Implicit** - All state transitions through events
5. **Framework Over Library** - Inversion of control (Hollywood Principle)

## References

### Internal Documents
- `WORKFLOW_REFACTORING_DISCUSSION.md` - Design evolution and decisions
- `rfc.md` - Emmett workflow RFC (TypeScript/Node.js inspiration)
- `workflow_part1.fs` - F# workflow implementation reference

### External Resources
1. **Decider Pattern (Canonical)**: https://thinkbeforecoding.com/post/2021/12/17/functional-event-sourcing-decider
2. **Decide-Evolve-React Pattern**: https://ismaelcelis.com/posts/decide-evolve-react-pattern-in-ruby/
3. **Functional Event Sourcing**: https://delta-base.com/docs/concepts/functional-event-sourcing-decider/
4. **Yves Reynhout's Workflow Pattern**: https://blog.bittacklr.be/the-workflow-pattern.html (Emmett's primary inspiration)
5. **Emmett Workflow PR**: https://github.com/event-driven-io/emmett/pull/256

## Build & Test Status

**Build:** ✅ Succeeds
**Tests:** ✅ 9/9 passing (27ms)
**Coverage:** Full workflow lifecycle + event replay

```bash
dotnet build Workflow/Workflow.sln
dotnet test Workflow/Workflow.sln
```

## Conclusion

We have successfully implemented the **core Decider Pattern** in C# with:
- Clean framework/domain separation
- Pure, testable business logic
- Event sourcing foundation
- Comprehensive test coverage

The implementation aligns perfectly with Emmett's workflow design philosophy. The infrastructure pieces (event store, orchestrator, background processing) are optional extensions that would enable production deployment with durability guarantees.

**Current State: Production-ready for in-memory workflows, foundation-ready for persistent workflows**
