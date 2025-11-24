namespace Workflow.Samples.Order;

public abstract record OrderProcessingState();

public record NoOrder : OrderProcessingState;

public record OrderCreated(string OrderId) : OrderProcessingState;

public record PaymentConfirmed(string OrderId) : OrderProcessingState;

public record Shipped(string OrderId, string TrackingNumber) : OrderProcessingState;

public record Delivered(string OrderId, string TrackingNumber) : OrderProcessingState;

public record Cancelled(string OrderId, string Reason) : OrderProcessingState;

public record InsufficientInventory(string OrderId) : OrderProcessingState;

public record AwaitingWarehouseInventory(string OrderId) : OrderProcessingState;