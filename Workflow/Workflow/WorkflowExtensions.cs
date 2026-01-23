using Microsoft.Extensions.DependencyInjection;
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