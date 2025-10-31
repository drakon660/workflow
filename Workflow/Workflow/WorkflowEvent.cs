namespace Workflow;

public abstract record WorkflowEvent<TInput, TOutput>;
public record Began<TInput, TOutput> : WorkflowEvent<TInput, TOutput>;
public record InitiatedBy<TInput, TOutput>(TInput Message) : WorkflowEvent<TInput, TOutput>;
public record Received<TInput, TOutput>(TInput Message) : WorkflowEvent<TInput, TOutput>;
public record Replied<TInput, TOutput>(TOutput Message) : WorkflowEvent<TInput, TOutput>;
public record Sent<TInput, TOutput>(TOutput Message) : WorkflowEvent<TInput, TOutput>;
public record Published<TInput, TOutput>(TOutput Message) : WorkflowEvent<TInput, TOutput>;
public record Scheduled<TInput, TOutput>(TimeSpan After, TOutput Message) : WorkflowEvent<TInput, TOutput>;
public record Completed<TInput, TOutput> : WorkflowEvent<TInput, TOutput>;