// using Microsoft.Extensions.Logging;
//
// namespace WorkflowWolverineSingle;
//
// public class CreateCustomerHandler(ILogger<CreateCustomerHandler> logger)
// {
//     private readonly ILogger<CreateCustomerHandler> _logger = logger;
//
//     public async Task<CreateCustomerCreated> Handle(CreateCustomer createCustomer)
//     {
//         _logger.LogInformation("Create customer");
//         _logger.LogInformation(createCustomer.ToString());
//         return new CreateCustomerCreated(createCustomer.Guid, createCustomer.ToString());
//     } 
// }
// public class CustomerCreatedHandler(ILogger<CustomerCreatedHandler> logger)
// {
//     private readonly ILogger<CustomerCreatedHandler> _logger = logger;
//
//     public async Task Handle(CreateCustomer createCustomer)
//     {
//         _logger.LogInformation("Customer created");
//     } 
// }