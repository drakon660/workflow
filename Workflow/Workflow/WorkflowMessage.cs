namespace Workflow;

/// <summary>
/// Represents a message in the unified workflow stream.
/// Can be either a command (instruction to execute) or an event (fact that happened).
/// Can be either input (received) or output (produced by workflow).
///
/// RFC: This unified model allows the workflow stream to serve as both inbox and outbox,
/// providing complete observability and durability in a single stream.
/// </summary>
public record WorkflowMessage<TInput, TOutput>(
    /// <summary>
    /// The workflow instance this message belongs to.
    /// Used for routing and querying.
    /// </summary>
    string WorkflowId,

    /// <summary>
    /// Position in the stream (1-based sequence number).
    /// Used for ordering and checkpoint tracking.
    /// </summary>
    long Position,

    /// <summary>
    /// Whether this message is a Command (instruction) or Event (fact).
    /// Commands need to be executed by output processors.
    /// Events are used to rebuild state via Evolve.
    /// </summary>
    MessageKind Kind,

    /// <summary>
    /// Whether this is an Input (received by workflow) or Output (produced by workflow).
    /// </summary>
    MessageDirection Direction,

    /// <summary>
    /// The actual message payload.
    /// Can be TInput (for Input direction) or TOutput (for Output direction).
    /// Can also be a WorkflowEvent for audit events (Began, Completed, Sent, etc.).
    /// </summary>
    object Message,

    /// <summary>
    /// When this message was recorded.
    /// </summary>
    DateTime Timestamp,

    /// <summary>
    /// Whether this command has been executed (only relevant for Kind=Command, Direction=Output).
    /// Events don't need processing, so this is always null for Kind=Event.
    /// </summary>
    bool? Processed = null
)
{
    /// <summary>
    /// Helper to check if this is an unprocessed output command that needs execution.
    /// </summary>
    public bool IsPendingCommand =>
        Kind == MessageKind.Command &&
        Direction == MessageDirection.Output &&
        Processed == false;

    /// <summary>
    /// Helper to check if this message should be used for state evolution.
    /// Only events (both input and output) evolve state.
    /// </summary>
    public bool IsEventForStateEvolution =>
        Kind == MessageKind.Event;
}

/// <summary>
/// Discriminates between commands (instructions to execute) and events (facts that happened).
/// </summary>
public enum MessageKind
{
    /// <summary>
    /// An instruction to perform an action (Send, Publish, Schedule, etc.).
    /// Must be executed by an output processor.
    /// </summary>
    Command,

    /// <summary>
    /// A fact about something that happened.
    /// Used to rebuild workflow state via Evolve.
    /// </summary>
    Event
}

/// <summary>
/// Indicates whether a message is flowing into or out of the workflow.
/// </summary>
public enum MessageDirection
{
    /// <summary>
    /// Message received by the workflow (inbox).
    /// Triggers workflow processing via Decide.
    /// </summary>
    Input,

    /// <summary>
    /// Message produced by the workflow (outbox).
    /// Result of processing via Decide/Translate.
    /// </summary>
    Output
}
