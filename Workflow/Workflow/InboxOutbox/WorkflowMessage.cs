namespace Workflow.InboxOutbox;

public enum MessageDirection { Input, Output }
public enum MessageKind { Command, Event }

public class WorkflowMessage
{
    public long SequenceNumber { get; set; }
    public Guid MessageId { get; set; }
    public MessageDirection Direction { get; set; }
    public MessageKind Kind { get; set; }

    /// <summary>
    /// The outer type (e.g., "Sent", "Send", "PlaceOrderInputMessage")
    /// </summary>
    public string MessageType { get; set; } = default!;

    /// <summary>
    /// For events/commands with inner messages, the type of the inner message
    /// (e.g., "ProcessPayment", "NotifyOrderPlaced")
    /// </summary>
    public string? InnerMessageType { get; set; }

    /// <summary>
    /// JSON body - for events/commands with messages, this is just the inner message body
    /// </summary>
    public string Body { get; set; } = default!;

    /// <summary>
    /// Additional data for complex types (e.g., TimeSpan for Schedule)
    /// </summary>
    public string? AdditionalData { get; set; }

    public DateTime RecordedTime { get; set; }
    public DateTime? DeliveredTime { get; set; }
    public string? DestinationAddress { get; set; }
    public Guid? CorrelationId { get; set; }
    public DateTime? ScheduledTime { get; set; }

    public bool IsDelivered => DeliveredTime.HasValue;

    // Cached deserialized body
    public object? DeserializedBody { get; set; }
}