using Microsoft.Extensions.Logging;

namespace Workflow.InboxOutbox;

/// <summary>
/// Generic Wolverine handler that processes workflow inputs from WorkflowInputEnvelope.
/// Routes to the appropriate WorkflowProcessor based on the WorkflowType.
///
/// This enables a single Wolverine local queue to handle all workflow types.
/// </summary>
public class WorkflowInputHandler
{
    private readonly IWorkflowProcessorFactory _processorFactory;
    private readonly ILogger<WorkflowInputHandler> _logger;

    public WorkflowInputHandler(
        IWorkflowProcessorFactory processorFactory,
        ILogger<WorkflowInputHandler> logger)
    {
        _processorFactory = processorFactory;
        _logger = logger;
    }

    /// <summary>
    /// Handles incoming workflow input envelopes by routing to the appropriate processor.
    /// Wolverine automatically handles retries and error handling.
    /// </summary>
    public async Task Handle(WorkflowInputEnvelope envelope, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Processing workflow input: WorkflowType={WorkflowType}, WorkflowId={WorkflowId}, InputType={InputType}",
            envelope.WorkflowType,
            envelope.WorkflowId,
            envelope.Input.GetType().Name);

        try
        {
            await _processorFactory.ProcessAsync(
                envelope.WorkflowType,
                envelope.WorkflowId,
                envelope.Input,
                cancellationToken);

            _logger.LogInformation(
                "Successfully processed workflow input: WorkflowId={WorkflowId}",
                envelope.WorkflowId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to process workflow input: WorkflowType={WorkflowType}, WorkflowId={WorkflowId}",
                envelope.WorkflowType,
                envelope.WorkflowId);
            throw; // Re-throw to let Wolverine handle retry/error queue
        }
    }
}
