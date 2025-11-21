namespace Workflow;

public record WorkflowSnapshot<TInput, TState, TOutput>(
    TState State,
    IReadOnlyList<WorkflowEvent<TInput, TOutput>> EventHistory
);

public record OrchestrationResult<TInput, TState, TOutput>(
    WorkflowSnapshot<TInput, TState, TOutput> Snapshot,
    IReadOnlyList<WorkflowCommand<TOutput>> Commands,
    IReadOnlyList<WorkflowEvent<TInput, TOutput>> Events
);

public class WorkflowOrchestrator<TInput, TState, TOutput>
{
    public OrchestrationResult<TInput, TState, TOutput> Run(IWorkflow<TInput, TState, TOutput> workflow,
        WorkflowSnapshot<TInput, TState, TOutput> snapshot,
        TInput message,
        bool begins = false)
    {
        // (a) Call Decide to determine what commands to execute
        var commands = workflow.Decide(message, snapshot.State);

        // (b) Translate commands into events
        var newEvents = workflow.Translate(begins, message, commands);

        // (c) Append translated events to event history
        var updatedEventHistory = snapshot.EventHistory
            .Concat(newEvents)
            .ToList();

        // (d) Evolve state by applying new events
        var newState = snapshot.State;
        foreach (var evt in newEvents)
        {
            newState = workflow.Evolve(newState, evt);
        }

        // (e) Create new snapshot ready for persistence
        var newSnapshot = new WorkflowSnapshot<TInput, TState, TOutput>(
            newState,
            updatedEventHistory
        );

        return new OrchestrationResult<TInput, TState, TOutput>(
            newSnapshot,
            commands,
            newEvents
        );
    }
    
    public WorkflowSnapshot<TInput, TState, TOutput> CreateInitialSnapshot(
        IWorkflow<TInput, TState, TOutput> workflow)
    {
        return new WorkflowSnapshot<TInput, TState, TOutput>(
            workflow.InitialState,
            []
        );
    }
}