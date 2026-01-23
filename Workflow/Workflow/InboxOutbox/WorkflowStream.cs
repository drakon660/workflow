namespace Workflow.InboxOutbox;

public class WorkflowStream
{
    readonly SemaphoreSlim _lock = new(1);
    readonly List<WorkflowMessage> _messages = new();

    public WorkflowStream(string workflowId)
    {
        WorkflowId = workflowId;
        Created = DateTime.UtcNow;
    }

    public string WorkflowId { get; }
    public DateTime Created { get; }
    public long? LastProcessedSequence { get; set; }
    public long? LastDeliveredSequence { get; set; }

    public Task AcquireLock(CancellationToken ct) => _lock.WaitAsync(ct);
    public void ReleaseLock() => _lock.Release();

    public long Append(WorkflowMessage message)
    {
        lock (_messages)
        {
            message.SequenceNumber = _messages.Count + 1;
            message.RecordedTime = DateTime.UtcNow;
            _messages.Add(message);
            return message.SequenceNumber;
        }
    }

    public List<WorkflowMessage> GetEvents()
    {
        lock (_messages)
            return _messages.Where(m => m.Kind == MessageKind.Event).ToList();
    }

    public bool HasAnyEvents()
    {
        lock (_messages)
            return _messages.Any(m => m.Kind == MessageKind.Event);
    }

    public List<WorkflowMessage> GetPendingOutputs()
    {
        var lastDelivered = LastDeliveredSequence ?? 0;
        lock (_messages)
            return _messages
                .Where(m => m.Direction == MessageDirection.Output
                            && m.Kind == MessageKind.Command // Only deliver commands, not events
                            && m.SequenceNumber > lastDelivered
                            && !m.IsDelivered)
                .ToList();
    }

    public bool HasInput(Guid messageId)
    {
        lock (_messages)
            return _messages.Any(m => m.Direction == MessageDirection.Input && m.MessageId == messageId);
    }

    public List<WorkflowMessage> GetAllMessages()
    {
        lock (_messages)
            return _messages.ToList();
    }
}