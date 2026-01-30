using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Workflow.InboxOutbox;

namespace Workflow;

public static class WorkflowExtensions
{
    /// <summary>
    /// Adds workflow infrastructure services to the dependency injection container.
    /// Note: This only registers infrastructure components.
    /// User workflow implementations should be registered separately using AddScoped&lt;TWorkflow&gt;().
    /// </summary>
    public static IServiceCollection AddWorkflow<TInput, TState, TOutput>(this IServiceCollection services)
    {
        services.AddSingleton<WorkflowStreamRepository>();
        services.AddScoped<WorkflowOrchestrator<TInput, TState, TOutput>>();
        services.AddScoped<WorkflowProcessor<TInput, TState, TOutput>>();

        return services;
    }

    /// <summary>
    /// Adds workflow infrastructure services and registers a user workflow implementation.
    /// </summary>
    public static IServiceCollection AddWorkflow<TInput, TState, TOutput, TWorkflow>(this IServiceCollection services)
        where TWorkflow : class, IWorkflow<TInput, TState, TOutput>
    {
        // Add infrastructure
        services.AddWorkflow<TInput, TState, TOutput>();

        // Register user workflow implementation as scoped
        services.AddScoped<IWorkflow<TInput, TState, TOutput>, TWorkflow>();
        services.AddScoped<TWorkflow>(); // Also register concrete type for direct injection

        return services;
    }

    /// <summary>
    /// Adds workflow infrastructure services, registers a user workflow implementation,
    /// and registers with the workflow processor factory for generic Wolverine handling.
    /// </summary>
    /// <param name="workflowType">Unique type key for this workflow (e.g., "OrderProcessing")</param>
    public static IServiceCollection AddWorkflow<TInput, TState, TOutput, TWorkflow>(
        this IServiceCollection services,
        string workflowType)
        where TWorkflow : class, IWorkflow<TInput, TState, TOutput>
    {
        // Add base workflow registration
        services.AddWorkflow<TInput, TState, TOutput, TWorkflow>();

        // Register processor registration for factory resolution
        services.AddSingleton<IWorkflowProcessorRegistration>(
            new WorkflowProcessorRegistration<TInput, TState, TOutput>(workflowType));

        // Ensure factory is registered (TryAdd prevents duplicates)
        services.TryAddSingleton<IWorkflowProcessorFactory, WorkflowProcessorFactory>();

        // Ensure type registry is registered
        services.TryAddSingleton<IWorkflowTypeRegistry, WorkflowTypeRegistry>();

        // Register IWorkflowBus (Wolverine implementation)
        services.TryAddScoped<IWorkflowBus, WolverineWorkflowBus>();

        // Register the input type -> workflow type mapping
        services.AddSingleton<IWorkflowTypeMapping>(new WorkflowTypeMapping(typeof(TInput), workflowType));

        return services;
    }

    /// <summary>
    /// Internal: Implementation of workflow type mapping.
    /// </summary>
    internal class WorkflowTypeMapping : IWorkflowTypeMapping
    {
        public Type InputType { get; }
        public string WorkflowType { get; }

        public WorkflowTypeMapping(Type inputType, string workflowType)
        {
            InputType = inputType;
            WorkflowType = workflowType;
        }
    }

    /// <summary>
    /// Adds workflow infrastructure services and registers a user async workflow implementation.
    /// </summary>
    public static IServiceCollection AddAsyncWorkflow<TInput, TState, TOutput, TContext, TWorkflow>(this IServiceCollection services)
        where TWorkflow : class, IAsyncWorkflow<TInput, TState, TOutput, TContext>
    {
        // Add infrastructure
        services.AddSingleton<WorkflowStreamRepository>();
        services.AddScoped<AsyncWorkflowOrchestrator<TInput, TState, TOutput, TContext>>();
        
        // Register user workflow implementation as scoped
        services.AddScoped<IAsyncWorkflow<TInput, TState, TOutput, TContext>, TWorkflow>();
        services.AddScoped<TWorkflow>(); // Also register concrete type for direct injection
        
        return services;
    }

    /// <summary>
    /// Registers a user workflow implementation separately from infrastructure.
    /// Use this when you have multiple workflow types in the same application.
    /// </summary>
    public static IServiceCollection AddWorkflowImplementation<TInput, TState, TOutput, TWorkflow>(this IServiceCollection services)
        where TWorkflow : class, IWorkflow<TInput, TState, TOutput>
    {
        services.AddScoped<IWorkflow<TInput, TState, TOutput>, TWorkflow>();
        services.AddScoped<TWorkflow>();
        
        return services;
    }

    /// <summary>
    /// Registers a user async workflow implementation separately from infrastructure.
    /// Use this when you have multiple workflow types in the same application.
    /// </summary>
    public static IServiceCollection AddAsyncWorkflowImplementation<TInput, TState, TOutput, TContext, TWorkflow>(this IServiceCollection services)
        where TWorkflow : class, IAsyncWorkflow<TInput, TState, TOutput, TContext>
    {
        services.AddScoped<IAsyncWorkflow<TInput, TState, TOutput, TContext>, TWorkflow>();
        services.AddScoped<TWorkflow>();
        
        return services;
    }
}