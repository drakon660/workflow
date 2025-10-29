namespace Workflow;

public abstract class Workflow<TInput, TState, TOutput> : IWorkflow<TInput, TState, TOutput>
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

    public abstract IReadOnlyList<WorkflowCommand<TOutput>> Decide(TInput input, TState state);

    public IReadOnlyList<WorkflowEvent<TInput, TOutput>> Translate(
        bool begins,
        TInput message,
        IReadOnlyList<WorkflowCommand<TOutput>> commands)
    {
        var events = new List<WorkflowEvent<TInput, TOutput>>();

        if (begins)
        {
            events.Add(new Began<TInput, TOutput>());
            events.Add(new InitiatedBy<TInput, TOutput>(message));
        }
        else
        {
            events.Add(new Received<TInput, TOutput>(message));
        }

        foreach (var command in commands)
        {
            events.Add(command switch
            {
                Reply<TOutput> r => new Replied<TInput, TOutput>(r.Message),
                Send<TOutput> s => new Sent<TInput, TOutput>(s.Message),
                Publish<TOutput> p => new Published<TInput, TOutput>(p.Message),
                Schedule<TOutput> sc => new Scheduled<TInput, TOutput>(sc.After, sc.Message),
                Complete<TOutput> => new Completed<TInput, TOutput>(),
                _ => throw new InvalidOperationException($"Unknown command type: {command}")
            });
        }

        return events;
    }
}