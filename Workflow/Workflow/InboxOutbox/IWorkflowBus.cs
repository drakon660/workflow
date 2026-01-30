namespace Workflow.InboxOutbox;

/// <summary>
/// Clean abstraction for sending workflow inputs.
/// Hides the envelope creation from client code.
/// </summary>
public interface IWorkflowBus
{
    /// <summary>
    /// Send an input message to a workflow instance.
    /// The workflow type is inferred from the input type registration.
    /// </summary>
    /// <typeparam name="TInput">The workflow input base type</typeparam>
    /// <param name="workflowId">Unique identifier for the workflow instance</param>
    /// <param name="input">The input message</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task SendAsync<TInput>(string workflowId, TInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Send an input message to a workflow instance with explicit workflow type.
    /// Use when input type is shared across multiple workflows.
    /// </summary>
    Task SendAsync<TInput>(string workflowType, string workflowId, TInput input, CancellationToken cancellationToken = default);
}

/// <summary>
/// Registry that maps input types to workflow types.
/// Populated during service registration.
/// </summary>
public interface IWorkflowTypeRegistry
{
    /// <summary>
    /// Get the workflow type key for a given input type.
    /// </summary>
    string GetWorkflowType(Type inputType);

    /// <summary>
    /// Register a mapping from input type to workflow type.
    /// </summary>
    void Register(Type inputType, string workflowType);

    /// <summary>
    /// Check if a mapping exists for the input type.
    /// </summary>
    bool HasMapping(Type inputType);
}

/// <summary>
/// Default implementation of the workflow type registry.
/// Mappings are populated from IWorkflowTypeMapping registrations in DI.
/// Supports inheritance - if PlaceOrderInputMessage : OrderProcessingInputMessage,
/// looking up PlaceOrderInputMessage will find the OrderProcessingInputMessage mapping.
/// </summary>
public class WorkflowTypeRegistry : IWorkflowTypeRegistry
{
    private readonly Dictionary<Type, string> _mappings = new();

    public WorkflowTypeRegistry(IEnumerable<IWorkflowTypeMapping> mappings)
    {
        foreach (var mapping in mappings)
        {
            _mappings[mapping.InputType] = mapping.WorkflowType;
        }
    }

    public string GetWorkflowType(Type inputType)
    {
        // Direct match
        if (_mappings.TryGetValue(inputType, out var workflowType))
            return workflowType;

        // Check base types (inheritance chain)
        var baseType = inputType.BaseType;
        while (baseType != null && baseType != typeof(object))
        {
            if (_mappings.TryGetValue(baseType, out workflowType))
                return workflowType;
            baseType = baseType.BaseType;
        }

        // Check interfaces
        foreach (var iface in inputType.GetInterfaces())
        {
            if (_mappings.TryGetValue(iface, out workflowType))
                return workflowType;
        }

        throw new InvalidOperationException(
            $"No workflow type registered for input type '{inputType.Name}' or its base types. " +
            $"Register using AddWorkflow<TInput, TState, TOutput, TWorkflow>(workflowType). " +
            $"Registered base types: {string.Join(", ", _mappings.Keys.Select(k => k.Name))}");
    }

    public void Register(Type inputType, string workflowType)
    {
        _mappings[inputType] = workflowType;
    }

    public bool HasMapping(Type inputType)
    {
        if (_mappings.ContainsKey(inputType))
            return true;

        var baseType = inputType.BaseType;
        while (baseType != null && baseType != typeof(object))
        {
            if (_mappings.ContainsKey(baseType))
                return true;
            baseType = baseType.BaseType;
        }

        return inputType.GetInterfaces().Any(i => _mappings.ContainsKey(i));
    }
}

/// <summary>
/// Interface for workflow type mappings registered via DI.
/// </summary>
public interface IWorkflowTypeMapping
{
    Type InputType { get; }
    string WorkflowType { get; }
}
