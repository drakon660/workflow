using Carter;
using Workflow;
using Workflow.Samples.Order;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Add Carter
builder.Services.AddCarter();
builder.Services.AddSwaggerGen();

// Add workflow infrastructure and register workflows
builder.Services.AddWorkflow<OrderProcessingInputMessage, OrderProcessingState, OrderProcessingOutputMessage, OrderProcessingWorkflow>();
//builder.Services.<OrderProcessingInputMessage, OrderProcessingState, OrderProcessingOutputMessage, IOrderContext, OrderProcessingAsyncWorkflow>();

// For OrderContext (used by async workflow)
builder.Services.AddScoped<IOrderContext, OrderContext>();

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