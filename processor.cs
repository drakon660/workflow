using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Google.Cloud.PubSub.V1;
using Google.Protobuf;

namespace IssueTrafficFineForSpeedingViolationWorkflowProcessor
{
    using IssueTrafficFineForSpeedingViolationWorkflow;
    using WorkflowEngine;

    public class WorkflowClients
    {
        public Topic SystemNumberTopic { get; init; }
        public Topic ManualIdentificationCodeTopic { get; init; }
        public Topic IssueTrafficFineTopic { get; init; }
    }

    public static class Processor
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters =
            {
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
            }
        };

        private static PubsubMessage Envelop(string workflowId, string messageId, object message)
        {
            var json = JsonSerializer.Serialize(message, message.GetType(), JsonOptions);
            var envelope = new PubsubMessage
            {
                Data = ByteString.CopyFromUtf8(json)
            };
            envelope.Attributes.Add("workflow_id", workflowId);
            envelope.Attributes.Add("message_id", messageId);
            return envelope;
        }

        public static async Task Handle(
            WorkflowClients clients,
            string workflowId,
            string messageId,
            WorkflowCommand<OutputMessage> command)
        {
            switch (command)
            {
                case Send<OutputMessage> send:
                    await HandleSend(clients, workflowId, messageId, send.Message);
                    break;

                case Reply<OutputMessage> reply:
                    throw new NotImplementedException($"Reply command has not been implemented.");

                case Publish<OutputMessage> publish:
                    throw new NotImplementedException($"Publish command has not been implemented.");

                case Schedule<OutputMessage> schedule:
                    throw new NotImplementedException($"Schedule command has not been implemented.");

                case Complete<OutputMessage>:
                    // Complete command - no action needed
                    break;

                default:
                    throw new NotImplementedException($"{command} command has not been implemented.");
            }
        }

        private static async Task HandleSend(
            WorkflowClients clients,
            string workflowId,
            string messageId,
            OutputMessage message)
        {
            switch (message)
            {
                case GenerateTrafficFineSystemNumber m:
                    {
                        var envelope = Envelop(workflowId, messageId, m);
                        await clients.SystemNumberTopic.PublishAsync(envelope);
                        break;
                    }

                case GenerateTrafficFineManualIdentificationCode m:
                    {
                        var envelope = Envelop(workflowId, messageId, m);
                        await clients.ManualIdentificationCodeTopic.PublishAsync(envelope);
                        break;
                    }

                case IssueTrafficFine m:
                    {
                        var envelope = Envelop(workflowId, messageId, m);
                        await clients.IssueTrafficFineTopic.PublishAsync(envelope);
                        break;
                    }

                default:
                    throw new NotImplementedException($"{message.GetType().Name} message has not been implemented.");
            }
        }
    }
}
