// Type aliases for cleaner pattern matching
using InitiatedBy = Workflow.InitiatedBy<Workflow.Samples.Order.OrderProcessingInputMessage, Workflow.Samples.Order.OrderProcessingOutputMessage>;
using Received = Workflow.Received<Workflow.Samples.Order.OrderProcessingInputMessage, Workflow.Samples.Order.OrderProcessingOutputMessage>;

namespace Workflow.Samples.Order;

public sealed class OrderProcessingWorkflow : Workflow<OrderProcessingInputMessage, OrderProcessingState, OrderProcessingOutputMessage>
{
    public override OrderProcessingState InitialState { get; } = new NoOrder();
    protected override OrderProcessingState InternalEvolve(OrderProcessingState state,
        WorkflowEvent<OrderProcessingInputMessage, OrderProcessingOutputMessage> workflowEvent)
    {
        return (state, workflowEvent) switch
        {
            (NoOrder n, InitiatedBy { Message: PlaceOrderInputMessage m })
                => new OrderCreated(m.OrderId),

            (OrderCreated s, Received { Message: PaymentReceivedInputMessage m })
                => new PaymentConfirmed(m.OrderId),

            (PaymentConfirmed s, Received { Message: OrderShippedInputMessage m })
                => new Shipped(m.OrderId,  m.TrackingNumber),

            (Shipped s, Received { Message: OrderDeliveredInputMessage m })
                => new Delivered(m.OrderId, s.TrackingNumber),

            (OrderCreated s, Received { Message: OrderCancelledInputMessage m })
                => new Cancelled(m.OrderId, m.Reason),

            (OrderCreated s, Received { Message: PaymentTimeoutInputMessage m })
                => new Cancelled(m.OrderId, "Payment_Timeout"),

            _ => state
        };
    }
    
    public override IReadOnlyList<WorkflowCommand<OrderProcessingOutputMessage>> Decide(
        OrderProcessingInputMessage input, OrderProcessingState state)
    {
        return (input, state) switch
        {
            (PlaceOrderInputMessage p, NoOrder s) =>
            [
                Send(new ProcessPayment(p.OrderId)),
                Send(new NotifyOrderPlaced(p.OrderId)),
                Schedule(TimeSpan.FromMinutes(15), new PaymentTimeoutOutMessage(p.OrderId))
            ],
            
            (PaymentReceivedInputMessage p, OrderCreated s) =>
            [
                Send(new ShipOrder(p.OrderId))
            ],

            (OrderShippedInputMessage p, PaymentConfirmed s) =>
            [
                Send(new NotifyOrderShipped(p.OrderId, p.TrackingNumber))
            ],

            (OrderDeliveredInputMessage p, Shipped s) =>
            [
                Send(new NotifyOrderDelivered(s.OrderId)),
                Complete()
            ],

            (OrderCancelledInputMessage p, OrderCreated s) =>
            [
                Send(new NotifyOrderCancelled(s.OrderId, "Cancelled")),
                Complete()
            ],

            (PaymentTimeoutInputMessage p, OrderCreated s) =>
            [
                Send(new NotifyOrderCancelled(s.OrderId, "Payment_Timeout")),
                Complete()
            ],
            
            (CheckOrderStateInputMessage p, OrderProcessingState s) => [
                Reply(new OrderProcessingStatus(
                    p.OrderId,
                    s switch
                    {
                        NoOrder => "NotExisting",
                        OrderCreated => "OrderCreated",
                        PaymentConfirmed => "PaymentConfirmed",
                        Shipped => "Shipped",
                        Delivered => "Delivered",
                        Cancelled c => $"Cancelled: {c.Reason}",
                        _ => "Unknown"
                    }
                ))
            ],

            _ => throw new NotImplementedException()
        };
    }
}


public sealed class OrderProcessingAsyncWorkflow : AsyncWorkflow<OrderProcessingInputMessage, OrderProcessingState,
    OrderProcessingOutputMessage, IOrderContext>
{
    public override OrderProcessingState InitialState { get; } = new NoOrder();
    protected override OrderProcessingState InternalEvolve(OrderProcessingState state, WorkflowEvent<OrderProcessingInputMessage, OrderProcessingOutputMessage> workflowEvent)
    {
        return (state, workflowEvent) switch
        {
            (NoOrder n, InitiatedBy { Message: PlaceOrderInputMessage m })
                => new OrderCreated(m.OrderId),

            (OrderCreated s, Received { Message: InsufficientInventoryInputMessage m })
                => new AwaitingWarehouseInventory(m.OrderId),

            (AwaitingWarehouseInventory s, Received { Message: WarehouseInventoryReceivedInputMessage m })
                => new PaymentConfirmed(m.OrderId),

            (AwaitingWarehouseInventory s, Received { Message: WarehouseInventoryUnavailableInputMessage m })
                => new Cancelled(m.OrderId, "Warehouse_Inventory_Unavailable"),

            (OrderCreated s, Received { Message: PaymentReceivedInputMessage m })
                => new PaymentConfirmed(m.OrderId),

            (PaymentConfirmed s, Received { Message: OrderShippedInputMessage m })
                => new Shipped(m.OrderId, m.TrackingNumber),

            (Shipped s, Received { Message: OrderDeliveredInputMessage m })
                => new Delivered(m.OrderId, s.TrackingNumber),

            (OrderCreated s, Received { Message: OrderCancelledInputMessage m })
                => new Cancelled(m.OrderId, m.Reason),

            (OrderCreated s, Received { Message: PaymentTimeoutInputMessage m })
                => new Cancelled(m.OrderId, "Payment_Timeout"),

            _ => state
        };
    }

    public override async Task<IReadOnlyList<WorkflowCommand<OrderProcessingOutputMessage>>> DecideAsync(OrderProcessingInputMessage input, OrderProcessingState state, IOrderContext service)
    {
        switch (input, state)
        {
            case (PlaceOrderInputMessage p, NoOrder):
                // Check inventory availability before processing order
                var hasInventory = await service.CheckInventoryAsync(p.OrderId, quantity: 1);

                if (!hasInventory)
                {
                    // No local inventory - request from warehouse
                    // The external system should then send InsufficientInventoryInputMessage to transition state
                    return [
                        Send(new NotifyInsufficientInventory(p.OrderId))
                    ];
                }

                return [
                    Send(new ProcessPayment(p.OrderId)),
                    Send(new NotifyOrderPlaced(p.OrderId)),
                    Schedule(TimeSpan.FromMinutes(15), new PaymentTimeoutOutMessage(p.OrderId))
                ];

            case (PaymentReceivedInputMessage p, OrderCreated):
                return [Send(new ShipOrder(p.OrderId))];

            case (OrderShippedInputMessage p, PaymentConfirmed):
                return [Send(new NotifyOrderShipped(p.OrderId, p.TrackingNumber))];

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
                        p.OrderId,
                        s switch
                        {
                            NoOrder => "NotExisting",
                            OrderCreated => "OrderCreated",
                            PaymentConfirmed => "PaymentConfirmed",
                            Shipped => "Shipped",
                            Delivered => "Delivered",
                            Cancelled c => $"Cancelled: {c.Reason}",
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