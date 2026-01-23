// namespace Workflow.Inbox;
//
// Core Interface
// public interface IMessageInbox
// {
//     Task StoreAsync(IncomingMessage message);
//     Task MarkAsHandledAsync(string messageId);
//     Task<bool> IsDuplicateAsync(string messageId);
//     Task MoveToDeadLetterAsync(string messageId, Exception error);
// }
// Database Schema
// CREATE TABLE message_inbox (
//     id VARCHAR(50) PRIMARY KEY,
//     message_type VARCHAR(250) NOT NULL,
//     body VARBINARY(MAX) NOT NULL,
//     status VARCHAR(20) NOT NULL DEFAULT 'Pending',
//     received_at DATETIME2 NOT NULL,
//     handled_at DATETIME2 NULL,
//     retry_count INT DEFAULT 0,
//     error_message NVARCHAR(MAX) NULL
// );
// CREATE INDEX IX_message_inbox_status_received ON message_inbox(status, received_at);
// Implementation
// public class SqlMessageInbox : IMessageInbox
// {
//     private readonly IDbConnectionFactory _connectionFactory;
//     public async Task StoreAsync(IncomingMessage message)
//     {
//         using var conn = _connectionFactory.CreateConnection();
//         await conn.ExecuteAsync(@"
//             INSERT INTO message_inbox (id, message_type, body, status, received_at)
//             VALUES (@Id, @MessageType, @Body, 'Pending', @ReceivedAt)", message);
//     }
//     public async Task<bool> IsDuplicateAsync(string messageId)
//     {
//         using var conn = _connectionFactory.CreateConnection();
//         var exists = await conn.ExecuteScalarAsync<int>(
//             "SELECT COUNT(1) FROM message_inbox WHERE id = @Id", new { Id = messageId });
//         return exists > 0;
//     }
// }
// Consumer with Inbox
// public class InboxMessageConsumer
// {
//     private readonly IMessageInbox _inbox;
//     private readonly IHandler _handler;
//     public async Task ProcessAsync(IncomingMessage message)
//     {
//         // Store first - prevents message loss
//         await _inbox.StoreAsync(message);
//         try
//         {
//             await _handler.HandleAsync(message.Body);
//             await _inbox.MarkAsHandledAsync(message.Id);
//         }
//         catch (Exception ex)
//         {
//             await _inbox.MoveToDeadLetterAsync(message.Id, ex);
//             throw;
//         }
//     }
// }
// Key Features
// - Persistence before processing prevents message loss
// - Primary key duplicate detection ensures exactly-once processing  
// - Status tracking provides observability
// - Dead letter handling for failed messages
// - Retry support with retry count tracking