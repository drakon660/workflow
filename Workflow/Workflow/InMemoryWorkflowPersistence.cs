using System.Collections.Concurrent;

namespace Workflow;

/// <summary>
/// Simple in-memory implementation of IWorkflowPersistence with thread-safety.
/// Suitable for testing, development, and single-instance deployments.
///
/// Thread-safety:
/// - Uses ConcurrentDictionary for workflow stream storage
/// - Uses locks per workflow stream for atomic append operations
/// - Returns defensive copies to prevent external mutation
/// </summary>
public class InMemoryWorkflowPersistence<TInput, TState, TOutput>
    : IWorkflowPersistence<TInput, TState, TOutput>
{
    // Main storage: workflowId -> list of messages
    private readonly ConcurrentDictionary<string, WorkflowStream> _streams = new();

    // Lock objects per workflow for atomic operations
    private readonly ConcurrentDictionary<string, object> _locks = new();

    /// <summary>
    /// Appends messages to the workflow stream atomically.
    /// </summary>
    public Task<long> AppendAsync(
        string workflowId,
        IReadOnlyList<WorkflowMessage<TInput, TOutput>> messages)
    {
        if (string.IsNullOrWhiteSpace(workflowId))
            throw new ArgumentException("Workflow ID cannot be null or empty", nameof(workflowId));

        if (messages == null || messages.Count == 0)
            throw new ArgumentException("Messages cannot be null or empty", nameof(messages));

        var lockObj = _locks.GetOrAdd(workflowId, _ => new object());

        lock (lockObj)
        {
            var stream = _streams.GetOrAdd(workflowId, _ => new WorkflowStream());

            foreach (var message in messages)
            {
                var position = stream.NextPosition++;

                // Create a new message with the correct position
                var storedMessage = message with { Position = position };
                stream.Messages.Add(storedMessage);
            }

            return Task.FromResult(stream.NextPosition - 1);
        }
    }

    /// <summary>
    /// Reads all messages from the workflow stream starting from a position.
    /// </summary>
    public Task<IReadOnlyList<WorkflowMessage<TInput, TOutput>>> ReadStreamAsync(
        string workflowId,
        long fromPosition = 0)
    {
        if (string.IsNullOrWhiteSpace(workflowId))
            throw new ArgumentException("Workflow ID cannot be null or empty", nameof(workflowId));

        if (!_streams.TryGetValue(workflowId, out var stream))
        {
            return Task.FromResult<IReadOnlyList<WorkflowMessage<TInput, TOutput>>>(
                Array.Empty<WorkflowMessage<TInput, TOutput>>());
        }

        var lockObj = _locks.GetOrAdd(workflowId, _ => new object());

        lock (lockObj)
        {
            var messages = stream.Messages
                .Where(m => m.Position >= fromPosition)
                .OrderBy(m => m.Position)
                .ToList();

            return Task.FromResult<IReadOnlyList<WorkflowMessage<TInput, TOutput>>>(messages);
        }
    }

    /// <summary>
    /// Gets all unprocessed output commands across all workflow instances.
    /// </summary>
    public Task<IReadOnlyList<WorkflowMessage<TInput, TOutput>>> GetPendingCommandsAsync(
        string? workflowId = null)
    {
        var pendingCommands = new List<WorkflowMessage<TInput, TOutput>>();

        var workflowsToCheck = workflowId != null
            ? _streams.Where(kvp => kvp.Key == workflowId)
            : _streams;

        foreach (var kvp in workflowsToCheck)
        {
            var lockObj = _locks.GetOrAdd(kvp.Key, _ => new object());

            lock (lockObj)
            {
                var commands = kvp.Value.Messages
                    .Where(m => m.IsPendingCommand)
                    .OrderBy(m => m.Position)
                    .ToList();

                pendingCommands.AddRange(commands);
            }
        }

        return Task.FromResult<IReadOnlyList<WorkflowMessage<TInput, TOutput>>>(pendingCommands);
    }

    /// <summary>
    /// Marks an output command as processed after successful execution.
    /// </summary>
    public Task MarkCommandProcessedAsync(string workflowId, long position)
    {
        if (string.IsNullOrWhiteSpace(workflowId))
            throw new ArgumentException("Workflow ID cannot be null or empty", nameof(workflowId));

        if (!_streams.TryGetValue(workflowId, out var stream))
        {
            throw new InvalidOperationException($"Workflow {workflowId} not found");
        }

        var lockObj = _locks.GetOrAdd(workflowId, _ => new object());

        lock (lockObj)
        {
            var messageIndex = stream.Messages.FindIndex(m => m.Position == position);

            if (messageIndex == -1)
            {
                throw new InvalidOperationException(
                    $"Message at position {position} not found in workflow {workflowId}");
            }

            var message = stream.Messages[messageIndex];

            if (message.Kind != MessageKind.Command || message.Direction != MessageDirection.Output)
            {
                throw new InvalidOperationException(
                    $"Message at position {position} is not an output command");
            }

            // Update the message to mark it as processed
            stream.Messages[messageIndex] = message with { Processed = true };
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Checks if a workflow stream exists.
    /// </summary>
    public Task<bool> ExistsAsync(string workflowId)
    {
        if (string.IsNullOrWhiteSpace(workflowId))
            throw new ArgumentException("Workflow ID cannot be null or empty", nameof(workflowId));

        return Task.FromResult(_streams.ContainsKey(workflowId));
    }

    /// <summary>
    /// Deletes an entire workflow stream.
    /// </summary>
    public Task DeleteAsync(string workflowId)
    {
        if (string.IsNullOrWhiteSpace(workflowId))
            throw new ArgumentException("Workflow ID cannot be null or empty", nameof(workflowId));

        _streams.TryRemove(workflowId, out _);
        _locks.TryRemove(workflowId, out _);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Internal class representing a workflow stream.
    /// </summary>
    private class WorkflowStream
    {
        public List<WorkflowMessage<TInput, TOutput>> Messages { get; } = new();
        public long NextPosition { get; set; } = 1; // Positions start at 1
    }
}
