namespace Workflow.Samples.Order;

public abstract record OrderProcessingInputMessage : IWorkflowInput
{
    public required string WorkflowId { get; init; }
}

public record PlaceOrderInputMessage : OrderProcessingInputMessage;

public record PaymentReceivedInputMessage : OrderProcessingInputMessage;

public record OrderShippedInputMessage(string TrackingNumber) : OrderProcessingInputMessage;

public record OrderDeliveredInputMessage : OrderProcessingInputMessage;

public record OrderCancelledInputMessage(string Reason) : OrderProcessingInputMessage;

public record PaymentTimeoutInputMessage : OrderProcessingInputMessage;

public record CheckOrderStateInputMessage : OrderProcessingInputMessage;

public record InsufficientInventoryInputMessage : OrderProcessingInputMessage;

public record WarehouseInventoryReceivedInputMessage : OrderProcessingInputMessage;

public record WarehouseInventoryUnavailableInputMessage : OrderProcessingInputMessage;
