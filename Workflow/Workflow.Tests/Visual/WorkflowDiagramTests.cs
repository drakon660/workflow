using Workflow.Visual;

namespace Workflow.Tests.Visual;

public class WorkflowDiagramTests
{
    private const string OrderProcessingWorkflowSource = """
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
                        Reply(new OrderProcessingStatus(p.OrderId, "status"))
                    ],

                    _ => throw new NotImplementedException()
                };
            }
        }
        """;

    [Fact]
    public void Analyzer_ExtractsWorkflowName()
    {
        var builder = WorkflowDiagram.FromSource(OrderProcessingWorkflowSource);
        var model = builder.Model;

        Assert.Equal("OrderProcessingWorkflow", model.WorkflowName);
    }

    [Fact]
    public void Analyzer_ExtractsStateTransitions()
    {
        var builder = WorkflowDiagram.FromSource(OrderProcessingWorkflowSource);
        var model = builder.Model;

        Assert.NotEmpty(model.StateTransitions);

        // Check for specific transition: NoOrder -> OrderCreated
        var initialTransition = model.StateTransitions
            .FirstOrDefault(t => t.FromState == "NoOrder" && t.ToState == "OrderCreated");
        Assert.NotNull(initialTransition);
        Assert.Equal("InitiatedBy", initialTransition.EventType);
        Assert.Equal("PlaceOrderInputMessage", initialTransition.InputType);
    }

    [Fact]
    public void Analyzer_ExtractsAllStateTransitions()
    {
        var builder = WorkflowDiagram.FromSource(OrderProcessingWorkflowSource);
        var model = builder.Model;

        // Should have 6 transitions based on the source
        Assert.Equal(6, model.StateTransitions.Count);

        var fromStates = model.StateTransitions.Select(t => t.FromState).Distinct().ToList();
        Assert.Contains("NoOrder", fromStates);
        Assert.Contains("OrderCreated", fromStates);
        Assert.Contains("PaymentConfirmed", fromStates);
        Assert.Contains("Shipped", fromStates);
    }

    [Fact]
    public void Analyzer_ExtractsDecisionRules()
    {
        var builder = WorkflowDiagram.FromSource(OrderProcessingWorkflowSource);
        var model = builder.Model;

        Assert.NotEmpty(model.DecisionRules);

        // Check for PlaceOrder decision rule
        var placeOrderRule = model.DecisionRules
            .FirstOrDefault(r => r.InputType == "PlaceOrderInputMessage" && r.StateType == "NoOrder");
        Assert.NotNull(placeOrderRule);
        Assert.Equal(3, placeOrderRule.Commands.Count); // Send, Send, Schedule

        // Check command types
        var commandKinds = placeOrderRule.Commands.Select(c => c.Kind).ToList();
        Assert.Contains(CommandKind.Send, commandKinds);
        Assert.Contains(CommandKind.Schedule, commandKinds);
    }

    [Fact]
    public void Analyzer_ExtractsCompletionRules()
    {
        var builder = WorkflowDiagram.FromSource(OrderProcessingWorkflowSource);
        var model = builder.Model;

        // Find rules that have Complete command
        var completionRules = model.DecisionRules
            .Where(r => r.Commands.Any(c => c.Kind == CommandKind.Complete))
            .ToList();

        Assert.NotEmpty(completionRules);
        // Should have 3 completion paths: Delivered, Cancelled, Timeout
        Assert.Equal(3, completionRules.Count);
    }

    [Fact]
    public void Analyzer_ExtractsReplyCommands()
    {
        var builder = WorkflowDiagram.FromSource(OrderProcessingWorkflowSource);
        var model = builder.Model;

        // Find query rule with Reply
        var queryRule = model.DecisionRules
            .FirstOrDefault(r => r.InputType == "CheckOrderStateInputMessage");
        Assert.NotNull(queryRule);
        Assert.Single(queryRule.Commands);
        Assert.Equal(CommandKind.Reply, queryRule.Commands[0].Kind);
    }

    [Fact]
    public void Generator_CreatesValidStateTransitionsMermaid()
    {
        var mermaid = WorkflowDiagram
            .FromSource(OrderProcessingWorkflowSource)
            .GenerateStateTransitions();

        Assert.Contains("```mermaid", mermaid);
        Assert.Contains("flowchart TD", mermaid);
        Assert.Contains("NoOrder", mermaid);
        Assert.Contains("OrderCreated", mermaid);
        Assert.Contains("PaymentConfirmed", mermaid);
        Assert.Contains("Shipped", mermaid);
        Assert.Contains("Delivered", mermaid);
        Assert.Contains("Cancelled", mermaid);
        Assert.Contains("```", mermaid);
    }

    [Fact]
    public void Generator_CreatesValidDecisionTreeMermaid()
    {
        var mermaid = WorkflowDiagram
            .FromSource(OrderProcessingWorkflowSource)
            .GenerateDecisionTree();

        Assert.Contains("```mermaid", mermaid);
        Assert.Contains("flowchart TD", mermaid);
        Assert.Contains("Input Message + State", mermaid);
        Assert.Contains("Send", mermaid);
        Assert.Contains("Schedule", mermaid);
        Assert.Contains("Complete", mermaid);
    }

    [Fact]
    public void Generator_IncludesStylesByDefault()
    {
        var mermaid = WorkflowDiagram
            .FromSource(OrderProcessingWorkflowSource)
            .GenerateStateTransitions();

        Assert.Contains("style", mermaid);
        Assert.Contains("fill:", mermaid);
    }

    [Fact]
    public void Generator_CanExcludeStyles()
    {
        var mermaid = WorkflowDiagram
            .FromSource(OrderProcessingWorkflowSource)
            .WithOptions(o => o.WithStyles(false))
            .GenerateStateTransitions();

        Assert.DoesNotContain("style", mermaid);
    }

    [Fact]
    public void Generator_GenerateAll_CombinesBothDiagrams()
    {
        var mermaid = WorkflowDiagram
            .FromSource(OrderProcessingWorkflowSource)
            .GenerateAll();

        // Should have both sections
        Assert.Contains("State Transitions", mermaid);
        Assert.Contains("Decision Rules", mermaid);

        // Should have two mermaid blocks
        var mermaidCount = mermaid.Split("```mermaid").Length - 1;
        Assert.Equal(2, mermaidCount);
    }

    [Fact]
    public void Demo_PrintStateTransitions()
    {
        var mermaid = WorkflowDiagram
            .FromSource(OrderProcessingWorkflowSource)
            .GenerateStateTransitions();

        // Output for visual inspection
        Console.WriteLine("=== State Transitions ===");
        Console.WriteLine(mermaid);
    }

    [Fact]
    public void Demo_PrintDecisionTree()
    {
        var mermaid = WorkflowDiagram
            .FromSource(OrderProcessingWorkflowSource)
            .GenerateDecisionTree();

        // Output for visual inspection
        Console.WriteLine("=== Decision Tree ===");
        Console.WriteLine(mermaid);
    }
}
