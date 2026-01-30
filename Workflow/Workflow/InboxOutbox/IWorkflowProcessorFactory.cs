using Microsoft.Extensions.DependencyInjection;

namespace Workflow.InboxOutbox;

/// <summary>
/// Factory interface for resolving workflow processors by type key.
/// Enables generic handling of workflow inputs through Wolverine.
/// </summary>
public interface IWorkflowProcessorFactory
{
    /// <summary>
    /// Process a workflow input using the appropriate processor.
    /// </summary>
    /// <param name="workflowType">Type key (e.g., "OrderProcessing")</param>
    /// <param name="workflowId">Workflow instance ID</param>
    /// <param name="input">The input message</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task ProcessAsync(string workflowType, string workflowId, object input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a processor is registered for the given workflow type.
    /// </summary>
    bool HasProcessor(string workflowType);
}

/// <summary>
/// Registration for a workflow processor.
/// </summary>
public interface IWorkflowProcessorRegistration
{
    string WorkflowType { get; }
    Type InputType { get; }
    Type StateType { get; }
    Type OutputType { get; }
    Task ProcessAsync(IServiceProvider serviceProvider, string workflowId, object input, CancellationToken cancellationToken);
}

/// <summary>
/// Typed registration for a specific workflow.
/// </summary>
public class WorkflowProcessorRegistration<TInput, TState, TOutput> : IWorkflowProcessorRegistration
{
    public string WorkflowType { get; }
    public Type InputType => typeof(TInput);
    public Type StateType => typeof(TState);
    public Type OutputType => typeof(TOutput);

    public WorkflowProcessorRegistration(string workflowType)
    {
        WorkflowType = workflowType;
    }

    public async Task ProcessAsync(IServiceProvider serviceProvider, string workflowId, object input, CancellationToken cancellationToken)
    {
        var processor = serviceProvider.GetRequiredService<WorkflowProcessor<TInput, TState, TOutput>>();
        await processor.ProcessAsync(workflowId, (TInput)input, cancellationToken);
    }
}

/// <summary>
/// Default implementation of the workflow processor factory.
/// </summary>
public class WorkflowProcessorFactory : IWorkflowProcessorFactory
{
    private readonly Dictionary<string, IWorkflowProcessorRegistration> _registrations = new(StringComparer.OrdinalIgnoreCase);
    private readonly IServiceProvider _serviceProvider;

    public WorkflowProcessorFactory(IServiceProvider serviceProvider, IEnumerable<IWorkflowProcessorRegistration> registrations)
    {
        _serviceProvider = serviceProvider;
        foreach (var registration in registrations)
        {
            _registrations[registration.WorkflowType] = registration;
        }
    }

    public bool HasProcessor(string workflowType) => _registrations.ContainsKey(workflowType);

    public async Task ProcessAsync(string workflowType, string workflowId, object input, CancellationToken cancellationToken = default)
    {
        if (!_registrations.TryGetValue(workflowType, out var registration))
        {
            throw new InvalidOperationException($"No workflow processor registered for type '{workflowType}'. " +
                $"Registered types: {string.Join(", ", _registrations.Keys)}");
        }

        // Create a scope for the processing
        using var scope = _serviceProvider.CreateScope();
        await registration.ProcessAsync(scope.ServiceProvider, workflowId, input, cancellationToken);
    }
}
