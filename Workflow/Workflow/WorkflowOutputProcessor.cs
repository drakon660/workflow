namespace Workflow;

/// <summary>
/// Processes output commands from workflow streams by executing them through handlers.
///
/// RFC Flow (line 138, 157):
/// 1. Poll for pending commands (Kind=Command, Direction=Output, Processed=false)
/// 2. Execute each command through registered handlers
/// 3. Mark command as processed
/// 4. Repeat
///
/// This provides durability: Commands are persisted before execution.
/// If the processor crashes, unprocessed commands are retried on restart.
/// </summary>
public class WorkflowOutputProcessor<TInput, TState, TOutput>
{
    private readonly IWorkflowPersistence<TInput, TState, TOutput> _persistence;
    private readonly ICommandExecutor<TOutput> _executor;
    private readonly TimeSpan _pollingInterval;

    public WorkflowOutputProcessor(
        IWorkflowPersistence<TInput, TState, TOutput> persistence,
        ICommandExecutor<TOutput> executor,
        TimeSpan? pollingInterval = null)
    {
        _persistence = persistence;
        _executor = executor;
        _pollingInterval = pollingInterval ?? TimeSpan.FromSeconds(1);
    }

    /// <summary>
    /// Continuously polls for and processes pending commands.
    /// This would typically run as a BackgroundService in ASP.NET Core.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(cancellationToken);
                await Task.Delay(_pollingInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Graceful shutdown
                break;
            }
            catch (Exception ex)
            {
                // Log error and continue (would use ILogger in production)
                Console.Error.WriteLine($"Error processing commands: {ex.Message}");
                await Task.Delay(_pollingInterval, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Processes a single batch of pending commands.
    /// </summary>
    public async Task ProcessBatchAsync(CancellationToken cancellationToken = default)
    {
        // Step 1: Get all pending commands across all workflow instances
        var pendingCommands = await _persistence.GetPendingCommandsAsync();

        // Step 2: Process each command
        foreach (var message in pendingCommands)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                // Step 3: Execute the command through the handler
                await _executor.ExecuteAsync((TOutput)message.Message, cancellationToken);

                // Step 4: Mark as processed (prevents re-execution)
                await _persistence.MarkCommandProcessedAsync(
                    message.WorkflowId,
                    message.Position
                );
            }
            catch (Exception ex)
            {
                // Log error but continue processing other commands
                // In production, you might want retry logic, DLQ, etc.
                Console.Error.WriteLine(
                    $"Error executing command at position {message.Position} " +
                    $"for workflow {message.WorkflowId}: {ex.Message}"
                );
            }
        }
    }
}

/// <summary>
/// Interface for executing workflow output commands.
/// Implementations handle Send, Publish, Schedule, Reply, and Complete commands.
/// </summary>
public interface ICommandExecutor<TOutput>
{
    /// <summary>
    /// Executes a command (Send, Publish, Schedule, etc.).
    ///
    /// Examples:
    /// - Send: Send message to a specific destination (e.g., message bus, HTTP endpoint)
    /// - Publish: Publish event to subscribers (e.g., pub/sub, event bus)
    /// - Schedule: Schedule message for future delivery (e.g., scheduler service)
    /// - Reply: Send response back to caller (e.g., HTTP response, RPC reply)
    /// - Complete: Mark workflow as completed (e.g., update workflow registry)
    /// </summary>
    Task ExecuteAsync(TOutput command, CancellationToken cancellationToken = default);
}

/// <summary>
/// Example implementation that routes commands to specific handlers.
/// </summary>
public class CompositeCommandExecutor<TOutput> : ICommandExecutor<TOutput>
{
    private readonly IMessageBus? _messageBus;
    private readonly IScheduler? _scheduler;

    public CompositeCommandExecutor(
        IMessageBus? messageBus = null,
        IScheduler? scheduler = null)
    {
        _messageBus = messageBus;
        _scheduler = scheduler;
    }

    public async Task ExecuteAsync(TOutput command, CancellationToken cancellationToken = default)
    {
        // In practice, you'd inspect the command type or use pattern matching
        // For now, this is a placeholder showing the pattern

        if (command == null)
            throw new ArgumentNullException(nameof(command));

        var commandTypeName = command.GetType().Name;

        // Route based on command type or naming convention
        if (commandTypeName.StartsWith("Send") || commandTypeName.Contains("CheckOut"))
        {
            if (_messageBus == null)
                throw new InvalidOperationException("MessageBus not configured");

            await _messageBus.SendAsync(command, cancellationToken);
        }
        else if (commandTypeName.Contains("Schedule"))
        {
            if (_scheduler == null)
                throw new InvalidOperationException("Scheduler not configured");

            // Extract delay from command metadata (simplified)
            var delay = TimeSpan.FromMinutes(5);
            await _scheduler.ScheduleAsync(command, delay, cancellationToken);
        }
        else
        {
            // Default: log or ignore
            Console.WriteLine($"No handler for command type: {commandTypeName}");
        }
    }
}

/// <summary>
/// Interface for message bus operations (Send, Publish).
/// </summary>
public interface IMessageBus
{
    Task SendAsync<T>(T message, CancellationToken cancellationToken = default);
    Task PublishAsync<T>(T message, CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for scheduling delayed message delivery.
/// </summary>
public interface IScheduler
{
    Task ScheduleAsync<T>(T message, TimeSpan delay, CancellationToken cancellationToken = default);
}
