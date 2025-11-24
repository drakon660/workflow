namespace Workflow.Samples.Order;

public record OrderProcessingInputMessage;

public record PlaceOrderInputMessage(string OrderId) : OrderProcessingInputMessage;

public record PaymentReceivedInputMessage(string OrderId) : OrderProcessingInputMessage;

public record OrderShippedInputMessage(string OrderId, string TrackingNumber) : OrderProcessingInputMessage;

public record OrderDeliveredInputMessage(string OrderId)  : OrderProcessingInputMessage;

public record OrderCancelledInputMessage(string OrderId, string Reason) : OrderProcessingInputMessage;

public record PaymentTimeoutInputMessage(string OrderId) : OrderProcessingInputMessage;

public record CheckOrderStateInputMessage(string OrderId) : OrderProcessingInputMessage;

public record InsufficientInventoryInputMessage(string OrderId) : OrderProcessingInputMessage;

public record WarehouseInventoryReceivedInputMessage(string OrderId) : OrderProcessingInputMessage;

public record WarehouseInventoryUnavailableInputMessage(string OrderId) : OrderProcessingInputMessage;