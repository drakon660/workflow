using System.Text;

namespace Workflow.Fluent;

/// <summary>
/// Base class for fluent workflow definitions.
/// Provides a declarative API for defining state transitions and commands.
///
/// Usage:
/// <code>
/// public class MyWorkflow : FluentWorkflow&lt;MyInput, MyState, MyOutput&gt;
/// {
///     public MyWorkflow()
///     {
///         Initially&lt;InitialState&gt;()
///             .On&lt;StartMessage&gt;()
///             .Execute(ctx => [Send(new DoSomething())])
///             .TransitionTo&lt;ProcessingState&gt;();
///
///         During&lt;ProcessingState&gt;()
///             .On&lt;CompletedMessage&gt;()
///             .Execute(ctx => [Complete()])
///             .TransitionTo&lt;FinishedState&gt;();
///     }
/// }
/// </code>
/// </summary>
public abstract class FluentWorkflow<TInput, TState, TOutput> where TInput : IWorkflowInput
{
    /// <summary>
    /// The workflow definition containing all states and transitions.
    /// Can be inspected for diagram generation or validation.
    /// </summary>
    public WorkflowDefinition<TInput, TState, TOutput> Definition { get; } = new();

    /// <summary>
    /// Define behavior for the initial state.
    /// </summary>
    protected StateBuilder<TInput, TState, TOutput> Initially<TInitialState>() where TInitialState : TState
    {
        var stateDefinition = Definition.GetOrCreateState(typeof(TInitialState), isInitial: true);
        return new StateBuilder<TInput, TState, TOutput>(Definition, stateDefinition);
    }

    /// <summary>
    /// Define behavior while in a specific state.
    /// </summary>
    protected StateBuilder<TInput, TState, TOutput> During<TDuringState>() where TDuringState : TState
    {
        var stateDefinition = Definition.GetOrCreateState(typeof(TDuringState));
        return new StateBuilder<TInput, TState, TOutput>(Definition, stateDefinition);
    }

    #region Command Helpers

    protected static Send<TOutput> Send(TOutput message) => new(message);
    protected static Publish<TOutput> Publish(TOutput message) => new(message);
    protected static Schedule<TOutput> Schedule(TimeSpan delay, TOutput message) => new(delay, message);
    protected static Reply<TOutput> Reply(TOutput message) => new(message);
    protected static Complete<TOutput> Complete() => new();

    #endregion

    #region Workflow Execution

    /// <summary>
    /// Process an input message and return the commands to execute and new state.
    /// </summary>
    public (IReadOnlyList<WorkflowCommand<TOutput>> Commands, TState NewState) Process(TInput input, TState currentState)
    {
        var currentStateType = currentState.GetType();
        var inputType = input.GetType();

        // Find the state definition
        var stateDefinition = Definition.States.FirstOrDefault(s => s.StateType == currentStateType);
        if (stateDefinition == null)
        {
            return ([], currentState); // Unknown state, no commands
        }

        // Find matching transition
        var transition = stateDefinition.Transitions.FirstOrDefault(t => t.MessageType.IsAssignableFrom(inputType));
        if (transition == null)
        {
            return ([], currentState); // No transition for this message
        }

        // Check condition and get the right branch
        var activeTransition = GetActiveTransition(transition, currentState, input);

        // Execute commands
        var commands = activeTransition.CommandsFactory?.Invoke(currentState, input) ?? [];

        // Determine new state
        var newState = activeTransition.Stay
            ? currentState
            : CreateNewState(activeTransition.TargetStateType, currentState, input);

        return (commands, newState);
    }

    private TransitionDefinition<TInput, TState, TOutput> GetActiveTransition(
        TransitionDefinition<TInput, TState, TOutput> transition,
        TState state,
        TInput input)
    {
        // No condition - use this transition
        if (transition.Condition == null)
            return transition;

        // Check condition
        if (transition.Condition(state, input))
            return transition;

        // Condition false - use else branch if available
        return transition.ElseBranch ?? transition;
    }

    /// <summary>
    /// Override to customize state creation.
    /// Default implementation tries to create state using parameterless constructor.
    /// </summary>
    protected virtual TState CreateNewState(Type targetStateType, TState currentState, TInput input)
    {
        // Try parameterless constructor
        var instance = Activator.CreateInstance(targetStateType);
        return (TState)instance;
    }

    #endregion

    #region Diagram Generation

    /// <summary>
    /// Generate a Mermaid state diagram from the workflow definition.
    /// </summary>
    public string ToMermaidStateDiagram()
    {
        var sb = new StringBuilder();
        sb.AppendLine("```mermaid");
        sb.AppendLine("stateDiagram-v2");

        // Add initial state marker
        if (Definition.InitialStateType != null)
        {
            sb.AppendLine($"    [*] --> {Definition.InitialStateType.Name}");
        }

        // Add transitions
        foreach (var state in Definition.States)
        {
            foreach (var transition in state.Transitions)
            {
                var fromState = state.StateType.Name;
                var message = transition.MessageType.Name;

                if (transition.Condition != null && transition.ElseBranch != null)
                {
                    // Conditional transition
                    var toStateIf = transition.Stay ? fromState : transition.TargetStateType?.Name ?? fromState;
                    var toStateElse = transition.ElseBranch.Stay ? fromState : transition.ElseBranch.TargetStateType?.Name ?? fromState;
                    var condition = transition.ConditionDescription ?? "condition";

                    sb.AppendLine($"    {fromState} --> {toStateIf} : {message} [{condition}]");
                    if (toStateElse != toStateIf || toStateElse == fromState)
                    {
                        sb.AppendLine($"    {fromState} --> {toStateElse} : {message} [else]");
                    }
                }
                else
                {
                    var toState = transition.Stay ? fromState : transition.TargetStateType?.Name ?? fromState;
                    sb.AppendLine($"    {fromState} --> {toState} : {message}");
                }
            }
        }

        // Add terminal states
        var allTargets = Definition.States
            .SelectMany(s => s.Transitions)
            .Where(t => !t.Stay)
            .Select(t => t.TargetStateType)
            .Concat(Definition.States.SelectMany(s => s.Transitions)
                .Where(t => t.ElseBranch != null && !t.ElseBranch.Stay)
                .Select(t => t.ElseBranch.TargetStateType))
            .Where(t => t != null)
            .Distinct();

        var statesWithOutgoing = Definition.States.Select(s => s.StateType).ToHashSet();
        var terminalStates = allTargets.Where(t => !statesWithOutgoing.Contains(t));

        foreach (var terminal in terminalStates)
        {
            sb.AppendLine($"    {terminal.Name} --> [*]");
        }

        sb.AppendLine("```");
        return sb.ToString();
    }

    /// <summary>
    /// Generate a Mermaid flowchart showing state transitions.
    /// </summary>
    public string ToMermaidFlowchart()
    {
        var sb = new StringBuilder();
        sb.AppendLine("```mermaid");
        sb.AppendLine("flowchart TD");

        // Start node
        sb.AppendLine("    Start([Start])");
        if (Definition.InitialStateType != null)
        {
            var initialName = Definition.InitialStateType.Name;
            sb.AppendLine($"    Start --> {initialName}[{initialName}]");
        }

        // Add transitions
        foreach (var state in Definition.States)
        {
            var fromState = state.StateType.Name;

            foreach (var transition in state.Transitions)
            {
                var message = transition.MessageType.Name;

                if (transition.Condition != null && transition.ElseBranch != null)
                {
                    var toStateIf = transition.Stay ? fromState : transition.TargetStateType?.Name ?? fromState;
                    var toStateElse = transition.ElseBranch.Stay ? fromState : transition.ElseBranch.TargetStateType?.Name ?? fromState;
                    var condition = transition.ConditionDescription ?? "condition";

                    sb.AppendLine($"    {fromState} -->|{message}<br/>[{condition}]| {toStateIf}[{toStateIf}]");
                    if (toStateElse != fromState)
                    {
                        sb.AppendLine($"    {fromState} -->|{message}<br/>[else]| {toStateElse}[{toStateElse}]");
                    }
                }
                else
                {
                    var toState = transition.Stay ? fromState : transition.TargetStateType?.Name ?? fromState;
                    if (toState != fromState)
                    {
                        sb.AppendLine($"    {fromState} -->|{message}| {toState}[{toState}]");
                    }
                    else
                    {
                        sb.AppendLine($"    {fromState} -->|{message}| {fromState}");
                    }
                }
            }
        }

        // End node for terminal states
        var statesWithOutgoing = Definition.States.Select(s => s.StateType).ToHashSet();
        var allTargets = Definition.States
            .SelectMany(s => s.Transitions)
            .Where(t => !t.Stay && t.TargetStateType != null)
            .Select(t => t.TargetStateType)
            .Distinct();

        var terminalStates = allTargets.Where(t => !statesWithOutgoing.Contains(t)).ToList();

        if (terminalStates.Any())
        {
            sb.AppendLine("    End([End])");
            foreach (var terminal in terminalStates)
            {
                sb.AppendLine($"    {terminal.Name} --> End");
            }
        }

        sb.AppendLine("```");
        return sb.ToString();
    }

    #endregion
}
