# Workflow Refactoring Discussion - Event Sourcing & Decider Pattern

**Date**: 2025-10-24

## Original Question
Should state be stored as a property in the Workflow class, or should it be passed as a parameter and returned from `Evolve`?

## Research & Findings

### Key Articles Referenced
1. **Decider Pattern (Canonical)**: https://thinkbeforecoding.com/post/2021/12/17/functional-event-sourcing-decider
2. **Decide-Evolve-React Pattern**: https://ismaelcelis.com/posts/decide-evolve-react-pattern-in-ruby/
3. **Functional Event Sourcing**: https://delta-base.com/docs/concepts/functional-event-sourcing-decider/

### The Decider Pattern Structure

```fsharp
type Decider<'c,'e,'s> = {
    decide: 'c -> 's -> 'e list
    evolve: 's -> 'e -> 's
    initialState: 's
    isTerminal: 's -> bool
}
```

**Key Principle**: *"All events returned by `decide` must be processable by `evolve`"*

## Decision: Functional/Immutable Approach

### Why Functional?
1. ✅ Matches F# design in existing codebase
2. ✅ Immutable by default (thread-safe)
3. ✅ Better for event sourcing (can replay events)
4. ✅ Easier to test (pure functions)
5. ✅ Industry standard (Decider pattern)

### Architecture Decisions

#### Option 1: State in Workflow Class (REJECTED)
```csharp
public State CurrentState { get; private set; }
public void Evolve(WorkflowEvent event) { ... }
```
**Problems**: Mutable state, threading issues, can't replay events

#### Option 2: State as Parameters (CHOSEN)
```csharp
public abstract TState InitialState { get; }
public abstract TState Evolve(TState state, WorkflowEvent event);
public abstract IReadOnlyList<WorkflowCommand> Decide(TInput input, TState state);
```

## Event Handling Evolution

### Initial Problem
We had two types of events mixed together:
1. **Domain Events**: `InitiatedBy`, `Received` (change state)
2. **Effect Events**: `Began`, `Sent`, `Published`, `Completed` (don't change state)

### Question: Should Evolve Handle All Events?

**Initial Approach**: Filter events before calling Evolve
```csharp
var domainEvents = events.Where(e => e is InitiatedBy<,> or Received<,>);
foreach (var @event in domainEvents)
    state = workflow.Evolve(state, @event);
```
**Problem**: Awkward, error-prone, not following Decider pattern

### Final Solution: Base Class Handles Generic Events

Per Decider pattern: **Evolve must handle ALL events**

**Base Class** (Workflow/Workflow/IWorkflow.cs:29-42):
```csharp
public virtual TState Evolve(TState state, WorkflowEvent<TInput, TOutput> workflowEvent)
{
    // Handle generic events that don't change state (common to all workflows)
    return workflowEvent switch
    {
        Began<TInput, TOutput> => state,
        Sent<TInput, TOutput> => state,
        Published<TInput, TOutput> => state,
        Scheduled<TInput, TOutput> => state,
        Replied<TInput, TOutput> => state,
        Completed<TInput, TOutput> => state,
        _ => throw new InvalidOperationException($"{workflowEvent} not supported by {state}")
    };
}
```

**Concrete Workflow** (Workflow/Workflow/IWorkflow.cs:107-129):
```csharp
public override State Evolve(State state, WorkflowEvent<InputMessage, OutputMessage> workflowEvent)
{
    return (state, workflowEvent) switch
    {
        // Only domain-specific events
        (Initial, InitiatedBy<...> { Message: PoliceReportPublished m }) => ...,
        (AwaitingSystemNumber s, Received<...> e) when e.Message is TrafficFineSystemNumberGenerated m => ...,

        // Delegate generic events to base class
        _ => base.Evolve(state, workflowEvent)
    };
}
```

## Final Architecture

### Workflow Base Class
```csharp
public abstract class Workflow<TInput, TState, TOutput>
{
    // Metadata: starting state definition
    public abstract TState InitialState { get; }

    // Pure function: applies events to state
    public virtual TState Evolve(TState state, WorkflowEvent<TInput, TOutput> workflowEvent);

    // Pure function: makes decisions based on input and state
    public abstract IReadOnlyList<WorkflowCommand<TOutput>> Decide(TInput input, TState state);

    // Helper: converts commands to events for audit trail
    public IReadOnlyList<WorkflowEvent<TInput, TOutput>> Translate(bool begins, TInput message, ...);
}
```

### Usage Pattern
```csharp
var workflow = new IssueFineForSpeedingViolationWorkflow();
var state = workflow.InitialState;

// Process a message
var commands = workflow.Decide(message, state);
var events = workflow.Translate(true, message, commands);

// Apply all events
foreach (var @event in events)
    state = workflow.Evolve(state, @event);
```

## Benefits Achieved

1. ✅ **Immutable State**: Thread-safe, easier to reason about
2. ✅ **Pure Functions**: Testable, reproducible
3. ✅ **Event Sourcing**: Can replay events to reconstruct state
4. ✅ **DRY Principle**: Generic events handled once in base class
5. ✅ **Decider Pattern Compliant**: All events flow through Evolve
6. ✅ **Matches F# Design**: Consistent across languages
7. ✅ **Clean Concrete Workflows**: Only domain logic, no boilerplate

## Key Files Modified

1. **Workflow/Workflow/IWorkflow.cs**
   - Base class with `InitialState`, `Evolve`, `Decide`, `Translate`
   - Generic event handling in base `Evolve`
   - Concrete workflow delegates to base for generic events

2. **Workflow/Workflow.Tests/WorkflowTests.cs**
   - Tests use functional approach with state passing
   - No event filtering needed

## Tests Status
✅ All tests passing (2/2)

## Next Steps / Future Considerations

- Consider adding `IsTerminal` predicate (part of Decider pattern)
- Implement proper side-effects handler (React pattern)
- Add more comprehensive tests for full workflow lifecycle
- Consider separating DomainEvent from EffectEvent type hierarchies

## Notes

- `InitialState` is **metadata** (constant), not runtime state
- State is never stored in the workflow - always passed/returned
- Generic events (Began, Sent, etc.) are for audit trail, not state transitions
- Decider pattern enforces: decide returns events, evolve handles ALL events
