namespace Workflow;

public abstract record WorkflowCommand<TOutput>;
public record Reply<TOutput>(TOutput Message) : WorkflowCommand<TOutput>;
public record Send<TOutput>(TOutput Message) : WorkflowCommand<TOutput>;
public record Publish<TOutput>(TOutput Message) : WorkflowCommand<TOutput>;
public record Schedule<TOutput>(TimeSpan After, TOutput Message) : WorkflowCommand<TOutput>;
public record Complete<TOutput> : WorkflowCommand<TOutput>;
