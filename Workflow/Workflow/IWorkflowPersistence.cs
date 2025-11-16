namespace Workflow;

/// <summary>
/// Interface for persisting workflow streams.
///
/// RFC: Each workflow instance has its own stream that serves as both inbox (inputs) and outbox (outputs).
/// The stream contains all messages (commands and events) with metadata (position, kind, direction).
///
/// State is rebuilt by replaying events through Evolve.
/// Commands are executed by output processors.
///
/// Implementations can use event stores (PostgreSQL, EventStoreDB), message stores, or other durable storage.
/// </summary>
public interface IWorkflowPersistence<TInput, TState, TOutput>
{
    /// <summary>
    /// Appends messages to the workflow stream in a single atomic operation.
    /// Returns the position of the last appended message.
    ///
    /// This is called after processing to store:
    /// - Input messages (Direction=Input) when they arrive
    /// - Output messages (Direction=Output) after Decide/Translate
    /// </summary>
    /// <param name="workflowId">Unique identifier for the workflow instance</param>
    /// <param name="messages">Messages to append (inputs, outputs, events, commands)</param>
    /// <returns>Position of the last appended message</returns>
    Task<long> AppendAsync(
        string workflowId,
        IReadOnlyList<WorkflowMessage<TInput, TOutput>> messages);

    /// <summary>
    /// Reads all messages from the workflow stream starting from a position.
    /// Used to rebuild state and process new messages.
    /// </summary>
    /// <param name="workflowId">Unique identifier for the workflow instance</param>
    /// <param name="fromPosition">Starting position (0 = from beginning)</param>
    /// <returns>All messages from the requested position onwards</returns>
    Task<IReadOnlyList<WorkflowMessage<TInput, TOutput>>> ReadStreamAsync(
        string workflowId,
        long fromPosition = 0);

    /// <summary>
    /// Gets all unprocessed output commands across all workflow instances (or for a specific one).
    /// Output processors poll this to find commands that need execution.
    ///
    /// Returns commands where:
    /// - Kind = Command
    /// - Direction = Output
    /// - Processed = false
    /// </summary>
    /// <param name="workflowId">Optional: filter to a specific workflow instance</param>
    /// <returns>Pending commands that need execution</returns>
    Task<IReadOnlyList<WorkflowMessage<TInput, TOutput>>> GetPendingCommandsAsync(
        string? workflowId = null);

    /// <summary>
    /// Marks an output command as processed after successful execution.
    /// Prevents duplicate execution if the processor crashes and restarts.
    /// </summary>
    /// <param name="workflowId">Workflow instance that owns this command</param>
    /// <param name="position">Position of the command in the stream</param>
    Task MarkCommandProcessedAsync(string workflowId, long position);

    /// <summary>
    /// Checks if a workflow stream exists.
    /// </summary>
    Task<bool> ExistsAsync(string workflowId);

    /// <summary>
    /// Deletes an entire workflow stream.
    /// Use with caution - this removes all history.
    /// </summary>
    Task DeleteAsync(string workflowId);
}
