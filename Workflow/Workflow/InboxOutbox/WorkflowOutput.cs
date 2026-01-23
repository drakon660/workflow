namespace Workflow.InboxOutbox;

public readonly struct WorkflowOutput
{
    public object Message { get; init; }
    public MessageKind Kind { get; init; }
    public string? Destination { get; init; }

    public static WorkflowOutput Command(object message, string destination)
        => new() { Message = message, Kind = MessageKind.Command, Destination = destination };

    public static WorkflowOutput Event(object message)
        => new() { Message = message, Kind = MessageKind.Event };
}

public class WorkflowProcessorOptions
{
    public string ProcessorId { get; set; } = default!;
    public int OutputDeliveryLimit { get; set; } = 100;
    public TimeSpan OutputDeliveryTimeout { get; set; } = TimeSpan.FromSeconds(30);
}