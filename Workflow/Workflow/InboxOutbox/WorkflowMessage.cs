namespace Workflow.InboxOutbox;

public enum MessageDirection { Input, Output }
public enum MessageKind { Command, Event }


public class WorkflowMessage
{
    public long SequenceNumber { get; set; }
    public Guid MessageId { get; set; }
    public MessageDirection Direction { get; set; }
    public MessageKind Kind { get; set; }
    public string MessageType { get; set; } = default!;
    public string Body { get; set; } = default!;
    public DateTime RecordedTime { get; set; }
    public DateTime? DeliveredTime { get; set; }
    public string? DestinationAddress { get; set; }
    public Guid? CorrelationId { get; set; }
    public DateTime? ScheduledTime { get; set; }

    public bool IsDelivered => DeliveredTime.HasValue;

    // Cached deserialized body
    public object? DeserializedBody { get; set; }
}