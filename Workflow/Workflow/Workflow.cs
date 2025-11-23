namespace Workflow;

public abstract class WorkflowBase<TInput, TState, TOutput> 
{
    public abstract TState InitialState { get; }
    
    public TState Evolve(TState state, WorkflowEvent<TInput, TOutput> workflowEvent)
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
            
            _ => InternalEvolve(state, workflowEvent),
            
            //_ => throw new InvalidOperationException($"{workflowEvent} not supported by {state}")
        };
    }

    protected abstract TState InternalEvolve(TState state, WorkflowEvent<TInput, TOutput> workflowEvent);

    // Helper class for pattern matching - uses type aliases to base types
    protected static class Events
    {
        // These are ALIASES to the base types, not derived types
        // This allows pattern matching with Events.Received while actual instances are base types
        public static Received<TInput, TOutput> Received(TInput message) => new(message);
        public static InitiatedBy<TInput, TOutput> InitiatedBy(TInput message) => new(message);
    }

    // Helper methods for creating workflow events (shortcuts to avoid verbose constructors)
    // These create base types to ensure type assertions work in tests
    protected Began<TInput, TOutput> Began() => new();
    protected InitiatedBy<TInput, TOutput> InitiatedBy(TInput message) => new(message);
    protected Received<TInput, TOutput> Received(TInput message) => new(message);
    protected Replied<TInput, TOutput> Replied(TOutput message) => new(message);
    protected Sent<TInput, TOutput> Sent(TOutput message) => new(message);
    protected Published<TInput, TOutput> Published(TOutput message) => new(message);
    protected Scheduled<TInput, TOutput> Scheduled(TimeSpan after, TOutput message) => new(after, message);
    protected Completed<TInput, TOutput> Completed() => new();

    // Helper methods for creating workflow commands (shortcuts to avoid verbose constructors)
    protected Reply<TOutput> Reply(TOutput message) => new(message);
    protected Send<TOutput> Send(TOutput message) => new(message);
    protected Publish<TOutput> Publish(TOutput message) => new(message);
    protected Schedule<TOutput> Schedule(TimeSpan after, TOutput message) => new(after, message);
    protected Complete<TOutput> Complete() => new();

    public virtual IReadOnlyList<WorkflowEvent<TInput, TOutput>> Translate(
        bool begins,
        TInput message,
        IReadOnlyList<WorkflowCommand<TOutput>> commands)
    {
        var events = new List<WorkflowEvent<TInput, TOutput>>();

        if (begins)
        {
            events.Add(Began());
            events.Add(InitiatedBy(message));
        }
        else
        {
            events.Add(Received(message));
        }

        foreach (var command in commands)
        {
            events.Add(command switch
            {
                Reply<TOutput> r => Replied(r.Message),
                Send<TOutput> s => Sent(s.Message),
                Publish<TOutput> p => Published(p.Message),
                Schedule<TOutput> sc => Scheduled(sc.After, sc.Message),
                Complete<TOutput> => Completed(),
                _ => throw new InvalidOperationException($"Unknown command type: {command}")
            });
        }

        return events;
    }
}

public abstract class Workflow<TInput, TState, TOutput> : WorkflowBase<TInput, TState, TOutput>, IWorkflow<TInput, TState, TOutput>
{
    public abstract IReadOnlyList<WorkflowCommand<TOutput>> Decide(TInput input, TState state);
}

public abstract class AsyncWorkflow<TInput, TState, TOutput, TContext> : WorkflowBase<TInput, TState, TOutput>, IAsyncWorkflow<TInput, TState, TOutput, TContext>
{
    public abstract Task<IReadOnlyList<WorkflowCommand<TOutput>>> DecideAsync(TInput input, TState state, TContext service);
}

public interface IAsyncWorkflow<TInput, TState, TOutput, TContext> : IWorkflowBase<TInput, TState, TOutput>
{
    Task<IReadOnlyList<WorkflowCommand<TOutput>>> DecideAsync(TInput input, TState state, TContext service);
}

public interface IWorkflow<TInput, TState, TOutput> : IWorkflowBase<TInput, TState, TOutput>
{
    IReadOnlyList<WorkflowCommand<TOutput>> Decide(TInput input, TState state);
}

public interface IWorkflowBase<TInput, TState, TOutput>
{
    TState InitialState { get; }

    TState Evolve(TState state, WorkflowEvent<TInput, TOutput> workflowEvent);
    
    IReadOnlyList<WorkflowEvent<TInput, TOutput>> Translate(
        bool begins,
        TInput message,
        IReadOnlyList<WorkflowCommand<TOutput>> commands);
}