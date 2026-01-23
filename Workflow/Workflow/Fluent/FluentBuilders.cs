namespace Workflow.Fluent;

/// <summary>
/// Builder for configuring transitions from a specific state.
/// </summary>
public class StateBuilder<TInput, TState, TOutput>
{
    private readonly WorkflowDefinition<TInput, TState, TOutput> _definition;
    private readonly StateDefinition<TInput, TState, TOutput> _stateDefinition;

    internal StateBuilder(WorkflowDefinition<TInput, TState, TOutput> definition, StateDefinition<TInput, TState, TOutput> stateDefinition)
    {
        _definition = definition;
        _stateDefinition = stateDefinition;
    }

    /// <summary>
    /// Defines what happens when a specific message type is received in this state.
    /// </summary>
    public TransitionBuilder<TInput, TState, TOutput, TMessage> On<TMessage>() where TMessage : TInput
    {
        var transition = new TransitionDefinition<TInput, TState, TOutput>
        {
            MessageType = typeof(TMessage)
        };
        _stateDefinition.Transitions.Add(transition);
        return new TransitionBuilder<TInput, TState, TOutput, TMessage>(_definition, _stateDefinition, transition);
    }
}

/// <summary>
/// Builder for configuring a specific transition.
/// </summary>
public class TransitionBuilder<TInput, TState, TOutput, TMessage> where TMessage : TInput
{
    private readonly WorkflowDefinition<TInput, TState, TOutput> _definition;
    private readonly StateDefinition<TInput, TState, TOutput> _stateDefinition;
    private readonly TransitionDefinition<TInput, TState, TOutput> _transition;

    internal TransitionBuilder(
        WorkflowDefinition<TInput, TState, TOutput> definition,
        StateDefinition<TInput, TState, TOutput> stateDefinition,
        TransitionDefinition<TInput, TState, TOutput> transition)
    {
        _definition = definition;
        _stateDefinition = stateDefinition;
        _transition = transition;
    }

    /// <summary>
    /// Add a condition for this transition.
    /// </summary>
    public ConditionalTransitionBuilder<TInput, TState, TOutput, TMessage> If(
        Func<TransitionContext<TState, TMessage>, bool> condition,
        string description = null)
    {
        _transition.Condition = (state, input) => condition(new TransitionContext<TState, TMessage>(state, (TMessage)input));
        _transition.ConditionDescription = description;
        return new ConditionalTransitionBuilder<TInput, TState, TOutput, TMessage>(_definition, _stateDefinition, _transition);
    }

    /// <summary>
    /// Execute commands when this message is received.
    /// </summary>
    public TransitionBuilder<TInput, TState, TOutput, TMessage> Execute(
        Func<TransitionContext<TState, TMessage>, IEnumerable<WorkflowCommand<TOutput>>> commandsFactory)
    {
        _transition.CommandsFactory = (state, input) =>
            commandsFactory(new TransitionContext<TState, TMessage>(state, (TMessage)input)).ToList();
        return this;
    }

    /// <summary>
    /// Execute commands when this message is received (returns single command).
    /// </summary>
    public TransitionBuilder<TInput, TState, TOutput, TMessage> Execute(
        Func<TransitionContext<TState, TMessage>, WorkflowCommand<TOutput>> commandFactory)
    {
        _transition.CommandsFactory = (state, input) =>
            [commandFactory(new TransitionContext<TState, TMessage>(state, (TMessage)input))];
        return this;
    }

    /// <summary>
    /// No commands to execute (just transition).
    /// </summary>
    public TransitionBuilder<TInput, TState, TOutput, TMessage> DoNothing()
    {
        _transition.CommandsFactory = (_, _) => [];
        return this;
    }

    /// <summary>
    /// Transition to a new state.
    /// </summary>
    public StateBuilder<TInput, TState, TOutput> TransitionTo<TTargetState>() where TTargetState : TState
    {
        _transition.TargetStateType = typeof(TTargetState);
        _transition.Stay = false;
        _definition.GetOrCreateState(typeof(TTargetState));
        return new StateBuilder<TInput, TState, TOutput>(_definition, _stateDefinition);
    }

    /// <summary>
    /// Stay in the current state (no transition).
    /// </summary>
    public StateBuilder<TInput, TState, TOutput> Stay()
    {
        _transition.Stay = true;
        _transition.TargetStateType = _stateDefinition.StateType;
        return new StateBuilder<TInput, TState, TOutput>(_definition, _stateDefinition);
    }
}

/// <summary>
/// Builder for conditional transitions (after If()).
/// </summary>
public class ConditionalTransitionBuilder<TInput, TState, TOutput, TMessage> where TMessage : TInput
{
    private readonly WorkflowDefinition<TInput, TState, TOutput> _definition;
    private readonly StateDefinition<TInput, TState, TOutput> _stateDefinition;
    private readonly TransitionDefinition<TInput, TState, TOutput> _transition;

    internal ConditionalTransitionBuilder(
        WorkflowDefinition<TInput, TState, TOutput> definition,
        StateDefinition<TInput, TState, TOutput> stateDefinition,
        TransitionDefinition<TInput, TState, TOutput> transition)
    {
        _definition = definition;
        _stateDefinition = stateDefinition;
        _transition = transition;
    }

    /// <summary>
    /// Execute commands when condition is true.
    /// </summary>
    public ConditionalTransitionBuilder<TInput, TState, TOutput, TMessage> Execute(
        Func<TransitionContext<TState, TMessage>, IEnumerable<WorkflowCommand<TOutput>>> commandsFactory)
    {
        _transition.CommandsFactory = (state, input) =>
            commandsFactory(new TransitionContext<TState, TMessage>(state, (TMessage)input)).ToList();
        return this;
    }

    /// <summary>
    /// Transition to a new state when condition is true.
    /// </summary>
    public ElseBuilder<TInput, TState, TOutput, TMessage> TransitionTo<TTargetState>() where TTargetState : TState
    {
        _transition.TargetStateType = typeof(TTargetState);
        _transition.Stay = false;
        _definition.GetOrCreateState(typeof(TTargetState));
        return new ElseBuilder<TInput, TState, TOutput, TMessage>(_definition, _stateDefinition, _transition);
    }

    /// <summary>
    /// Stay in current state when condition is true.
    /// </summary>
    public ElseBuilder<TInput, TState, TOutput, TMessage> Stay()
    {
        _transition.Stay = true;
        _transition.TargetStateType = _stateDefinition.StateType;
        return new ElseBuilder<TInput, TState, TOutput, TMessage>(_definition, _stateDefinition, _transition);
    }
}

/// <summary>
/// Builder for the else branch of a conditional transition.
/// </summary>
public class ElseBuilder<TInput, TState, TOutput, TMessage> where TMessage : TInput
{
    private readonly WorkflowDefinition<TInput, TState, TOutput> _definition;
    private readonly StateDefinition<TInput, TState, TOutput> _stateDefinition;
    private readonly TransitionDefinition<TInput, TState, TOutput> _transition;

    internal ElseBuilder(
        WorkflowDefinition<TInput, TState, TOutput> definition,
        StateDefinition<TInput, TState, TOutput> stateDefinition,
        TransitionDefinition<TInput, TState, TOutput> transition)
    {
        _definition = definition;
        _stateDefinition = stateDefinition;
        _transition = transition;
    }

    /// <summary>
    /// Define the else branch behavior.
    /// </summary>
    public ElseTransitionBuilder<TInput, TState, TOutput, TMessage> Else()
    {
        var elseBranch = new TransitionDefinition<TInput, TState, TOutput>
        {
            MessageType = _transition.MessageType
        };
        _transition.ElseBranch = elseBranch;
        return new ElseTransitionBuilder<TInput, TState, TOutput, TMessage>(_definition, _stateDefinition, elseBranch);
    }

    /// <summary>
    /// Continue configuring the same state (no else branch needed).
    /// </summary>
    public StateBuilder<TInput, TState, TOutput> Done()
    {
        return new StateBuilder<TInput, TState, TOutput>(_definition, _stateDefinition);
    }
}

/// <summary>
/// Builder for the else branch transition.
/// </summary>
public class ElseTransitionBuilder<TInput, TState, TOutput, TMessage> where TMessage : TInput
{
    private readonly WorkflowDefinition<TInput, TState, TOutput> _definition;
    private readonly StateDefinition<TInput, TState, TOutput> _stateDefinition;
    private readonly TransitionDefinition<TInput, TState, TOutput> _transition;

    internal ElseTransitionBuilder(
        WorkflowDefinition<TInput, TState, TOutput> definition,
        StateDefinition<TInput, TState, TOutput> stateDefinition,
        TransitionDefinition<TInput, TState, TOutput> transition)
    {
        _definition = definition;
        _stateDefinition = stateDefinition;
        _transition = transition;
    }

    /// <summary>
    /// Execute commands in else branch.
    /// </summary>
    public ElseTransitionBuilder<TInput, TState, TOutput, TMessage> Execute(
        Func<TransitionContext<TState, TMessage>, IEnumerable<WorkflowCommand<TOutput>>> commandsFactory)
    {
        _transition.CommandsFactory = (state, input) =>
            commandsFactory(new TransitionContext<TState, TMessage>(state, (TMessage)input)).ToList();
        return this;
    }

    /// <summary>
    /// No commands in else branch.
    /// </summary>
    public ElseTransitionBuilder<TInput, TState, TOutput, TMessage> DoNothing()
    {
        _transition.CommandsFactory = (_, _) => [];
        return this;
    }

    /// <summary>
    /// Transition to a new state in else branch.
    /// </summary>
    public StateBuilder<TInput, TState, TOutput> TransitionTo<TTargetState>() where TTargetState : TState
    {
        _transition.TargetStateType = typeof(TTargetState);
        _transition.Stay = false;
        _definition.GetOrCreateState(typeof(TTargetState));
        return new StateBuilder<TInput, TState, TOutput>(_definition, _stateDefinition);
    }

    /// <summary>
    /// Stay in current state in else branch.
    /// </summary>
    public StateBuilder<TInput, TState, TOutput> Stay()
    {
        _transition.Stay = true;
        _transition.TargetStateType = _stateDefinition.StateType;
        return new StateBuilder<TInput, TState, TOutput>(_definition, _stateDefinition);
    }
}

/// <summary>
/// Context passed to Execute and If lambdas.
/// </summary>
public record TransitionContext<TState, TMessage>(TState State, TMessage Message);
