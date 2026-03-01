namespace Workflow.Fluent;

/// <summary>
/// Captures the complete workflow definition as data - enables introspection and diagram generation.
/// </summary>
public class WorkflowDefinition<TInput, TState, TOutput> where TInput : IWorkflowInput
{
    public Type InitialStateType { get; internal set; }
    public List<StateDefinition<TInput, TState, TOutput>> States { get; } = [];

    public StateDefinition<TInput, TState, TOutput> GetOrCreateState(Type stateType, bool isInitial = false)
    {
        var state = States.FirstOrDefault(s => s.StateType == stateType);
        if (state == null)
        {
            state = new StateDefinition<TInput, TState, TOutput> { StateType = stateType, IsInitial = isInitial };
            States.Add(state);
        }
        if (isInitial)
        {
            state.IsInitial = true;
            InitialStateType = stateType;
        }
        return state;
    }
}

/// <summary>
/// Defines behavior for a specific state.
/// </summary>
public class StateDefinition<TInput, TState, TOutput> where TInput : IWorkflowInput
{
    public Type StateType { get; init; }
    public bool IsInitial { get; set; }
    public List<TransitionDefinition<TInput, TState, TOutput>> Transitions { get; } = [];
}

/// <summary>
/// Defines a transition: when a message arrives, what to do.
/// </summary>
public class TransitionDefinition<TInput, TState, TOutput> where TInput : IWorkflowInput
{
    public Type MessageType { get; init; }
    public Type TargetStateType { get; set; }
    public bool Stay { get; set; } // If true, remain in current state

    /// <summary>
    /// The function that produces commands. Takes (state, message) and returns commands.
    /// </summary>
    public Func<TState, TInput, IReadOnlyList<WorkflowCommand<TOutput>>> CommandsFactory { get; set; }

    /// <summary>
    /// Optional condition for this transition.
    /// </summary>
    public Func<TState, TInput, bool> Condition { get; set; }

    /// <summary>
    /// Description of the condition for diagram generation.
    /// </summary>
    public string ConditionDescription { get; set; }

    /// <summary>
    /// Else branch if condition is false.
    /// </summary>
    public TransitionDefinition<TInput, TState, TOutput> ElseBranch { get; set; }
}
