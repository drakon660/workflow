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
            { state: NoOrder s, workflowEvent: InitiatedBy { Message: PlaceOrderInputMessage m } } => new OrderCreated(m.WorkflowId),

            { state: OrderCreated s, workflowEvent: Received { Message: PaymentReceivedInputMessage m } } => new PaymentConfirmed(m.WorkflowId),

            { state: PaymentConfirmed s, workflowEvent: Received { Message: OrderShippedInputMessage m } } => new Shipped(m.WorkflowId, m.TrackingNumber),

            { state: Shipped s, workflowEvent: Received { Message: OrderDeliveredInputMessage m } } => new Delivered(m.WorkflowId, s.TrackingNumber),

            { state: OrderCreated s, workflowEvent: Received { Message: OrderCancelledInputMessage m } } => new OrderCancelled(m.WorkflowId, m.Reason),

            { state: OrderCreated s, workflowEvent: Received { Message: PaymentTimeoutInputMessage m } } => new OrderCancelled(m.WorkflowId, "Payment_Timeout"),

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
                Send(new ProcessPayment(p.WorkflowId)),
                Send(new NotifyOrderPlaced(p.WorkflowId)),
                Schedule(TimeSpan.FromMinutes(15), new PaymentTimeoutOutMessage(p.WorkflowId))
            ],

            (PaymentReceivedInputMessage p, OrderCreated s) =>
            [
                Send(new ShipOrder(p.WorkflowId))
            ],

            (OrderShippedInputMessage p, PaymentConfirmed s) =>
            [
                Send(new NotifyOrderShipped(p.WorkflowId, p.TrackingNumber))
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
                    p.WorkflowId,
                    s switch
                    {
                        NoOrder => "NotExisting",
                        OrderCreated => "OrderCreated",
                        PaymentConfirmed => "PaymentConfirmed",
                        Shipped => "Shipped",
                        Delivered => "Delivered",
                        OrderCancelled c => $"Cancelled: {c.Reason}",
                        _ => "Unknown"
                    }
                ))
            ],

            _ => throw new NotImplementedException()
        };
    }
}
