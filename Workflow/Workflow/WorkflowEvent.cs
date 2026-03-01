namespace Workflow;

public abstract record WorkflowEvent<TInput, TOutput> where TInput : IWorkflowInput;
public record Began<TInput, TOutput> : WorkflowEvent<TInput, TOutput> where TInput : IWorkflowInput;
public record InitiatedBy<TInput, TOutput>(TInput Message) : WorkflowEvent<TInput, TOutput> where TInput : IWorkflowInput;
public record Received<TInput, TOutput>(TInput Message) : WorkflowEvent<TInput, TOutput> where TInput : IWorkflowInput;

public record Replied<TInput, TOutput>(TOutput Message) : WorkflowEvent<TInput, TOutput> where TInput : IWorkflowInput;
public record Sent<TInput, TOutput>(TOutput Message) : WorkflowEvent<TInput, TOutput> where TInput : IWorkflowInput;
public record Published<TInput, TOutput>(TOutput Message) : WorkflowEvent<TInput, TOutput> where TInput : IWorkflowInput;
public record Scheduled<TInput, TOutput>(TimeSpan After, TOutput Message) : WorkflowEvent<TInput, TOutput> where TInput : IWorkflowInput;
public record Completed<TInput, TOutput> : WorkflowEvent<TInput, TOutput> where TInput : IWorkflowInput;