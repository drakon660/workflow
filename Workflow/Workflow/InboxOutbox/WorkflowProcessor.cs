using System.Text.Json;
using System.Text.Json.Serialization;

namespace Workflow.InboxOutbox;

/// <summary>
/// Processes workflow inputs through the decide/evolve pattern with inbox/outbox storage.
/// Uses WorkflowOrchestrator for state management and event translation.
/// </summary>
public class WorkflowProcessor<TInput, TState, TOutput>
{
    // JsonSerializer options - NOTE: polymorphic properties (TInput/TOutput) require
    // [JsonPolymorphic] attributes on base types for full serialization.
    // DeserializedBody is used at runtime, Body is for persistence/recovery.
    static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    readonly WorkflowStreamRepository _repository;
    readonly IWorkflow<TInput, TState, TOutput> _workflow;
    readonly WorkflowOrchestrator<TInput, TState, TOutput> _orchestrator;
    readonly Func<WorkflowMessage, CancellationToken, Task>? _deliverOutput;
    readonly WorkflowProcessorOptions _options;

    public WorkflowProcessor(
        WorkflowStreamRepository repository,
        IWorkflow<TInput, TState, TOutput> workflow,
        Func<WorkflowMessage, CancellationToken, Task>? deliverOutput = null,
        WorkflowProcessorOptions? options = null)
    {
        _repository = repository;
        _workflow = workflow;
        _orchestrator = new WorkflowOrchestrator<TInput, TState, TOutput>();
        _deliverOutput = deliverOutput;
        _options = options ?? new WorkflowProcessorOptions();
    }

    /// <summary>
    /// Process an input message through the workflow.
    /// Routing/correlation is handled by the caller.
    /// </summary>
    public async Task ProcessAsync(string workflowId, TInput input, CancellationToken ct = default)
    {
        var stream = await _repository.Lock(workflowId, ct).ConfigureAwait(false);
        try
        {
            // Check if this is a new workflow (begins)
            var begins = !stream.HasAnyEvents();

            // Store input in inbox
            var inputMessage = StoreInput(stream, input);

            // Rebuild snapshot from stored events
            var snapshot = RebuildSnapshot(stream);

            // Run orchestrator: decide -> translate -> evolve
            var result = _orchestrator.Run(_workflow, snapshot, input, begins);

            // Store new events in stream (for state rebuild on next message)
            foreach (var evt in result.Events)
            {
                StoreEvent(stream, evt);
            }

            // Store commands in outbox (for delivery)
            foreach (var command in result.Commands)
            {
                StoreCommand(stream, command);
            }

            stream.LastProcessedSequence = inputMessage.SequenceNumber;

            // Deliver pending outputs
            if(_deliverOutput is not null)
                await DeliverOutputsAsync(stream, ct).ConfigureAwait(false);
        }
        finally
        {
            stream.ReleaseLock();
        }
    }

    /// <summary>
    /// Store input message in workflow stream (inbox).
    /// </summary>
    WorkflowMessage StoreInput(WorkflowStream stream, TInput input)
    {
        var message = new WorkflowMessage
        {
            MessageId = Guid.NewGuid(),
            Direction = MessageDirection.Input,
            Kind = MessageKind.Command, // Inputs are commands/messages
            MessageType = input!.GetType().AssemblyQualifiedName!,
            Body = JsonSerializer.Serialize(input, input.GetType(), SerializerOptions),
            DeserializedBody = input
        };

        stream.Append(message);
        return message;
    }

    /// <summary>
    /// Store event in workflow stream (for state rebuilding).
    /// Extracts inner message and serializes it with its runtime type.
    /// </summary>
    void StoreEvent(WorkflowStream stream, WorkflowEvent<TInput, TOutput> evt)
    {
        // Extract inner message and additional data based on event type
        var (innerMessage, innerMessageType, additionalData) = evt switch
        {
            InitiatedBy<TInput, TOutput> e => ((object?)e.Message, e.Message?.GetType(), (string?)null),
            Received<TInput, TOutput> e => (e.Message, e.Message?.GetType(), null),
            Sent<TInput, TOutput> e => (e.Message, e.Message?.GetType(), null),
            Replied<TInput, TOutput> e => (e.Message, e.Message?.GetType(), null),
            Published<TInput, TOutput> e => (e.Message, e.Message?.GetType(), null),
            Scheduled<TInput, TOutput> e => (e.Message, e.Message?.GetType(), e.After.ToString()),
            Began<TInput, TOutput> => (null, null, null),
            Completed<TInput, TOutput> => (null, null, null),
            _ => (null, null, null)
        };

        // Get simple event type name (e.g., "Sent" instead of full generic type)
        var eventTypeName = evt.GetType().Name;
        if (eventTypeName.Contains('`'))
            eventTypeName = eventTypeName[..eventTypeName.IndexOf('`')];

        var message = new WorkflowMessage
        {
            MessageId = Guid.NewGuid(),
            Direction = MessageDirection.Output,
            Kind = MessageKind.Event,
            MessageType = eventTypeName,
            InnerMessageType = innerMessageType?.AssemblyQualifiedName,
            Body = innerMessage != null
                ? JsonSerializer.Serialize(innerMessage, innerMessageType!, SerializerOptions)
                : "{}",
            AdditionalData = additionalData,
            DeserializedBody = evt
        };

        stream.Append(message);
    }

    /// <summary>
    /// Store command in workflow stream (outbox for delivery).
    /// Extracts inner message and serializes it with its runtime type.
    /// </summary>
    void StoreCommand(WorkflowStream stream, WorkflowCommand<TOutput> command)
    {
        // Extract inner message and additional data based on command type
        var (innerMessage, innerMessageType, additionalData, destination, scheduledTime) = command switch
        {
            Send<TOutput> c => ((object?)c.Message, c.Message?.GetType(), (string?)null, "send", (DateTime?)null),
            Reply<TOutput> c => (c.Message, c.Message?.GetType(), null, "reply", null),
            Publish<TOutput> c => (c.Message, c.Message?.GetType(), null, "publish", null),
            Schedule<TOutput> c => (c.Message, c.Message?.GetType(), c.After.ToString(), "schedule", DateTime.UtcNow.Add(c.After)),
            Complete<TOutput> => (null, null, null, "complete", null),
            _ => (null, null, null, "unknown", null)
        };

        // Get simple command type name (e.g., "Send" instead of full generic type)
        var commandTypeName = command.GetType().Name;
        if (commandTypeName.Contains('`'))
            commandTypeName = commandTypeName[..commandTypeName.IndexOf('`')];

        var message = new WorkflowMessage
        {
            MessageId = Guid.NewGuid(),
            Direction = MessageDirection.Output,
            Kind = MessageKind.Command,
            MessageType = commandTypeName,
            InnerMessageType = innerMessageType?.AssemblyQualifiedName,
            Body = innerMessage != null
                ? JsonSerializer.Serialize(innerMessage, innerMessageType!, SerializerOptions)
                : "{}",
            AdditionalData = additionalData,
            DeserializedBody = command,
            DestinationAddress = destination,
            ScheduledTime = scheduledTime
        };

        stream.Append(message);
    }

    /// <summary>
    /// Rebuild workflow snapshot from stored events.
    /// </summary>
    WorkflowSnapshot<TInput, TState, TOutput> RebuildSnapshot(WorkflowStream stream)
    {
        var state = _workflow.InitialState;
        var eventHistory = new List<WorkflowEvent<TInput, TOutput>>();

        var storedEvents = stream.GetEvents();

        foreach (var message in storedEvents)
        {
            var evt = (WorkflowEvent<TInput, TOutput>)(message.DeserializedBody ?? DeserializeEvent(message));
            state = _workflow.Evolve(state, evt);
            eventHistory.Add(evt);
        }

        return new WorkflowSnapshot<TInput, TState, TOutput>(state, eventHistory);
    }

    /// <summary>
    /// Deliver pending command outputs from outbox.
    /// </summary>
    async Task DeliverOutputsAsync(WorkflowStream stream, CancellationToken ct)
    {
        var outputs = stream.GetPendingOutputs();
        var delivered = 0;
    
        foreach (var output in outputs)
        {
            if (delivered >= _options.OutputDeliveryLimit)
                break;
    
            // Skip scheduled messages that aren't due yet
            if (output.ScheduledTime.HasValue && output.ScheduledTime.Value > DateTime.UtcNow)
                continue;
    
            using var timeoutCts = new CancellationTokenSource(_options.OutputDeliveryTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
    
            output.DeserializedBody ??= DeserializeCommand(output);

            await _deliverOutput!(output, linkedCts.Token).ConfigureAwait(false);

            output.DeliveredTime = DateTime.UtcNow;
            stream.LastDeliveredSequence = output.SequenceNumber;
            delivered++;
        }
    }

    /// <summary>
    /// Deserialize event from stored message, reconstructing the proper generic type.
    /// </summary>
    WorkflowEvent<TInput, TOutput> DeserializeEvent(WorkflowMessage message)
    {
        // Deserialize inner message if present
        object? innerMessage = null;
        if (!string.IsNullOrEmpty(message.InnerMessageType) && message.Body != "{}")
        {
            var innerType = Type.GetType(message.InnerMessageType)
                ?? throw new InvalidOperationException($"Cannot resolve inner type: {message.InnerMessageType}");
            innerMessage = JsonSerializer.Deserialize(message.Body, innerType, SerializerOptions);
        }

        // Reconstruct the event based on type name
        return message.MessageType switch
        {
            "Began" => new Began<TInput, TOutput>(),
            "Completed" => new Completed<TInput, TOutput>(),
            "InitiatedBy" => new InitiatedBy<TInput, TOutput>((TInput)innerMessage!),
            "Received" => new Received<TInput, TOutput>((TInput)innerMessage!),
            "Sent" => new Sent<TInput, TOutput>((TOutput)innerMessage!),
            "Replied" => new Replied<TInput, TOutput>((TOutput)innerMessage!),
            "Published" => new Published<TInput, TOutput>((TOutput)innerMessage!),
            "Scheduled" => new Scheduled<TInput, TOutput>(
                TimeSpan.Parse(message.AdditionalData!),
                (TOutput)innerMessage!),
            _ => throw new InvalidOperationException($"Unknown event type: {message.MessageType}")
        };
    }

    /// <summary>
    /// Deserialize command from stored message, reconstructing the proper generic type.
    /// </summary>
    WorkflowCommand<TOutput> DeserializeCommand(WorkflowMessage message)
    {
        // Deserialize inner message if present
        object? innerMessage = null;
        if (!string.IsNullOrEmpty(message.InnerMessageType) && message.Body != "{}")
        {
            var innerType = Type.GetType(message.InnerMessageType)
                ?? throw new InvalidOperationException($"Cannot resolve inner type: {message.InnerMessageType}");
            innerMessage = JsonSerializer.Deserialize(message.Body, innerType, SerializerOptions);
        }

        // Reconstruct the command based on type name
        return message.MessageType switch
        {
            "Complete" => new Complete<TOutput>(),
            "Send" => new Send<TOutput>((TOutput)innerMessage!),
            "Reply" => new Reply<TOutput>((TOutput)innerMessage!),
            "Publish" => new Publish<TOutput>((TOutput)innerMessage!),
            "Schedule" => new Schedule<TOutput>(
                TimeSpan.Parse(message.AdditionalData!),
                (TOutput)innerMessage!),
            _ => throw new InvalidOperationException($"Unknown command type: {message.MessageType}")
        };
    }
}
