// See https://aka.ms/new-console-template for more information

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;
//using WorkflowWolverineSingle;

var builder = Host.CreateApplicationBuilder(args);
builder.UseWolverine(_ => { });
//builder.Services.AddHostedService<BpPublisher>();
builder.Services.AddOptions();
builder.Services.AddLogging();

var app = builder.Build();
app.Run();


public record PlaceOrder(string OrderId);