// using Microsoft.Extensions.Hosting;
// using Wolverine;
//
// namespace WorkflowWolverineSingle;
//
// public class BpPublisher(IMessageBus messageBus) : BackgroundService
// {
//     private readonly IMessageBus _messageBus = messageBus;
//
//     protected override async Task ExecuteAsync(CancellationToken stoppingToken)
//     {
//         while (!stoppingToken.IsCancellationRequested)
//         {
//             await _messageBus.SendAsync(new CreateCustomer(Guid.NewGuid(), Path.GetRandomFileName()) , new DeliveryOptions()
//             {
//                 
//             });
//             
//             await Task.Delay(2000, stoppingToken);
//         }
//     }
// }