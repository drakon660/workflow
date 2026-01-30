using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Workflow.InboxOutbox;

namespace Workflow.Samples.Api.Services;

/// <summary>
/// Handles output messages from the workflow processor.
/// Processes different types of outputs (Send, Reply, Publish, etc.) and routes them appropriately.
/// </summary>
public static class WorkflowOutputHandler
{
    /// <summary>
    /// Creates an output handler function that can be registered with the workflow processor.
    /// </summary>
    public static Func<WorkflowMessage, CancellationToken, Task> CreateHandler(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("WorkflowOutputHandler");

        return async (workflowMessage, ct) =>
        {
            try
            {
                logger.LogInformation("Processing workflow output: {MessageType} to {Destination}",
                    workflowMessage.MessageType, workflowMessage.DestinationAddress);

                switch (workflowMessage.DestinationAddress)
                {
                    case "send":
                        await HandleSendAsync(workflowMessage, logger, ct);
                        break;

                    case "publish":
                        await HandlePublishAsync(workflowMessage, logger, ct);
                        break;

                    case "reply":
                        await HandleReplyAsync(workflowMessage, logger, ct);
                        break;

                    case "schedule":
                        // Scheduled messages are handled by the processor's timer logic
                        logger.LogInformation("Scheduled message for {Time}", workflowMessage.ScheduledTime);
                        break;

                    case "complete":
                        await HandleCompleteAsync(workflowMessage, logger, ct);
                        break;

                    default:
                        logger.LogWarning("Unknown destination address: {Destination}", workflowMessage.DestinationAddress);
                        break;
                }

                logger.LogInformation("Successfully processed workflow output: {MessageType}", workflowMessage.MessageType);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to process workflow output: {MessageType}", workflowMessage.MessageType);
                throw;
            }
        };
    }

    private static Task HandleSendAsync(WorkflowMessage workflowMessage, ILogger logger, CancellationToken ct)
    {
        // Extract the inner message
        if (workflowMessage.InnerMessageType != null && workflowMessage.Body != "{}")
        {
            var innerType = Type.GetType(workflowMessage.InnerMessageType);
            if (innerType != null)
            {
                logger.LogInformation("Sending message of type: {InnerType}", innerType.Name);

                // Here you could:
                // 1. Send HTTP requests to external APIs
                // 2. Queue additional workflow messages
                // 3. Call other services

                logger.LogInformation("Message sent: {Body}", workflowMessage.Body);
            }
        }
        return Task.CompletedTask;
    }

    private static Task HandlePublishAsync(WorkflowMessage workflowMessage, ILogger logger, CancellationToken ct)
    {
        // Publish events - could be:
        // 1. Stored in an events table
        // 2. Broadcast via SignalR
        // 3. Sent to external event systems

        logger.LogInformation("Published event: {Body}", workflowMessage.Body);
        return Task.CompletedTask;
    }

    private static Task HandleReplyAsync(WorkflowMessage workflowMessage, ILogger logger, CancellationToken ct)
    {
        // Handle replies - could be:
        // 1. Stored for API polling
        // 2. Sent via SignalR to waiting clients
        // 3. Cached for immediate responses

        logger.LogInformation("Reply handled: {Body}", workflowMessage.Body);
        return Task.CompletedTask;
    }

    private static Task HandleCompleteAsync(WorkflowMessage workflowMessage, ILogger logger, CancellationToken ct)
    {
        // Handle workflow completion
        logger.LogInformation("Workflow completed");
        return Task.CompletedTask;
    }
}
