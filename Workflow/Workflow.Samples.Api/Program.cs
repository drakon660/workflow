using Carter;
using Wolverine;
using Workflow;
using Workflow.InboxOutbox;
using Workflow.Samples.Api.Services;
using Workflow.Samples.Order;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddOpenApi();

// Add Carter
builder.Services.AddCarter();
builder.Services.AddSwaggerGen();

// Register workflow with type key for generic Wolverine handling
// This automatically registers: IWorkflowBus, IWorkflowProcessorFactory, IWorkflowTypeRegistry
builder.Services.AddWorkflow<OrderProcessingInputMessage, OrderProcessingState, OrderProcessingOutputMessage, OrderProcessingWorkflow>(
    workflowType: "OrderProcessing");

// Register output handler for processing workflow outputs (optional - for output delivery)
builder.Services.AddSingleton<Func<WorkflowMessage, CancellationToken, Task>>(
    provider => WorkflowOutputHandler.CreateHandler(provider));

// Configure workflow processor with output delivery
builder.Services.AddScoped<WorkflowProcessor<OrderProcessingInputMessage, OrderProcessingState, OrderProcessingOutputMessage>>(sp =>
{
    var repository = sp.GetRequiredService<WorkflowStreamRepository>();
    var workflow = sp.GetRequiredService<IWorkflow<OrderProcessingInputMessage, OrderProcessingState, OrderProcessingOutputMessage>>();
    var outputHandler = sp.GetRequiredService<Func<WorkflowMessage, CancellationToken, Task>>();
    var options = new WorkflowProcessorOptions
    {
        OutputDeliveryLimit = 10,
        OutputDeliveryTimeout = TimeSpan.FromSeconds(30)
    };

    return new WorkflowProcessor<OrderProcessingInputMessage, OrderProcessingState, OrderProcessingOutputMessage>(
        repository, workflow, outputHandler, options);
});

// For OrderContext (used by async workflow)
builder.Services.AddScoped<IOrderContext, OrderContext>();

// Add Wolverine with local queue configuration
builder.Host.UseWolverine(opts =>
{
    // Configure local queue for workflow inputs (generic handler)
    opts.LocalQueue("workflow-inputs")
        .Sequential(); // Process messages one at a time (ensures ordering)

    // Route WorkflowInputEnvelope to the local queue
    opts.PublishMessage<WorkflowInputEnvelope>()
        .ToLocalQueue("workflow-inputs");

    // Discovery - Wolverine will find WorkflowInputHandler automatically
    opts.Discovery.IncludeAssembly(typeof(WorkflowInputHandler).Assembly);
    opts.Discovery.IncludeAssembly(typeof(Program).Assembly);
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// Add Carter
app.MapCarter();

app.UseSwagger();
app.UseSwaggerUI();

app.Run();
