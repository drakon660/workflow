using Wolverine;

namespace Workflow.InboxOutbox;

/// <summary>
/// Wolverine implementation of IWorkflowBus.
/// Wraps IMessageBus and creates WorkflowInputEnvelope internally.
/// </summary>
public class WolverineWorkflowBus : IWorkflowBus
{
    private readonly IMessageBus _messageBus;
    private readonly IWorkflowTypeRegistry _typeRegistry;

    public WolverineWorkflowBus(IMessageBus messageBus, IWorkflowTypeRegistry typeRegistry)
    {
        _messageBus = messageBus;
        _typeRegistry = typeRegistry;
    }

    /// <summary>
    /// Send input to workflow, inferring workflow type from input's runtime type.
    /// Uses inheritance lookup - PlaceOrderInputMessage will match OrderProcessingInputMessage registration.
    /// </summary>
    public async Task SendAsync<TInput>(string workflowId, TInput input, CancellationToken cancellationToken = default)
    {
        // Use runtime type, not generic type parameter, to support inheritance
        var workflowType = _typeRegistry.GetWorkflowType(input!.GetType());
        await SendAsync(workflowType, workflowId, input, cancellationToken);
    }

    /// <summary>
    /// Send input to workflow with explicit workflow type.
    /// </summary>
    public async Task SendAsync<TInput>(string workflowType, string workflowId, TInput input, CancellationToken cancellationToken = default)
    {
        var envelope = new WorkflowInputEnvelope
        {
            WorkflowId = workflowId,
            WorkflowType = workflowType,
            Input = input!,
            CorrelationId = workflowId
        };

        await _messageBus.SendAsync(envelope);
    }
}
