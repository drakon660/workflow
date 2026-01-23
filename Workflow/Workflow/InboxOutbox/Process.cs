// namespace Workflow.Inbox;
//
// 1. Router Function (Workflow ID Detection)
// public class GroupCheckoutRouter : IMessageRouter
// {
//     public string Route(object message)
//     {
//         return message switch
//         {
//             InitiateGroupCheckout cmd => $"group-checkout-{cmd.GroupId}",
//             GuestCheckedOut evt => $"group-checkout-{evt.GroupId}",
//             GuestCheckoutFailed evt => $"group-checkout-{evt.GroupId}",
//             _ => null
//         };
//     }
// }
// 2. Workflow Stream as Inbox/Outbox
// Wolverine's durability already provides this pattern:
// public class GroupCheckoutWorkflow
// {
//     // This IS the workflow stream processor
//     public async Task<GroupCheckoutState> Handle(
//         GroupCheckoutMessage input, 
//         GroupCheckoutState state, 
//         IMessageBus bus)
//     {
//         var newState = Evolve(state, input);
//         var outputs = Decide(input, newState);
//         
//         // Store outputs to same stream (Wolverine handles this)
//         foreach (var output in outputs)
//         {
//             await bus.Publish(output);
//         }
//         
//         return newState;
//     }
// }
// 3. Double-Hop with Wolverine Transports
// // Configure the routing
// services.AddWolverine(opts =>
// {
//     // Step 1-2: Input message arrives → Router determines workflow
//     opts.UseRabbitMq()
//        .BindToExchange("events")
//        .ListenToQueue("guest-events");
//     
//     // Step 3-4: Store input → Consumer delivers from workflow stream
//     opts.ListenToMessages<GroupCheckoutMessage>()
//        .RouteToSpecificQueue(r => r.Route(message));
//     
//     // Step 7-8: Store outputs → Output processor sends
//     opts.PublishAllMessages()
//        .ToRabbitMqExchange("workflows");
// });
// 4. State Management with Wolverine + Marten
// // Wolverine handles the stream persistence
// public class GroupCheckoutState
// {
//     public string Status { get; set; }
//     public Dictionary<string, GuestStatus> GuestStays { get; set; }
//     
//     // Evolve function equivalent
//     public GroupCheckoutState Apply(GroupCheckoutMessage message)
//     {
//         return message switch
//         {
//             GroupCheckoutInitiated => Initiated(message),
//             GuestCheckedOut => Completed(message),
//             GuestCheckoutFailed => Failed(message),
//             _ => this
//         };
//     }
// }
// 5. Complete Flow Configuration
// services.AddWolverine(opts =>
// {
//     // Configure durable execution (RFC's exactly-once delivery)
//     opts.PersistMessagesWithPostgresql(connectionString)
//        .EnrollInDurable InboxOutbox();
//     
//     // Prevent concurrent execution (RFC's advisory locks)
//     opts.DisableConcurrentHandlingOnSingleGroup();
//     
//     // Configure workflow routing (RFC's double-hop)
//     opts.LocalRouting.AddRouting<GroupCheckoutMessage>(
//         msg => $"workflow-{GetGroupId(msg)}");
// });
// Key Advantages with Wolverine
// Built-in Durability: The inbox/outbox pattern is native - no extra infrastructure needed.
// Automatic Correlation: Wolverine handles message routing and correlation out of the box.
// Performance: Runtime code generation makes the double-hop efficient.
// Observability: Built-in metrics and tracing for each hop in the flow.
// Recovery: Wolverine's durability agent handles crash recovery automatically.
// The RFC's double-hop pattern maps naturally to Wolverine's architecture - you get the same benefits with less custom code and better performance.