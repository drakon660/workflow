namespace Workflow.InboxOutbox;

/// <summary>
/// Non-generic envelope for workflow inputs that Wolverine can route.
/// Contains the workflow identifier and the input message to be processed.
/// </summary>
public record WorkflowInputEnvelope
{
    /// <summary>
    /// Unique identifier for the workflow instance (e.g., "order-123")
    /// </summary>
    public required string WorkflowId { get; init; }

    /// <summary>
    /// Type key to resolve the correct workflow processor (e.g., "OrderProcessing")
    /// </summary>
    public required string WorkflowType { get; init; }

    /// <summary>
    /// The actual input message (runtime type determines the workflow action)
    /// </summary>
    public required object Input { get; init; }

    /// <summary>
    /// Optional correlation ID for tracing
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// When the message was created
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
