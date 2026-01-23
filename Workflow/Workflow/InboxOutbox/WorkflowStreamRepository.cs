namespace Workflow.InboxOutbox;

public class WorkflowStreamRepository
{
    readonly Dictionary<string, WorkflowStream> _streams = new();
    readonly SemaphoreSlim _lock = new(1);

    public async Task<WorkflowStream> GetOrCreate(string workflowId, CancellationToken ct)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_streams.TryGetValue(workflowId, out var stream))
            {
                stream = new WorkflowStream(workflowId);
                _streams[workflowId] = stream;
            }
            return stream;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<WorkflowStream> Lock(string workflowId, CancellationToken ct)
    {
        var stream = await GetOrCreate(workflowId, ct).ConfigureAwait(false);
        await stream.AcquireLock(ct).ConfigureAwait(false);
        return stream;
    }
}