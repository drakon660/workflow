namespace Workflow.Samples.Order;

public sealed class OrderProcessingAsyncWorkflow : AsyncWorkflow<OrderProcessingInputMessage, OrderProcessingState,
    OrderProcessingOutputMessage, IOrderContext>
{
    public override OrderProcessingState InitialState { get; } = new NoOrder();
    protected override OrderProcessingState InternalEvolve(OrderProcessingState state, WorkflowEvent<OrderProcessingInputMessage, OrderProcessingOutputMessage> workflowEvent)
    {
        return (state, workflowEvent) switch
        {
            (NoOrder n, InitiatedBy<OrderProcessingInputMessage, OrderProcessingOutputMessage> { Message: PlaceOrderInputMessage m })
                => new OrderCreated(m.WorkflowId),

            (OrderCreated s, Received<OrderProcessingInputMessage, OrderProcessingOutputMessage> { Message: InsufficientInventoryInputMessage m })
                => new AwaitingWarehouseInventory(m.WorkflowId),

            (AwaitingWarehouseInventory s, Received<OrderProcessingInputMessage, OrderProcessingOutputMessage> { Message: WarehouseInventoryReceivedInputMessage m })
                => new PaymentConfirmed(m.WorkflowId),

            (AwaitingWarehouseInventory s, Received<OrderProcessingInputMessage, OrderProcessingOutputMessage> { Message: WarehouseInventoryUnavailableInputMessage m })
                => new OrderCancelled(m.WorkflowId, "Warehouse_Inventory_Unavailable"),

            (OrderCreated s, Received<OrderProcessingInputMessage, OrderProcessingOutputMessage> { Message: PaymentReceivedInputMessage m })
                => new PaymentConfirmed(m.WorkflowId),

            (PaymentConfirmed s, Received<OrderProcessingInputMessage, OrderProcessingOutputMessage> { Message: OrderShippedInputMessage m })
                => new Shipped(m.WorkflowId, m.TrackingNumber),

            (Shipped s, Received<OrderProcessingInputMessage, OrderProcessingOutputMessage> { Message: OrderDeliveredInputMessage m })
                => new Delivered(m.WorkflowId, s.TrackingNumber),

            (OrderCreated s, Received<OrderProcessingInputMessage, OrderProcessingOutputMessage> { Message: OrderCancelledInputMessage m })
                => new OrderCancelled(m.WorkflowId, m.Reason),

            (OrderCreated s, Received<OrderProcessingInputMessage, OrderProcessingOutputMessage> { Message: PaymentTimeoutInputMessage m })
                => new OrderCancelled(m.WorkflowId, "Payment_Timeout"),

            _ => state
        };
    }
    public override async Task<IReadOnlyList<WorkflowCommand<OrderProcessingOutputMessage>>> DecideAsync(OrderProcessingInputMessage input, OrderProcessingState state, IOrderContext service)
    {
        switch (input, state)
        {
            case (PlaceOrderInputMessage p, NoOrder):
                // Check inventory availability before processing order
                var hasInventory = await service.CheckInventoryAsync(p.WorkflowId, quantity: 1);

                if (!hasInventory)
                {
                    // No local inventory - request from warehouse
                    return [
                        Send(new NotifyInsufficientInventory(p.WorkflowId))
                    ];
                }

                return [
                    Send(new ProcessPayment(p.WorkflowId)),
                    Send(new NotifyOrderPlaced(p.WorkflowId)),
                    Schedule(TimeSpan.FromMinutes(15), new PaymentTimeoutOutMessage(p.WorkflowId))
                ];

            case (PaymentReceivedInputMessage p, OrderCreated):
                return [Send(new ShipOrder(p.WorkflowId))];

            case (OrderShippedInputMessage p, PaymentConfirmed):
                return [Send(new NotifyOrderShipped(p.WorkflowId, p.TrackingNumber))];

            case (OrderDeliveredInputMessage p, Shipped s):
                return [
                    Send(new NotifyOrderDelivered(s.OrderId)),
                    Complete()
                ];

            case (OrderCancelledInputMessage p, OrderCreated s):
                return [
                    Send(new NotifyOrderCancelled(s.OrderId, "Cancelled")),
                    Complete()
                ];

            case (PaymentTimeoutInputMessage p, OrderCreated s):
                return [
                    Send(new NotifyOrderCancelled(s.OrderId, "Payment_Timeout")),
                    Complete()
                ];

            case (InsufficientInventoryInputMessage p, OrderCreated s):
                // Request inventory from main warehouse
                return [
                    Send(new RequestInventoryFromWarehouse(s.OrderId, Quantity: 1))
                ];

            case (WarehouseInventoryReceivedInputMessage p, AwaitingWarehouseInventory s):
                // Warehouse has inventory - proceed with payment
                return [
                    Send(new ProcessPayment(s.OrderId)),
                    Send(new NotifyOrderPlaced(s.OrderId)),
                    Schedule(TimeSpan.FromMinutes(15), new PaymentTimeoutOutMessage(s.OrderId))
                ];

            case (WarehouseInventoryUnavailableInputMessage p, AwaitingWarehouseInventory s):
                // Warehouse also doesn't have inventory - cancel order
                return [
                    Send(new NotifyOrderCancelled(s.OrderId, "Warehouse_Inventory_Unavailable")),
                    Complete()
                ];

            case (CheckOrderStateInputMessage p, OrderProcessingState s):
                return [
                    Reply(new OrderProcessingStatus(
                        p.WorkflowId,
                        s switch
                        {
                            NoOrder => "NotExisting",
                            OrderCreated => "OrderCreated",
                            PaymentConfirmed => "PaymentConfirmed",
                            Shipped => "Shipped",
                            Delivered => "Delivered",
                            OrderCancelled c => $"Cancelled: {c.Reason}",
                            InsufficientInventory => "InsufficientInventory",
                            AwaitingWarehouseInventory => "AwaitingWarehouseInventory",
                            _ => "Unknown"
                        }
                    ))
                ];

            default:
                throw new NotImplementedException();
        }
    }
}
