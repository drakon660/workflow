using Workflow.Fluent;

namespace Workflow.Samples.Order;

public class FluentOrderProcessingWorkflow : FluentWorkflow<OrderProcessingInputMessage, OrderProcessingState,
    OrderProcessingOutputMessage>
{
    public FluentOrderProcessingWorkflow()
    {
        Initially<NoOrder>()
            .On<PlaceOrderInputMessage>()
            .Execute(ctx=> 
                Send(new ProcessPayment(ctx.Message.OrderId)))
            .TransitionTo<OrderCreated>();
    }
}


// public class FluentOrderProcessingWorkflow2 : Workflow<OrderProcessingInputMessage, OrderProcessingState,
//     OrderProcessingOutputMessage>
// {
//     public override OrderProcessingState InitialState { get; } = new NoOrder();
//     
//     protected override OrderProcessingState InternalEvolve(OrderProcessingState state, WorkflowEvent<OrderProcessingInputMessage, OrderProcessingOutputMessage> workflowEvent)
//     {
//        
//         
//     }
//
//     public override IReadOnlyList<WorkflowCommand<OrderProcessingOutputMessage>> Decide(OrderProcessingInputMessage input, OrderProcessingState state)
//     {
//         throw new NotImplementedException();
//     }
// }