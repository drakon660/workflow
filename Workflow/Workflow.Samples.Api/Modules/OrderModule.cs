using Carter;
using Microsoft.AspNetCore.Mvc;
using Workflow.InboxOutbox;
using Workflow.Samples.Order;

namespace Workflow.Samples.Api.Modules;

public class OrderModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/orders/{id:guid}", GetOrderById)
            .WithName("GetOrderById")
            .Produces<OrderStatusResponse>(200)
            .Produces(404)
            .WithTags("Orders")
            .WithSummary("Get order status by ID")
            .WithDescription("Retrieves the current status and details of an order");

        app.MapPost("/orders", CreateOrder)
            .WithName("CreateOrder")
            .Accepts<CreateOrderRequest>("application/json")
            .Produces<OrderCreatedResponse>(201)
            .Produces(400)
            .WithTags("Orders")
            .WithSummary("Create a new order")
            .WithDescription("Creates a new order and initiates the order processing workflow");

        app.MapPost("/orders/{id:guid}/payment", RecordPayment)
            .WithName("RecordPayment")
            .Accepts<RecordPaymentRequest>("application/json")
            .Produces(200)
            .Produces(400)
            .Produces(404)
            .WithTags("Orders")
            .WithSummary("Record payment for an order")
            .WithDescription("Records that payment has been received for an order");

        app.MapPost("/orders/{id:guid}/ship", ShipOrder)
            .WithName("ShipOrder")
            .Accepts<ShipOrderRequest>("application/json")
            .Produces(200)
            .Produces(400)
            .Produces(404)
            .WithTags("Orders")
            .WithSummary("Ship an order")
            .WithDescription("Records that an order has been shipped with tracking information");

        app.MapPost("/orders/{id:guid}/deliver", DeliverOrder)
            .WithName("DeliverOrder")
            .Produces(200)
            .Produces(400)
            .Produces(404)
            .WithTags("Orders")
            .WithSummary("Mark order as delivered")
            .WithDescription("Marks an order as delivered");

        app.MapPost("/orders/{id:guid}/cancel", CancelOrder)
            .WithName("CancelOrder")
            .Accepts<CancelOrderRequest>("application/json")
            .Produces(200)
            .Produces(400)
            .Produces(404)
            .WithTags("Orders")
            .WithSummary("Cancel an order")
            .WithDescription("Cancels an order with a specified reason");

        app.MapGet("/orders/{id:guid}/status", GetOrderStatus)
            .WithName("GetOrderStatus")
            .Produces<OrderStatusResponse>(200)
            .Produces(404)
            .WithTags("Orders")
            .WithSummary("Get order status")
            .WithDescription("Retrieves detailed status information for an order");

        app.MapGet("/all-messages", GetAllMessages)
            .WithName("GetAllMessages");
    }

    private static async Task<IResult> GetOrderById(Guid id, CancellationToken cancellationToken)
    {
        // TODO: Load order state from persistence
        await Task.Delay(1, cancellationToken);
        return Results.Ok(new OrderStatusResponse
        {
            OrderId = id,
            Status = "NotExisting",
            CreatedAt = DateTime.UtcNow,
            TrackingNumber = null
        });
    }

    private static async Task<IResult> CreateOrder(
        CreateOrderRequest request,
        [FromServices] IWorkflowBus workflowBus,
        CancellationToken cancellationToken)
    {
        var orderId = request.OrderId ?? Guid.NewGuid();
        var orderIdStr = orderId.ToString();

        // Clean API - no envelope creation needed
        await workflowBus.SendAsync(orderIdStr, new PlaceOrderInputMessage(orderIdStr), cancellationToken);

        return Results.Created($"/orders/{orderId}", new OrderCreatedResponse
        {
            OrderId = orderId,
            Status = "OrderCreated",
            Message = "Order queued for processing"
        });
    }

    private static async Task<IResult> RecordPayment(
        Guid id,
        RecordPaymentRequest request,
        [FromServices] IWorkflowBus workflowBus,
        CancellationToken cancellationToken)
    {
        var orderIdStr = id.ToString();

        await workflowBus.SendAsync(orderIdStr, new PaymentReceivedInputMessage(orderIdStr), cancellationToken);

        return Results.Ok(new { Message = "Payment recorded successfully" });
    }

    private static async Task<IResult> ShipOrder(
        Guid id,
        ShipOrderRequest request,
        [FromServices] IWorkflowBus workflowBus,
        CancellationToken cancellationToken)
    {
        var orderIdStr = id.ToString();

        await workflowBus.SendAsync(orderIdStr, new OrderShippedInputMessage(orderIdStr, request.TrackingNumber), cancellationToken);

        return Results.Ok(new { Message = "Order shipped successfully" });
    }

    private static async Task<IResult> DeliverOrder(
        Guid id,
        [FromServices] IWorkflowBus workflowBus,
        CancellationToken cancellationToken)
    {
        var orderIdStr = id.ToString();

        await workflowBus.SendAsync(orderIdStr, new OrderDeliveredInputMessage(orderIdStr), cancellationToken);

        return Results.Ok(new { Message = "Order marked as delivered successfully" });
    }

    private static async Task<IResult> CancelOrder(
        Guid id,
        CancelOrderRequest request,
        [FromServices] IWorkflowBus workflowBus,
        CancellationToken cancellationToken)
    {
        var orderIdStr = id.ToString();

        await workflowBus.SendAsync(orderIdStr, new OrderCancelledInputMessage(orderIdStr, request.Reason), cancellationToken);

        return Results.Ok(new { Message = "Order cancelled successfully" });
    }

    private static async Task<IResult> GetOrderStatus(
        Guid id,
        [FromServices] IWorkflowBus workflowBus,
        CancellationToken cancellationToken)
    {
        var orderIdStr = id.ToString();

        await workflowBus.SendAsync(orderIdStr, new CheckOrderStateInputMessage(orderIdStr), cancellationToken);

        // For now, return immediate response
        return Results.Ok(new OrderStatusResponse
        {
            OrderId = id,
            Status = "StatusRequested",
            CreatedAt = DateTime.UtcNow
        });
    }

    private static IResult GetAllMessages(string id, WorkflowStreamRepository workflowStreamRepository)
    {
        var allMessages = workflowStreamRepository.GetAll(id);
        return Results.Ok(allMessages.GetAllMessages());
    }
}

// Request/Response DTOs
public record CreateOrderRequest
{
    public Guid? OrderId { get; init; }
    public string CustomerId { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public List<string> Items { get; init; } = new();
}

public record OrderCreatedResponse
{
    public Guid OrderId { get; init; }
    public string Status { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

public record RecordPaymentRequest
{
    public decimal Amount { get; init; }
    public string PaymentMethod { get; init; } = string.Empty;
    public string TransactionId { get; init; } = string.Empty;
}

public record ShipOrderRequest
{
    public string TrackingNumber { get; init; } = string.Empty;
    public string Carrier { get; init; } = string.Empty;
}

public record CancelOrderRequest
{
    public string Reason { get; init; } = string.Empty;
}

public record OrderStatusResponse
{
    public Guid OrderId { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public string? TrackingNumber { get; init; }
    public string? CustomerId { get; init; }
    public decimal? Amount { get; init; }
    public List<string> Items { get; init; } = new();
}
