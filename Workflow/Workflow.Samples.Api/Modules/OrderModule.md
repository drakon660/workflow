# OrderModule API Documentation

This module provides REST endpoints for managing orders using Carter in ASP.NET Core.

## Endpoints

### 1. Get Order by ID
```http
GET /orders/{id:guid}
```

**Response:**
```json
{
  "orderId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "status": "OrderCreated",
  "createdAt": "2025-01-23T10:30:00Z",
  "trackingNumber": null,
  "customerId": "customer-123",
  "amount": 99.99,
  "items": ["item-1", "item-2"]
}
```

### 2. Create Order
```http
POST /orders
Content-Type: application/json

{
  "orderId": "optional-guid-or-null-to-auto-generate",
  "customerId": "customer-123",
  "amount": 99.99,
  "items": ["item-1", "item-2"]
}
```

**Response (201 Created):**
```json
{
  "orderId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "status": "OrderCreated",
  "message": "Order created and processing initiated"
}
```

### 3. Record Payment
```http
POST /orders/{id:guid}/payment
Content-Type: application/json

{
  "amount": 99.99,
  "paymentMethod": "credit_card",
  "transactionId": "txn-12345"
}
```

### 4. Ship Order
```http
POST /orders/{id:guid}/ship
Content-Type: application/json

{
  "trackingNumber": "1Z999AA10123456784",
  "carrier": "UPS"
}
```

### 5. Deliver Order
```http
POST /orders/{id:guid}/deliver
```

### 6. Cancel Order
```http
POST /orders/{id:guid}/cancel
Content-Type: application/json

{
  "reason": "Customer requested cancellation"
}
```

### 7. Get Order Status
```http
GET /orders/{id:guid}/status
```

**Response:**
```json
{
  "orderId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "status": "Delivered",
  "createdAt": "2025-01-23T10:30:00Z",
  "trackingNumber": "1Z999AA10123456784"
}
```

## Integration with Workflow Engine

### Current State (Scaffolded)
All endpoints currently return mock responses. They need to be integrated with the workflow engine:

1. **Create Order**: Should send `PlaceOrderInputMessage` to workflow
2. **Record Payment**: Should send `PaymentReceivedInputMessage` to workflow  
3. **Ship Order**: Should send `OrderShippedInputMessage` to workflow
4. **Deliver Order**: Should send `OrderDeliveredInputMessage` to workflow
5. **Cancel Order**: Should send `OrderCancelledInputMessage` to workflow
6. **Get Status**: Should use `CheckOrderStateInputMessage` with Reply pattern

### Required Workflow Integration

To make these endpoints functional, you need to:

1. **Register Workflows in Program.cs**:
```csharp
// Add workflow infrastructure and register workflows
builder.Services.AddWorkflow<OrderProcessingInputMessage, OrderProcessingState, OrderProcessingOutputMessage, OrderProcessingWorkflow>();
builder.Services.AddAsyncWorkflow<OrderProcessingInputMessage, OrderProcessingState, OrderProcessingOutputMessage, IOrderContext, OrderProcessingAsyncWorkflow>();

// For OrderContext (used by async workflow)
builder.Services.AddScoped<IOrderContext, OrderContext>();
```

2. **Inject Workflow Services**:
```csharp
public class OrderModule : ICarterModule
{
    private readonly IWorkflowPersistence<OrderProcessingInputMessage, OrderProcessingState, OrderProcessingOutputMessage> _persistence;
    private readonly WorkflowOrchestrator<OrderProcessingInputMessage, OrderProcessingState, OrderProcessingOutputMessage> _orchestrator;
    private readonly OrderProcessingWorkflow _workflow;
    
    // Constructor injection
}
```

3. **Handle Workflow Commands**:
```csharp
// Example for Create Order
private static async Task<IResult> CreateOrder(
    CreateOrderRequest request,
    [FromServices] WorkflowOrchestrator orchestrator,
    [FromServices] OrderProcessingWorkflow workflow,
    [FromServices] IWorkflowPersistence persistence,
    CancellationToken cancellationToken)
{
    // Generate order ID
    var orderId = request.OrderId ?? Guid.NewGuid();
    var placeOrder = new PlaceOrderInputMessage(orderId);
    
    // Process through workflow
    var result = await orchestrator.ProcessAsync(
        workflow,
        new WorkflowSnapshot<OrderProcessingState>(workflow.InitialState, Array.Empty<WorkflowEvent<OrderProcessingInputMessage, OrderProcessingOutputMessage>>()),
        placeOrder,
        begins: true
    );
    
    // Store workflow events
    var workflowId = $"order-{orderId}";
    var messages = ConvertToWorkflowMessages(workflowId, result.Events);
    await persistence.AppendAsync(workflowId, messages);
    
    // Return response
    return Results.Created($"/orders/{orderId}", new OrderCreatedResponse
    {
        OrderId = orderId,
        Status = "OrderCreated",
        Message = "Order created and processing initiated"
    });
}
```

3. **State Management**:
   - Load workflow state from persistence
   - Convert workflow state to API responses
   - Handle different order states appropriately

## Order States

Based on the workflow engine, orders can be in these states:

- **NoOrder** - Order doesn't exist
- **OrderCreated** - Order created, awaiting payment
- **PaymentConfirmed** - Payment received, awaiting shipping
- **Shipped** - Order shipped, with tracking number
- **Delivered** - Order delivered to customer
- **OrderCancelled** - Order cancelled with reason
- **InsufficientInventory** - Order cancelled due to insufficient inventory
- **AwaitingWarehouseInventory** - Waiting for warehouse inventory check

## Testing

Use the provided `.http` file in Visual Studio or VS Code to test endpoints:

1. Run the API: `dotnet run`
2. Open `Workflow.Samples.Api.http`
3. Execute requests in order to test the flow

## Next Steps

1. Implement workflow integration for all endpoints
2. Add proper error handling and validation
3. Add authentication/authorization if needed
4. Add rate limiting
5. Add comprehensive logging
6. Add integration tests
7. Add OpenAPI documentation enhancement