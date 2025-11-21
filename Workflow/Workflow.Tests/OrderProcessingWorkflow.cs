using System.Diagnostics;

namespace Workflow.Tests;

public sealed class OrderProcessingWorkflow : Workflow<OrderProcessingInputMessage, OrderProcessingState, OrderProcessingOutputMessage>
{
    public override OrderProcessingState InitialState { get; } = new NoOrder();
    protected override OrderProcessingState InternalEvolve(OrderProcessingState state,
        WorkflowEvent<OrderProcessingInputMessage, OrderProcessingOutputMessage> workflowEvent)
    {
        return (state, workflowEvent) switch
        {
            (NoOrder n, Events.InitiatedBy
                {
                    Message: PlaceOrderInputMessage m
                })
                => new OrderCreated(m.OrderId),
            
            (OrderCreated s, Events.Received { Message: PaymentReceivedInputMessage m }) => new PaymentConfirmed(m.OrderId),
            
            (PaymentConfirmed s, Events.Received { Message: OrderShippedInputMessage m }) =>
                new Shipped(m.OrderId,  m.TrackingNumber),
            
            (Shipped s, Events.Received
                {
                    Message: OrderDeliveredInputMessage m
                }) => new Delivered(m.OrderId, s.TrackingNumber),
            
            (OrderCreated s, Events.Received  {  Message: OrderCancelledInputMessage m }) =>
                new Cancelled(m.OrderId, m.Reason),
            
            (OrderCreated s, Events.Received  {  Message: PaymentTimeoutInputMessage m }) =>
                new Cancelled(m.OrderId, "Payment_Timeout"),
            
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

public record OrderProcessingInputMessage;

public record PlaceOrderInputMessage(string OrderId) : OrderProcessingInputMessage;

public record PaymentReceivedInputMessage(string OrderId) : OrderProcessingInputMessage;

public record OrderShippedInputMessage(string OrderId, string TrackingNumber) : OrderProcessingInputMessage;

public record OrderDeliveredInputMessage(string OrderId)  : OrderProcessingInputMessage;

public record OrderCancelledInputMessage(string OrderId, string Reason) : OrderProcessingInputMessage;

public record PaymentTimeoutInputMessage(string OrderId) : OrderProcessingInputMessage;

public record CheckOrderStateInputMessage(string OrderId) : OrderProcessingInputMessage;

public record OrderProcessingOutputMessage;

public record ProcessPayment(string OrderId) : OrderProcessingOutputMessage;

public record NotifyOrderPlaced(string OrderId) : OrderProcessingOutputMessage;

public record ShipOrder(string OrderId) : OrderProcessingOutputMessage;

public record NotifyOrderShipped(string OrderId, string TrackingNumber) : OrderProcessingOutputMessage;

public record NotifyOrderDelivered(string OrderId) : OrderProcessingOutputMessage;

public record NotifyOrderCancelled(string OrderId, string Reason) : OrderProcessingOutputMessage;

public record PaymentTimeoutOutMessage(string OrderId) : OrderProcessingOutputMessage;

public record OrderProcessingStatus(string OrderId, string Status) : OrderProcessingOutputMessage;


public abstract record OrderProcessingState();

public record NoOrder : OrderProcessingState;

public record OrderCreated(string OrderId) : OrderProcessingState;

public record PaymentConfirmed(string OrderId) : OrderProcessingState;

public record Shipped(string OrderId, string TrackingNumber) : OrderProcessingState;

public record Delivered(string OrderId, string TrackingNumber) : OrderProcessingState;

public record Cancelled(string OrderId, string Reason) : OrderProcessingState;