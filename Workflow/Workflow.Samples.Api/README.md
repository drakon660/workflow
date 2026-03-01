# Channel-Based Workflow Architecture

## Overview
This implementation replaces the distributed message broker approach with an in-memory channel and hosted service, maintaining the same workflow processing flow.

## Architecture Flow

```
API Endpoint → Channel → WorkflowConsumerService → WorkflowProcessor → WorkflowStreamRepository
                    ↓
OutputHandler ← WorkflowProcessor ← WorkflowConsumerService
```

## Components

### 1. WorkflowMessage<TInput>
- Wrapper for workflow inputs
- Contains WorkflowId, Input message, metadata (correlation, timestamps)

### 2. WorkflowConsumerService (BackgroundService)
- Reads from the channel continuously
- Processes messages using WorkflowProcessor
- Handles errors and logging

### 3. Channel (System.Threading.Channels)
- Producer-consumer pattern
- Thread-safe message passing
- Backpressure handling

### 4. WorkflowOutputHandler
- Processes workflow outputs (Send, Publish, Reply, etc.)
- Routes to appropriate handlers
- Extensible for different output types

## Usage

### API Endpoints
All endpoints now inject the channel instead of WorkflowProcessor:

```csharp
[FromServices] Channel<WorkflowMessage<OrderProcessingInputMessage>> channel
```

### Message Queueing
```csharp
var message = new WorkflowMessage<OrderProcessingInputMessage>
{
    WorkflowId = orderIdStr,
    Input = new PlaceOrderInputMessage(orderIdStr),
    CorrelationId = orderIdStr
};

await channel.Writer.WriteAsync(message, cancellationToken);
```

## Benefits

1. **Same Flow**: Maintains exact same workflow processing logic
2. **Decoupled**: API doesn't wait for processing completion
3. **Durable**: WorkflowProcessor still handles state persistence
4. **Scalable**: Channel handles concurrent producers/consumers
5. **Observable**: All workflow events stored in stream repository
6. **Simple**: No external message broker infrastructure needed

## Future Enhancements

1. **Retry Logic**: Add retry with exponential backoff
2. **Dead Letter Queue**: Handle failed messages
3. **Priority Processing**: Multiple channels for different priorities
4. **Metrics**: Add observability and performance tracking
5. **Scaling**: Multiple consumer instances

## Testing

Start the API and send requests to `/orders` endpoint. Messages will be queued and processed by the background service, with all workflow state persisted in the stream repository.