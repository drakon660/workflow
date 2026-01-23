# Workflow Diagrams

This directory contains Mermaid diagrams documenting the OrderProcessingWorkflow implementations.

## Diagram Types

### State Transition Diagrams (InternalEvolve)

These diagrams show how workflow states change in response to events:

- **01-OrderProcessingWorkflow-StateTransitions.md**: Synchronous workflow state transitions
- **02-OrderProcessingAsyncWorkflow-StateTransitions.md**: Async workflow with inventory checking

### Decision/Command Diagrams (Decide/DecideAsync)

These diagrams show what commands are generated based on input and state:

- **03-OrderProcessingWorkflow-DecisionTree.md**: Decision tree showing all command generation rules
- **04-OrderProcessingAsyncWorkflow-DecisionLogic.md**: Branching logic with inventory checks
- **05-OrderProcessing-CommandTypeMatrix.md**: Matrix showing which command types are used where

### Business Process Diagrams

- **06-OrderProcessing-BusinessProcessFlow.md**: High-level business process view with actions

## Key Concepts

### InternalEvolve (State Transitions)
- Shows **state changes** in response to events
- Pattern: `(CurrentState, Event) => NewState`
- Pure state machine diagram

### Decide/DecideAsync (Command Generation)
- Shows **commands generated** based on input and current state
- Pattern: `(Input, State) => Commands[]`
- Represents business logic decisions

## Viewing the Diagrams

These diagrams use Mermaid syntax and can be viewed in:
- GitHub (native Mermaid support)
- VS Code (with Mermaid extension)
- Any Mermaid-compatible markdown viewer

## Diagram Legend

### Colors Used

- **Light Blue** (#e1f5ff): Initial/start states or commands
- **Light Yellow** (#fff4e1): Intermediate states or query commands
- **Light Orange** (#ffd9b3): Waiting/pending states
- **Light Green** (#e1ffe1): Success path states or commands
- **Light Pink** (#ffe1f5): Shipping/delivery states
- **Green** (#90EE90): Terminal success states
- **Light Red** (#ffcccc): Cancellation/failure states
- **Yellow** (#ffeb99): Decision points

### Workflow Framework Events

- **Began**: Workflow start marker (no state change)
- **InitiatedBy**: First message that starts the workflow
- **Received**: Subsequent messages received by workflow
- **Sent/Published/Scheduled/Replied**: Output events (no state change)
- **Completed**: Workflow finished marker (no state change)
