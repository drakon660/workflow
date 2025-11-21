using AwesomeAssertions;

namespace Workflow.Tests;

public class OrderProcessingWorkflowTests
{
    [Fact]
    public void Check_OrderProcessingWorkflow()
    {
        var workflow = new OrderProcessingWorkflow();
        var workflowOrchestrator = new WorkflowOrchestrator<OrderProcessingInputMessage, OrderProcessingState, OrderProcessingOutputMessage>();
        var snapshot = workflowOrchestrator.CreateInitialSnapshot(workflow);

        var orderId = "order-1";
        
        var result = workflowOrchestrator.Run(workflow, snapshot, new PlaceOrderInputMessage(orderId), true);
        
        result.Events.Count.Should().Be(5);  // ✅ Correct!

        // Check state changed
        result.Snapshot.State.Should().BeOfType<OrderCreated>();  // ✅
        var orderCreatedState = (OrderCreated)result.Snapshot.State;
        orderCreatedState.OrderId.Should().Be(orderId);  // ✅

        // Check commands generated
        result.Commands.Count.Should().Be(3);
    }
}