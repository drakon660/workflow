# Workflow Extensions Documentation

## Overview

The `WorkflowExtensions` class provides extension methods for registering workflow services in ASP.NET Core dependency injection container.

## Available Extension Methods

### 1. AddWorkflow<TInput, TState, TOutput>(IServiceCollection)

**Purpose**: Registers only workflow infrastructure components (no user workflow).

**Usage**: Use when you want to register multiple workflow types separately or when you have custom registration logic.

**Registers**:
- `WorkflowStreamRepository` (Scoped)
- `WorkflowOrchestrator<TInput, TState, TOutput>` (Scoped)  
- `WorkflowProcessor<TInput, TState, TOutput>` (Scoped)

**Example**:
```csharp
// Register infrastructure only
builder.Services.AddWorkflow<OrderProcessingInputMessage, OrderProcessingState, OrderProcessingOutputMessage>();

// Register workflows separately
builder.Services.AddWorkflowImplementation<OrderProcessingInputMessage, OrderProcessingState, OrderProcessingOutputMessage, OrderProcessingWorkflow>();
builder.Services.AddWorkflowImplementation<AnotherInput, AnotherState, AnotherOutput, AnotherWorkflow>();
```

### 2. AddWorkflow<TInput, TState, TOutput, TWorkflow>(IServiceCollection)

**Purpose**: Registers workflow infrastructure AND a user workflow implementation in one call.

**Usage**: Most common scenario - single workflow type in the application.

**Registers**:
- All infrastructure components (from method 1)
- `IWorkflow<TInput, TState, TOutput>` → `TWorkflow` (Scoped)
- `TWorkflow` (Scoped) - for direct injection

**Example**:
```csharp
// Register infrastructure + workflow implementation
builder.Services.AddWorkflow<OrderProcessingInputMessage, OrderProcessingState, OrderProcessingOutputMessage, OrderProcessingWorkflow>();
```

### 3. AddAsyncWorkflow<TInput, TState, TOutput, TContext, TWorkflow>(IServiceCollection)

**Purpose**: Registers workflow infrastructure AND a user async workflow implementation.

**Usage**: When using async workflows with context injection.

**Registers**:
- `WorkflowStreamRepository` (Scoped)
- `AsyncWorkflowOrchestrator<TInput, TState, TOutput, TContext>` (Scoped)
- `IAsyncWorkflow<TInput, TState, TOutput, TContext>` → `TWorkflow` (Scoped)
- `TWorkflow` (Scoped) - for direct injection

**Example**:
```csharp
// Register infrastructure + async workflow
builder.Services.AddAsyncWorkflow<OrderProcessingInputMessage, OrderProcessingState, OrderProcessingOutputMessage, IOrderContext, OrderProcessingAsyncWorkflow>();

// Don't forget to register the context
builder.Services.AddScoped<IOrderContext, OrderContext>();
```

### 4. AddWorkflowImplementation<TInput, TState, TOutput, TWorkflow>(IServiceCollection)

**Purpose**: Registers only the user workflow implementation (assumes infrastructure already registered).

**Usage**: When you have multiple workflow types and registered infrastructure once.

**Example**:
```csharp
// Register infrastructure once
builder.Services.AddWorkflow<OrderProcessingInputMessage, OrderProcessingState, OrderProcessingOutputMessage>();

// Register multiple workflows
builder.Services.AddWorkflowImplementation<OrderProcessingInputMessage, OrderProcessingState, OrderProcessingOutputMessage, OrderProcessingWorkflow>();
builder.Services.AddWorkflowImplementation<InvoiceInput, InvoiceState, InvoiceOutput, InvoiceWorkflow>();
```

### 5. AddAsyncWorkflowImplementation<TInput, TState, TOutput, TContext, TWorkflow>(IServiceCollection)

**Purpose**: Registers only the user async workflow implementation.

**Usage**: When you have multiple async workflow types and registered infrastructure once.

**Example**:
```csharp
// Register infrastructure once
builder.Services.AddAsyncWorkflow<OrderProcessingInputMessage, OrderProcessingState, OrderProcessingOutputMessage, IOrderContext>();

// Register multiple async workflows
builder.Services.AddAsyncWorkflowImplementation<OrderProcessingInputMessage, OrderProcessingState, OrderProcessingOutputMessage, IOrderContext, OrderProcessingAsyncWorkflow>();
builder.Services.AddAsyncWorkflowImplementation<InvoiceInput, InvoiceState, InvoiceOutput, IInvoiceContext, InvoiceAsyncWorkflow>();
```

## Usage Patterns

### Single Workflow Application

```csharp
var builder = WebApplication.CreateBuilder(args);

// Register workflow and infrastructure in one call
builder.Services.AddWorkflow<OrderInput, OrderState, OrderOutput, OrderWorkflow>();

var app = builder.Build();
app.Run();
```

### Multiple Workflow Application

```csharp
var builder = WebApplication.CreateBuilder(args);

// Option A: Register infrastructure once, then workflows
builder.Services.AddWorkflow<OrderProcessingInputMessage, OrderProcessingState, OrderProcessingOutputMessage>();
builder.Services.AddWorkflowImplementation<OrderProcessingInputMessage, OrderProcessingState, OrderProcessingOutputMessage, OrderProcessingWorkflow>();
builder.Services.AddWorkflowImplementation<InvoiceInput, InvoiceState, InvoiceOutput, InvoiceWorkflow>();

// Option B: Use the combined methods multiple times (registers infrastructure each time)
builder.Services.AddWorkflow<OrderProcessingInputMessage, OrderProcessingState, OrderProcessingOutputMessage, OrderProcessingWorkflow>();
builder.Services.AddWorkflow<InvoiceInput, InvoiceState, InvoiceOutput, InvoiceWorkflow>();
```

### Mixed Sync/Async Workflows

```csharp
var builder = WebApplication.CreateBuilder(args);

// Register sync workflow
builder.Services.AddWorkflow<OrderProcessingInputMessage, OrderProcessingState, OrderProcessingOutputMessage, OrderProcessingWorkflow>();

// Register async workflow with context
builder.Services.AddAsyncWorkflow<InvoiceInput, InvoiceState, InvoiceOutput, IInvoiceContext, InvoiceAsyncWorkflow>();

// Register context services
builder.Services.AddScoped<IOrderContext, OrderContext>();
builder.Services.AddScoped<IInvoiceContext, InvoiceContext>();
```

## Service Lifetime

All services are registered as **Scoped** by default:

- **Scoped**: One instance per HTTP request (recommended for web applications)
- Services maintain consistency within a single request
- Thread-safe for concurrent requests

If you need different lifetimes, you can override:

```csharp
// Register as singleton (not recommended for most scenarios)
builder.Services.AddSingleton<IWorkflow<OrderInput, OrderState, OrderOutput>, OrderWorkflow>();

// Register as transient (new instance every injection)
builder.Services.AddTransient<IWorkflow<OrderInput, OrderState, OrderOutput>, OrderWorkflow>();
```

## Dependency Injection in Workflows

Since workflows are registered as scoped, they can depend on other scoped services:

```csharp
public class OrderProcessingWorkflow : Workflow<OrderInput, OrderState, OrderOutput>
{
    private readonly ILogger<OrderProcessingWorkflow> _logger;
    private readonly ITimeProvider _timeProvider;

    public OrderProcessingWorkflow(ILogger<OrderProcessingWorkflow> logger, ITimeProvider timeProvider)
    {
        _logger = logger;
        _timeProvider = timeProvider;
    }

    // ... workflow implementation
}
```

**Note**: Be careful with service lifetimes. A scoped workflow shouldn't depend on a singleton service that maintains state.

## Testing Support

For unit testing, you can use the extensions to set up test DI container:

```csharp
public class OrderWorkflowTests
{
    private readonly ServiceProvider _serviceProvider;
    private readonly OrderProcessingWorkflow _workflow;
    private readonly WorkflowOrchestrator<OrderInput, OrderState, OrderOutput> _orchestrator;

    public OrderWorkflowTests()
    {
        var services = new ServiceCollection();
        
        // Register workflow for testing
        services.AddWorkflow<OrderInput, OrderState, OrderOutput, OrderProcessingWorkflow>();
        
        // Add test doubles
        services.AddScoped<ITimeProvider, TestTimeProvider>();
        
        _serviceProvider = services.BuildServiceProvider();
        _workflow = _serviceProvider.GetRequiredService<OrderProcessingWorkflow>();
        _orchestrator = _serviceProvider.GetRequiredService<WorkflowOrchestrator<OrderInput, OrderState, OrderOutput>>();
    }
    
    [Fact]
    public void Should_Handle_PlaceOrder()
    {
        // Test workflow using injected services
        var commands = _workflow.Decide(new PlaceOrder("123"), _workflow.InitialState);
        // ... assertions
    }
}
```

## Migration Guide

### From Manual Registration (Before)

```csharp
// Old way - manual registration
builder.Services.AddScoped<WorkflowStreamRepository>();
builder.Services.AddScoped<WorkflowOrchestrator<OrderInput, OrderState, OrderOutput>>();
builder.Services.AddScoped<IWorkflow<OrderInput, OrderState, OrderOutput>, OrderWorkflow>();
builder.Services.AddScoped<OrderWorkflow>();
```

### To Extension Methods (After)

```csharp
// New way - using extensions
builder.Services.AddWorkflow<OrderInput, OrderState, OrderOutput, OrderWorkflow>();
```

## Best Practices

1. **Use the generic method when you have a single workflow type**
2. **Register infrastructure once when you have multiple workflow types**
3. **Keep workflows stateless** (state is managed by workflow engine)
4. **Use scoped lifetime** for web applications
5. **Register context services for async workflows**
6. **Test with the same DI setup** as production

## Troubleshooting

### Issue: "No service for type 'IWorkflow<TInput, TState, TOutput>' has been registered"

**Solution**: Make sure you're using one of the workflow extension methods to register your workflow implementation.

```csharp
// ❌ This only registers infrastructure
builder.Services.AddWorkflow<OrderInput, OrderState, OrderOutput>();

// ✅ Register the workflow implementation
builder.Services.AddWorkflow<OrderInput, OrderState, OrderOutput, OrderWorkflow>();
// OR
builder.Services.AddWorkflowImplementation<OrderInput, OrderState, OrderOutput, OrderWorkflow>();
```

### Issue: Multiple registrations of infrastructure components

**Concern**: Calling `AddWorkflow<TInput, TState, TOutput, TWorkflow>()` multiple times will register infrastructure multiple times.

**Solution**: Either use the separate pattern (register infrastructure once) or don't worry - DI handles duplicate registrations gracefully.

```csharp
// Option 1: Don't worry (recommended for simplicity)
builder.Services.AddWorkflow<OrderInput, OrderState, OrderOutput, OrderWorkflow>();
builder.Services.AddWorkflow<InvoiceInput, InvoiceState, InvoiceOutput, InvoiceWorkflow>();

// Option 2: Register once
builder.Services.AddWorkflow<OrderInput, OrderState, OrderOutput>();
builder.Services.AddWorkflowImplementation<OrderInput, OrderState, OrderOutput, OrderWorkflow>();
builder.Services.AddWorkflowImplementation<InvoiceInput, InvoiceState, InvoiceOutput, InvoiceWorkflow>();
```