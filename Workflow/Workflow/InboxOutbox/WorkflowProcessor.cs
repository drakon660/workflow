using System.Text.Json;

namespace Workflow.InboxOutbox;

/// <summary>
/// Processes workflow inputs through the decide/evolve pattern with inbox/outbox storage.
/// Uses WorkflowOrchestrator for state management and event translation.
/// </summary>
public class WorkflowProcessor<TInput, TState, TOutput>
{
    readonly WorkflowStreamRepository _repository;
    readonly IWorkflow<TInput, TState, TOutput> _workflow;
    readonly WorkflowOrchestrator<TInput, TState, TOutput> _orchestrator;
    readonly Func<WorkflowMessage, CancellationToken, Task> _deliverOutput;
    readonly WorkflowProcessorOptions _options;

    public WorkflowProcessor(
        WorkflowStreamRepository repository,
        IWorkflow<TInput, TState, TOutput> workflow,
        Func<WorkflowMessage, CancellationToken, Task> deliverOutput,
        WorkflowProcessorOptions options)
    {
        _repository = repository;
        _workflow = workflow;
        _orchestrator = new WorkflowOrchestrator<TInput, TState, TOutput>();
        _deliverOutput = deliverOutput;
        _options = options;
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
            MessageType = typeof(TInput).AssemblyQualifiedName!,
            Body = JsonSerializer.Serialize(input),
            DeserializedBody = input
        };

        stream.Append(message);
        return message;
    }

    /// <summary>
    /// Store event in workflow stream (for state rebuilding).
    /// </summary>
    void StoreEvent(WorkflowStream stream, WorkflowEvent<TInput, TOutput> evt)
    {
        var message = new WorkflowMessage
        {
            MessageId = Guid.NewGuid(),
            Direction = MessageDirection.Output, // Events are outputs of processing
            Kind = MessageKind.Event,
            MessageType = evt.GetType().AssemblyQualifiedName!,
            Body = JsonSerializer.Serialize(evt, evt.GetType()),
            DeserializedBody = evt
        };

        stream.Append(message);
    }

    /// <summary>
    /// Store command in workflow stream (outbox for delivery).
    /// </summary>
    void StoreCommand(WorkflowStream stream, WorkflowCommand<TOutput> command)
    {
        var (destination, scheduledTime) = command switch
        {
            Send<TOutput> => ("send", (DateTime?)null),
            Reply<TOutput> => ("reply", null),
            Publish<TOutput> => ("publish", null),
            Schedule<TOutput> s => ("schedule", DateTime.UtcNow.Add(s.After)),
            Complete<TOutput> => ("complete", null),
            _ => ("unknown", (DateTime?)null)
        };

        var message = new WorkflowMessage
        {
            MessageId = Guid.NewGuid(),
            Direction = MessageDirection.Output,
            Kind = MessageKind.Command,
            MessageType = command.GetType().AssemblyQualifiedName!,
            Body = JsonSerializer.Serialize(command, command.GetType()),
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

            await _deliverOutput(output, linkedCts.Token).ConfigureAwait(false);

            output.DeliveredTime = DateTime.UtcNow;
            stream.LastDeliveredSequence = output.SequenceNumber;
            delivered++;
        }
    }

    WorkflowEvent<TInput, TOutput> DeserializeEvent(WorkflowMessage message)
    {
        var type = Type.GetType(message.MessageType)
                   ?? throw new InvalidOperationException($"Cannot resolve type: {message.MessageType}");
        return (WorkflowEvent<TInput, TOutput>)JsonSerializer.Deserialize(message.Body, type)!;
    }

    WorkflowCommand<TOutput> DeserializeCommand(WorkflowMessage message)
    {
        var type = Type.GetType(message.MessageType)
                   ?? throw new InvalidOperationException($"Cannot resolve type: {message.MessageType}");
        return (WorkflowCommand<TOutput>)JsonSerializer.Deserialize(message.Body, type)!;
    }
}
