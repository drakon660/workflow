using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Google.Cloud.PubSub.V1;
using Google.Protobuf;

namespace IssueTrafficFineForSpeedingViolationWorkflowProcessor
{
    using IssueTrafficFineForSpeedingViolationWorkflow;
    using WorkflowEngine;

    /// <summary>
    /// Enhanced processor with reliability patterns:
    /// - Idempotency keys
    /// - Message deduplication
    /// - Correlation IDs
    /// - Timeout scheduling
    /// </summary>
    public static class ProcessorWithReliability
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        /// <summary>
        /// Envelope with reliability metadata
        /// </summary>
        private static PubsubMessage EnvelopWithMetadata(
            string workflowId,
            string messageId,
            string commandType,
            int sequenceNumber,
            object message)
        {
            string json = JsonSerializer.Serialize(message, message.GetType(), JsonOptions);
            PubsubMessage envelope = new PubsubMessage
            {
                Data = ByteString.CopyFromUtf8(json)
            };

            // Message ID for Pub/Sub deduplication
            envelope.Attributes.Add("message_id", messageId);

            // Workflow correlation
            envelope.Attributes.Add("workflow_id", workflowId);

            // Idempotency key: prevents duplicate processing
            // Format: {workflow_id}:{command_type}:{sequence}
            string idempotencyKey = $"{workflowId}:{commandType}:{sequenceNumber}";
            envelope.Attributes.Add("idempotency_key", idempotencyKey);

            // Command type for routing
            envelope.Attributes.Add("command_type", commandType);

            // Timestamp
            envelope.Attributes.Add("timestamp", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());

            return envelope;
        }

        /// <summary>
        /// Handle command with reliability features
        /// </summary>
        public static async Task Handle(
            WorkflowClients clients,
            string workflowId,
            string messageId,
            int sequenceNumber,
            WorkflowCommand<OutputMessage> command)
        {
            switch (command)
            {
                case Send<OutputMessage> send:
                    await HandleSendWithReliability(clients, workflowId, messageId, sequenceNumber, send.Message);
                    break;

                case Schedule<OutputMessage> schedule:
                    await HandleSchedule(clients, workflowId, messageId, sequenceNumber, schedule);
                    break;

                case Complete<OutputMessage>:
                    // Mark workflow as complete in persistence layer
                    Console.WriteLine($"[Workflow {workflowId}] Complete");
                    break;

                default:
                    throw new NotImplementedException($"{command} command has not been implemented.");
            }
        }

        /// <summary>
        /// Handle Send command with reliability metadata
        /// </summary>
        private static async Task HandleSendWithReliability(
            WorkflowClients clients,
            string workflowId,
            string messageId,
            int sequenceNumber,
            OutputMessage message)
        {
            switch (message)
            {
                case GenerateTrafficFineSystemNumber m:
                    {
                        PubsubMessage envelope = EnvelopWithMetadata(
                            workflowId,
                            messageId,
                            "GenerateTrafficFineSystemNumber",
                            sequenceNumber,
                            m);

                        await clients.SystemNumberTopic.PublishAsync(envelope);
                        Console.WriteLine($"[Workflow {workflowId}] Published: GenerateTrafficFineSystemNumber");
                        Console.WriteLine($"  Idempotency Key: {envelope.Attributes["idempotency_key"]}");
                        break;
                    }

                case GenerateTrafficFineManualIdentificationCode m:
                    {
                        PubsubMessage envelope = EnvelopWithMetadata(
                            workflowId,
                            messageId,
                            "GenerateTrafficFineManualIdentificationCode",
                            sequenceNumber,
                            m);

                        await clients.ManualIdentificationCodeTopic.PublishAsync(envelope);
                        Console.WriteLine($"[Workflow {workflowId}] Published: GenerateTrafficFineManualIdentificationCode");
                        Console.WriteLine($"  Idempotency Key: {envelope.Attributes["idempotency_key"]}");
                        break;
                    }

                case IssueTrafficFine m:
                    {
                        PubsubMessage envelope = EnvelopWithMetadata(
                            workflowId,
                            messageId,
                            "IssueTrafficFine",
                            sequenceNumber,
                            m);

                        await clients.IssueTrafficFineTopic.PublishAsync(envelope);
                        Console.WriteLine($"[Workflow {workflowId}] Published: IssueTrafficFine");
                        Console.WriteLine($"  Idempotency Key: {envelope.Attributes["idempotency_key"]}");
                        break;
                    }

                default:
                    throw new NotImplementedException($"{message.GetType().Name} message has not been implemented.");
            }
        }

        /// <summary>
        /// Handle Schedule command (for timeouts)
        /// </summary>
        private static async Task HandleSchedule(
            WorkflowClients clients,
            string workflowId,
            string messageId,
            int sequenceNumber,
            Schedule<OutputMessage> schedule)
        {
            Console.WriteLine($"[Workflow {workflowId}] Scheduling message after {schedule.After.TotalSeconds}s");

            // In real implementation:
            // - Use Cloud Scheduler or Cloud Tasks
            // - Schedule a message to be delivered after the delay
            // - Include timeout metadata

            await Task.CompletedTask;
        }
    }

    /// <summary>
    /// Example command handler/consumer showing idempotency checking
    /// This is what runs on the other side of Pub/Sub
    /// </summary>
    public class CommandHandlerExample
    {
        private readonly IIdempotencyStore _idempotencyStore;

        public CommandHandlerExample(IIdempotencyStore idempotencyStore)
        {
            _idempotencyStore = idempotencyStore;
        }

        /// <summary>
        /// Process incoming command from Pub/Sub subscription
        /// </summary>
        public async Task<bool> HandleGenerateSystemNumber(PubsubMessage message)
        {
            // Extract metadata
            string workflowId = message.Attributes["workflow_id"];
            string messageId = message.Attributes["message_id"];
            string idempotencyKey = message.Attributes["idempotency_key"];

            Console.WriteLine($"[Consumer] Received: GenerateSystemNumber");
            Console.WriteLine($"  Workflow ID: {workflowId}");
            Console.WriteLine($"  Message ID: {messageId}");
            Console.WriteLine($"  Idempotency Key: {idempotencyKey}");

            // 1. Check idempotency: Have we already processed this?
            string cachedResult = await _idempotencyStore.Get(idempotencyKey);
            if (cachedResult != null)
            {
                Console.WriteLine($"[Consumer] Already processed - returning cached result");
                // Optionally re-publish the result event
                return true;
            }

            // 2. Process the command
            Console.WriteLine($"[Consumer] Processing command...");
            string systemNumber = GenerateSystemNumber(); // Business logic

            // 3. Store result with idempotency key (in same transaction if possible)
            await _idempotencyStore.Set(idempotencyKey, systemNumber);
            Console.WriteLine($"[Consumer] Stored result: {systemNumber}");

            // 4. Publish result event back to workflow
            // PublishEvent(new TrafficFineSystemNumberGenerated(...));
            Console.WriteLine($"[Consumer] Published result event");

            return true;
        }

        private string GenerateSystemNumber()
        {
            return $"PPXRG/{DateTime.UtcNow.Year}TV{new Random().Next(1000, 9999)}";
        }
    }

    /// <summary>
    /// Idempotency store interface (Redis, Database, etc.)
    /// </summary>
    public interface IIdempotencyStore
    {
        Task<string> Get(string key);
        Task Set(string key, string value, TimeSpan? expiration = null);
        Task<bool> Contains(string key);
    }

    /// <summary>
    /// In-memory idempotency store for demonstration
    /// In production: use Redis, Memcached, or database
    /// </summary>
    public class InMemoryIdempotencyStore : IIdempotencyStore
    {
        private readonly Dictionary<string, (string value, DateTime expiry)> _store = new();

        public Task<string> Get(string key)
        {
            if (_store.TryGetValue(key, out (string value, DateTime expiry) entry))
            {
                if (entry.expiry > DateTime.UtcNow)
                {
                    return Task.FromResult(entry.value);
                }
                _store.Remove(key);
            }
            return Task.FromResult<string>(null);
        }

        public Task Set(string key, string value, TimeSpan? expiration = null)
        {
            DateTime expiry = DateTime.UtcNow.Add(expiration ?? TimeSpan.FromHours(24));
            _store[key] = (value, expiry);
            return Task.CompletedTask;
        }

        public async Task<bool> Contains(string key)
        {
            string value = await Get(key);
            return value != null;
        }
    }

    /// <summary>
    /// Example showing the full flow with reliability patterns
    /// </summary>
    public class ReliabilityExample
    {
        public static async Task RunExample()
        {
            Console.WriteLine("=== Reliability Patterns Example ===\n");

            // Setup
            IIdempotencyStore idempotencyStore = new InMemoryIdempotencyStore();
            CommandHandlerExample consumer = new CommandHandlerExample(idempotencyStore);

            // Simulate workflow sending command
            Console.WriteLine("--- Workflow Sends Command ---");
            string workflowId = "wf-123";
            string messageId = "msg-001";
            int sequenceNumber = 1;
            string idempotencyKey = $"{workflowId}:GenerateTrafficFineSystemNumber:{sequenceNumber}";

            PubsubMessage simulatedMessage = new PubsubMessage();
            simulatedMessage.Attributes.Add("workflow_id", workflowId);
            simulatedMessage.Attributes.Add("message_id", messageId);
            simulatedMessage.Attributes.Add("idempotency_key", idempotencyKey);
            simulatedMessage.Attributes.Add("command_type", "GenerateTrafficFineSystemNumber");

            // First processing - should execute
            Console.WriteLine("\n--- First Processing Attempt ---");
            await consumer.HandleGenerateSystemNumber(simulatedMessage);

            // Duplicate message (network retry, Pub/Sub redelivery, etc.)
            Console.WriteLine("\n--- Duplicate Message (Should Skip) ---");
            await consumer.HandleGenerateSystemNumber(simulatedMessage);

            Console.WriteLine("\n=== Key Takeaways ===");
            Console.WriteLine("1. Idempotency key prevents duplicate processing");
            Console.WriteLine("2. Cached results can be returned immediately");
            Console.WriteLine("3. Consumers publish result events back to workflow");
            Console.WriteLine("4. Workflow continues when it receives result event");
        }
    }
}
