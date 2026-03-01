using Wolverine;

namespace Workflow.InboxOutbox;

/// <summary>
/// Wolverine implementation of IWorkflowBus.
/// Wraps IMessageBus and creates WorkflowInputEnvelope internally.
/// WorkflowId is extracted from the input message via IWorkflowInput.
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
    public async Task SendAsync<TInput>(TInput input, CancellationToken cancellationToken = default) where TInput : IWorkflowInput
    {
        // Use runtime type, not generic type parameter, to support inheritance
        var workflowType = _typeRegistry.GetWorkflowType(input!.GetType());
        await SendAsync(workflowType, input, cancellationToken);
    }

    /// <summary>
    /// Send input to workflow with explicit workflow type.
    /// </summary>
    public async Task SendAsync<TInput>(string workflowType, TInput input, CancellationToken cancellationToken = default) where TInput : IWorkflowInput
    {
        var envelope = new WorkflowInputEnvelope
        {
            WorkflowId = input.WorkflowId,
            WorkflowType = workflowType,
            Input = input!,
            CorrelationId = input.WorkflowId
        };

        await _messageBus.SendAsync(envelope);
    }
}
