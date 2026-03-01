# WorkflowProcessor Proposal

Standalone WorkflowProcessor implementation inspired by MassTransit's in-memory inbox/outbox pattern.

## Overview

This proposal implements the workflow processing pattern from the RFC using a similar architecture to MassTransit's `InMemoryInboxMessage`, `InMemoryOutboxMessage`, and `InMemoryOutboxMessageRepository`. The workflow stream acts as both inbox (for inputs) and outbox (for outputs).

## Component Mapping

| MassTransit Component | WorkflowProcessor Component | Purpose |
|-----------------------|----------------------------|---------|
| `InMemoryOutboxMessage` | `WorkflowMessage` | Message stored in stream |
| `InMemoryInboxMessage` | `WorkflowStream` | Workflow instance with message stream |
| `InMemoryOutboxMessageRepository` | `WorkflowStreamRepository` | Thread-safe storage with locking |
| `InMemoryOutboxContextFactory` | `WorkflowProcessor<TState>` | Processing with lock management |

## Processing Flow

```
Process(input)
  → Lock(workflowId)
  → StoreInput() → append to stream
  → RebuildState() → replay events through Evolve()
  → Decide() → get outputs
  → StoreOutput() → append each to stream
  → DeliverOutputs() → send pending, mark delivered
  → ReleaseLock()
```

## Implementation

```csharp
#nullable enable
namespace Emmett.Workflow
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    #region Message Types

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
        internal object? DeserializedBody { get; set; }
    }

    #endregion

    #region Workflow Stream (like InMemoryInboxMessage)

    /// <summary>
    /// Represents a workflow instance's stream containing both inbox (inputs) and outbox (outputs).
    /// Similar to MassTransit's InMemoryInboxMessage but stores the complete message stream.
    /// </summary>
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

        public List<WorkflowMessage> GetPendingOutputs()
        {
            var lastDelivered = LastDeliveredSequence ?? 0;
            lock (_messages)
                return _messages
                    .Where(m => m.Direction == MessageDirection.Output
                             && m.SequenceNumber > lastDelivered
                             && !m.IsDelivered)
                    .ToList();
        }

        public bool HasInput(Guid messageId)
        {
            lock (_messages)
                return _messages.Any(m => m.Direction == MessageDirection.Input && m.MessageId == messageId);
        }
    }

    #endregion

    #region Repository (like InMemoryOutboxMessageRepository)

    /// <summary>
    /// Repository for workflow streams. Provides thread-safe access and locking per workflow instance.
    /// Similar to MassTransit's InMemoryOutboxMessageRepository.
    /// </summary>
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

    #endregion

    #region Workflow Definition

    /// <summary>
    /// Defines a workflow with decide/evolve pattern.
    /// </summary>
    public interface IWorkflow<TState>
    {
        TState InitialState();
        TState Evolve(TState state, object @event, string eventType);
        IEnumerable<WorkflowOutput> Decide(object input, string inputType, TState state);
    }

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

    #endregion

    #region Workflow Processor Options

    public class WorkflowProcessorOptions
    {
        public string ProcessorId { get; set; } = default!;
        public int OutputDeliveryLimit { get; set; } = 100;
        public TimeSpan OutputDeliveryTimeout { get; set; } = TimeSpan.FromSeconds(30);
    }

    #endregion

    #region Workflow Processor

    /// <summary>
    /// Processes workflow inputs through the decide/evolve pattern with inbox/outbox storage.
    /// Inspired by MassTransit's InMemoryOutboxContextFactory and OutboxMessagePipe.
    /// </summary>
    public class WorkflowProcessor<TState>
    {
        readonly WorkflowStreamRepository _repository;
        readonly IWorkflow<TState> _workflow;
        readonly Func<object, string?> _getWorkflowId;
        readonly Func<WorkflowMessage, CancellationToken, Task> _deliverOutput;
        readonly WorkflowProcessorOptions _options;

        public WorkflowProcessor(
            WorkflowStreamRepository repository,
            IWorkflow<TState> workflow,
            Func<object, string?> getWorkflowId,
            Func<WorkflowMessage, CancellationToken, Task> deliverOutput,
            WorkflowProcessorOptions options)
        {
            _repository = repository;
            _workflow = workflow;
            _getWorkflowId = getWorkflowId;
            _deliverOutput = deliverOutput;
            _options = options;
        }

        /// <summary>
        /// Process an input message through the workflow.
        /// </summary>
        public async Task Process(object input, MessageKind inputKind, CancellationToken ct = default)
        {
            var workflowId = _getWorkflowId(input);
            if (workflowId == null)
                throw new InvalidOperationException("Unable to determine workflow ID from input");

            var stream = await _repository.Lock(workflowId, ct).ConfigureAwait(false);
            try
            {
                // Store input in inbox
                var inputMessage = StoreInput(stream, input, inputKind);

                // Rebuild state from events
                var state = RebuildState(stream);

                // Execute decide
                var outputs = _workflow.Decide(input, input.GetType().Name, state);

                // Store outputs in outbox
                foreach (var output in outputs)
                {
                    StoreOutput(stream, output);
                }

                stream.LastProcessedSequence = inputMessage.SequenceNumber;

                // Deliver pending outputs
                await DeliverOutputs(stream, ct).ConfigureAwait(false);
            }
            finally
            {
                stream.ReleaseLock();
            }
        }

        /// <summary>
        /// Store input message in workflow stream (inbox).
        /// </summary>
        WorkflowMessage StoreInput(WorkflowStream stream, object input, MessageKind kind)
        {
            var message = new WorkflowMessage
            {
                MessageId = Guid.NewGuid(),
                Direction = MessageDirection.Input,
                Kind = kind,
                MessageType = input.GetType().AssemblyQualifiedName!,
                Body = JsonSerializer.Serialize(input, input.GetType()),
                DeserializedBody = input
            };

            stream.Append(message);
            return message;
        }

        /// <summary>
        /// Store output message in workflow stream (outbox).
        /// </summary>
        void StoreOutput(WorkflowStream stream, WorkflowOutput output)
        {
            var message = new WorkflowMessage
            {
                MessageId = Guid.NewGuid(),
                Direction = MessageDirection.Output,
                Kind = output.Kind,
                MessageType = output.Message.GetType().AssemblyQualifiedName!,
                Body = JsonSerializer.Serialize(output.Message, output.Message.GetType()),
                DestinationAddress = output.Destination,
                DeserializedBody = output.Message
            };

            stream.Append(message);
        }

        /// <summary>
        /// Rebuild workflow state from events.
        /// </summary>
        TState RebuildState(WorkflowStream stream)
        {
            var state = _workflow.InitialState();
            var events = stream.GetEvents();

            foreach (var message in events)
            {
                var body = message.DeserializedBody ?? DeserializeBody(message);
                state = _workflow.Evolve(state, body, message.MessageType);
            }

            return state;
        }

        /// <summary>
        /// Deliver pending outputs from outbox.
        /// </summary>
        async Task DeliverOutputs(WorkflowStream stream, CancellationToken ct)
        {
            var outputs = stream.GetPendingOutputs();
            var delivered = 0;

            foreach (var output in outputs)
            {
                if (delivered >= _options.OutputDeliveryLimit)
                    break;

                using var timeoutCts = new CancellationTokenSource(_options.OutputDeliveryTimeout);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

                output.DeserializedBody ??= DeserializeBody(output);

                await _deliverOutput(output, linkedCts.Token).ConfigureAwait(false);

                output.DeliveredTime = DateTime.UtcNow;
                stream.LastDeliveredSequence = output.SequenceNumber;
                delivered++;
            }
        }

        static object DeserializeBody(WorkflowMessage message)
        {
            var type = Type.GetType(message.MessageType)
                ?? throw new InvalidOperationException($"Cannot resolve type: {message.MessageType}");
            return JsonSerializer.Deserialize(message.Body, type)!;
        }
    }

    #endregion
}
```

## Key Design Decisions

### 1. Single Stream for Inbox and Outbox

Unlike MassTransit which separates inbox messages from outbox messages, the workflow stream contains both inputs and outputs in a single ordered sequence. This provides:

- Complete audit trail of workflow execution
- Natural ordering of events for state rebuilding
- Simplified recovery (replay from any point)

### 2. Locking Strategy

Following MassTransit's pattern:
- Repository-level lock for creating/accessing streams
- Per-stream lock for processing (prevents concurrent execution of same workflow instance)

```csharp
// Repository lock (brief, for dictionary access)
await _lock.WaitAsync(ct);
try { /* get or create stream */ }
finally { _lock.Release(); }

// Stream lock (held during processing)
await stream.AcquireLock(ct);
try { /* process workflow */ }
finally { stream.ReleaseLock(); }
```

### 3. State Rebuilding

State is rebuilt from events only (not commands), matching the RFC specification:

```csharp
public List<WorkflowMessage> GetEvents()
{
    lock (_messages)
        return _messages.Where(m => m.Kind == MessageKind.Event).ToList();
}
```

### 4. Output Delivery Tracking

Similar to MassTransit's `LastSequenceNumber` tracking:

```csharp
public long? LastDeliveredSequence { get; set; }

public List<WorkflowMessage> GetPendingOutputs()
{
    var lastDelivered = LastDeliveredSequence ?? 0;
    lock (_messages)
        return _messages
            .Where(m => m.Direction == MessageDirection.Output
                     && m.SequenceNumber > lastDelivered
                     && !m.IsDelivered)
            .ToList();
}
```

## Stream Structure Example

```
Workflow Stream (workflowId: "group-checkout-123"):

Seq | Direction | Kind    | MessageType
----|-----------|---------|----------------------------------
1   | Input     | Command | InitiateGroupCheckout
2   | Output    | Event   | GroupCheckoutInitiated
3   | Output    | Command | CheckOut (guest: g1)
4   | Output    | Command | CheckOut (guest: g2)
5   | Input     | Event   | GuestCheckedOut (guest: g1)
6   | Input     | Event   | GuestCheckoutFailed (guest: g2)
7   | Output    | Event   | GroupCheckoutFailed
```

## Recovery Scenarios

### Crash After StoreInput, Before Decide

- Input is in stream
- On restart: detect unprocessed input, re-run Decide

### Crash After StoreOutput, Before Delivery

- Outputs are in stream with `DeliveredTime = null`
- On restart: `GetPendingOutputs()` returns undelivered, resume delivery

### Crash During Delivery

- `LastDeliveredSequence` tracks progress
- On restart: continue from last delivered sequence
