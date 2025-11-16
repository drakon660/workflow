using Xunit;

namespace Workflow.Tests;

public class InMemoryWorkflowPersistenceTests
{
    private readonly InMemoryWorkflowPersistence<string, int, string> _persistence;

    public InMemoryWorkflowPersistenceTests()
    {
        _persistence = new InMemoryWorkflowPersistence<string, int, string>();
    }

    [Fact]
    public async Task AppendAsync_ShouldAssignSequentialPositions()
    {
        // Arrange
        var workflowId = "workflow-1";
        var messages = new[]
        {
            CreateMessage(workflowId, MessageKind.Event, MessageDirection.Input, "Input1"),
            CreateMessage(workflowId, MessageKind.Event, MessageDirection.Output, "Output1")
        };

        // Act
        var lastPosition = await _persistence.AppendAsync(workflowId, messages);

        // Assert
        Assert.Equal(2, lastPosition);

        var storedMessages = await _persistence.ReadStreamAsync(workflowId);
        Assert.Equal(2, storedMessages.Count);
        Assert.Equal(1, storedMessages[0].Position);
        Assert.Equal(2, storedMessages[1].Position);
    }

    [Fact]
    public async Task AppendAsync_ConcurrentWrites_ShouldBeThreadSafe()
    {
        // Arrange
        var workflowId = "workflow-concurrent";
        var tasks = new List<Task>();

        // Act - Simulate 10 concurrent appends
        for (int i = 0; i < 10; i++)
        {
            var messageIndex = i;
            tasks.Add(Task.Run(async () =>
            {
                var messages = new[]
                {
                    CreateMessage(workflowId, MessageKind.Event, MessageDirection.Input, $"Message-{messageIndex}")
                };
                await _persistence.AppendAsync(workflowId, messages);
            }));
        }

        await Task.WhenAll(tasks);

        // Assert
        var storedMessages = await _persistence.ReadStreamAsync(workflowId);
        Assert.Equal(10, storedMessages.Count);

        // Verify positions are sequential and unique
        var positions = storedMessages.Select(m => m.Position).OrderBy(p => p).ToList();
        Assert.Equal(Enumerable.Range(1, 10).Select(i => (long)i), positions);
    }

    [Fact]
    public async Task ReadStreamAsync_WithFromPosition_ShouldReturnMessagesFromPosition()
    {
        // Arrange
        var workflowId = "workflow-2";
        var messages = new[]
        {
            CreateMessage(workflowId, MessageKind.Event, MessageDirection.Input, "Message1"),
            CreateMessage(workflowId, MessageKind.Event, MessageDirection.Input, "Message2"),
            CreateMessage(workflowId, MessageKind.Event, MessageDirection.Input, "Message3")
        };
        await _persistence.AppendAsync(workflowId, messages);

        // Act
        var result = await _persistence.ReadStreamAsync(workflowId, fromPosition: 2);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal(2, result[0].Position);
        Assert.Equal(3, result[1].Position);
    }

    [Fact]
    public async Task ReadStreamAsync_NonExistentWorkflow_ShouldReturnEmpty()
    {
        // Act
        var result = await _persistence.ReadStreamAsync("non-existent");

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetPendingCommandsAsync_ShouldReturnOnlyUnprocessedOutputCommands()
    {
        // Arrange
        var workflowId = "workflow-3";
        var messages = new[]
        {
            CreateMessage(workflowId, MessageKind.Event, MessageDirection.Input, "Event1"),
            CreateMessage(workflowId, MessageKind.Command, MessageDirection.Output, "Command1", processed: false),
            CreateMessage(workflowId, MessageKind.Command, MessageDirection.Output, "Command2", processed: false),
            CreateMessage(workflowId, MessageKind.Command, MessageDirection.Input, "Command3", processed: false),
            CreateMessage(workflowId, MessageKind.Event, MessageDirection.Output, "Event2")
        };
        await _persistence.AppendAsync(workflowId, messages);

        // Act
        var pending = await _persistence.GetPendingCommandsAsync();

        // Assert
        Assert.Equal(2, pending.Count);
        Assert.All(pending, cmd =>
        {
            Assert.Equal(MessageKind.Command, cmd.Kind);
            Assert.Equal(MessageDirection.Output, cmd.Direction);
            Assert.False(cmd.Processed);
        });
    }

    [Fact]
    public async Task GetPendingCommandsAsync_WithWorkflowIdFilter_ShouldReturnOnlyForThatWorkflow()
    {
        // Arrange
        await _persistence.AppendAsync("workflow-A", new[]
        {
            CreateMessage("workflow-A", MessageKind.Command, MessageDirection.Output, "CommandA", processed: false)
        });

        await _persistence.AppendAsync("workflow-B", new[]
        {
            CreateMessage("workflow-B", MessageKind.Command, MessageDirection.Output, "CommandB", processed: false)
        });

        // Act
        var pendingForA = await _persistence.GetPendingCommandsAsync("workflow-A");

        // Assert
        Assert.Single(pendingForA);
        Assert.Equal("workflow-A", pendingForA[0].WorkflowId);
    }

    [Fact]
    public async Task MarkCommandProcessedAsync_ShouldUpdateProcessedFlag()
    {
        // Arrange
        var workflowId = "workflow-4";
        var messages = new[]
        {
            CreateMessage(workflowId, MessageKind.Command, MessageDirection.Output, "Command1", processed: false)
        };
        await _persistence.AppendAsync(workflowId, messages);

        // Act
        await _persistence.MarkCommandProcessedAsync(workflowId, position: 1);

        // Assert
        var pending = await _persistence.GetPendingCommandsAsync(workflowId);
        Assert.Empty(pending);

        var allMessages = await _persistence.ReadStreamAsync(workflowId);
        Assert.True(allMessages[0].Processed);
    }

    [Fact]
    public async Task MarkCommandProcessedAsync_NonExistentWorkflow_ShouldThrow()
    {
        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _persistence.MarkCommandProcessedAsync("non-existent", 1));
    }

    [Fact]
    public async Task MarkCommandProcessedAsync_NonExistentPosition_ShouldThrow()
    {
        // Arrange
        var workflowId = "workflow-5";
        await _persistence.AppendAsync(workflowId, new[]
        {
            CreateMessage(workflowId, MessageKind.Event, MessageDirection.Input, "Event1")
        });

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _persistence.MarkCommandProcessedAsync(workflowId, 999));
    }

    [Fact]
    public async Task MarkCommandProcessedAsync_NotACommand_ShouldThrow()
    {
        // Arrange
        var workflowId = "workflow-6";
        await _persistence.AppendAsync(workflowId, new[]
        {
            CreateMessage(workflowId, MessageKind.Event, MessageDirection.Input, "Event1")
        });

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _persistence.MarkCommandProcessedAsync(workflowId, 1));
    }

    [Fact]
    public async Task ExistsAsync_ExistingWorkflow_ShouldReturnTrue()
    {
        // Arrange
        var workflowId = "workflow-7";
        await _persistence.AppendAsync(workflowId, new[]
        {
            CreateMessage(workflowId, MessageKind.Event, MessageDirection.Input, "Event1")
        });

        // Act
        var exists = await _persistence.ExistsAsync(workflowId);

        // Assert
        Assert.True(exists);
    }

    [Fact]
    public async Task ExistsAsync_NonExistentWorkflow_ShouldReturnFalse()
    {
        // Act
        var exists = await _persistence.ExistsAsync("non-existent");

        // Assert
        Assert.False(exists);
    }

    [Fact]
    public async Task DeleteAsync_ShouldRemoveWorkflowStream()
    {
        // Arrange
        var workflowId = "workflow-8";
        await _persistence.AppendAsync(workflowId, new[]
        {
            CreateMessage(workflowId, MessageKind.Event, MessageDirection.Input, "Event1")
        });

        // Act
        await _persistence.DeleteAsync(workflowId);

        // Assert
        var exists = await _persistence.ExistsAsync(workflowId);
        Assert.False(exists);

        var messages = await _persistence.ReadStreamAsync(workflowId);
        Assert.Empty(messages);
    }

    [Fact]
    public async Task ConcurrentOperations_MultipleMethods_ShouldBeThreadSafe()
    {
        // Arrange
        var workflowId = "workflow-concurrent-ops";
        var tasks = new List<Task>();

        // Act - Simulate concurrent operations
        for (int i = 0; i < 20; i++)
        {
            var index = i;
            tasks.Add(Task.Run(async () =>
            {
                if (index % 3 == 0)
                {
                    // Append
                    await _persistence.AppendAsync(workflowId, new[]
                    {
                        CreateMessage(workflowId, MessageKind.Command, MessageDirection.Output, $"Command-{index}", processed: false)
                    });
                }
                else if (index % 3 == 1)
                {
                    // Read
                    await _persistence.ReadStreamAsync(workflowId);
                }
                else
                {
                    // Get pending
                    await _persistence.GetPendingCommandsAsync(workflowId);
                }
            }));
        }

        await Task.WhenAll(tasks);

        // Assert - Should not throw and data should be consistent
        var messages = await _persistence.ReadStreamAsync(workflowId);
        var pending = await _persistence.GetPendingCommandsAsync(workflowId);

        Assert.True(messages.Count >= 7); // At least 7 appends should have happened
        Assert.True(pending.Count >= 7);
    }

    private static WorkflowMessage<string, string> CreateMessage(
        string workflowId,
        MessageKind kind,
        MessageDirection direction,
        string message,
        bool? processed = null)
    {
        return new WorkflowMessage<string, string>(
            WorkflowId: workflowId,
            Position: 0, // Will be assigned by AppendAsync
            Kind: kind,
            Direction: direction,
            Message: message,
            Timestamp: DateTime.UtcNow,
            Processed: processed
        );
    }
}
