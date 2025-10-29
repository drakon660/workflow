namespace Workflow;

public interface IWorkflow<TInput, TState, TOutput>
{
    TState InitialState { get; }

    TState Evolve(TState state, WorkflowEvent<TInput, TOutput> workflowEvent);

    IReadOnlyList<WorkflowCommand<TOutput>> Decide(TInput input, TState state);

    IReadOnlyList<WorkflowEvent<TInput, TOutput>> Translate(
        bool begins,
        TInput message,
        IReadOnlyList<WorkflowCommand<TOutput>> commands);
}