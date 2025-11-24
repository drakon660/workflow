namespace Workflow.Samples.Order;

public class OrderContext : IOrderContext
{
    private readonly int _availableInventory;

    public OrderContext(int availableInventory = 100)
    {
        _availableInventory = availableInventory;
    }

    public Task<int> GetInventoryCountAsync()
    {
        return Task.FromResult(_availableInventory);
    }

    public Task<bool> CheckInventoryAsync(string orderId, int quantity)
    {
        // Check if we have enough inventory for the requested quantity
        return Task.FromResult(_availableInventory >= quantity);
    }
}

public interface IOrderContext
{
    Task<int> GetInventoryCountAsync();
    Task<bool> CheckInventoryAsync(string orderId, int quantity);
}