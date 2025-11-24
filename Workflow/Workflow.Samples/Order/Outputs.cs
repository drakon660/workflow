namespace Workflow.Samples.Order;

public record OrderProcessingOutputMessage;

public record ProcessPayment(string OrderId) : OrderProcessingOutputMessage;

public record NotifyOrderPlaced(string OrderId) : OrderProcessingOutputMessage;

public record ShipOrder(string OrderId) : OrderProcessingOutputMessage;

public record NotifyOrderShipped(string OrderId, string TrackingNumber) : OrderProcessingOutputMessage;

public record NotifyOrderDelivered(string OrderId) : OrderProcessingOutputMessage;

public record NotifyOrderCancelled(string OrderId, string Reason) : OrderProcessingOutputMessage;

public record PaymentTimeoutOutMessage(string OrderId) : OrderProcessingOutputMessage;

public record OrderProcessingStatus(string OrderId, string Status) : OrderProcessingOutputMessage;

public record NotifyInsufficientInventory(string OrderId) : OrderProcessingOutputMessage;

public record RequestInventoryFromWarehouse(string OrderId, int Quantity) : OrderProcessingOutputMessage;