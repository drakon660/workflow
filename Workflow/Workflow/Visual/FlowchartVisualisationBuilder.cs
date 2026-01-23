namespace Workflow.Visual;

/// <summary>
/// Fluent builder for generating Mermaid flowchart diagrams from workflow source code.
///
/// Usage:
/// <code>
/// var diagram = WorkflowDiagram
///     .FromSourceFile("OrderProcessingWorkflow.cs")
///     .GenerateStateTransitions();
/// </code>
///
/// Or from source string:
/// <code>
/// var diagram = WorkflowDiagram
///     .FromSource(sourceCode)
///     .GenerateAll();
/// </code>
/// </summary>
public static class WorkflowDiagram
{
    /// <summary>
    /// Creates a diagram builder from a source code file path.
    /// </summary>
    public static WorkflowDiagramBuilder FromSourceFile(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Workflow source file not found: {filePath}");

        var sourceCode = File.ReadAllText(filePath);
        return new WorkflowDiagramBuilder(sourceCode);
    }

    /// <summary>
    /// Creates a diagram builder from source code string.
    /// </summary>
    public static WorkflowDiagramBuilder FromSource(string sourceCode)
    {
        return new WorkflowDiagramBuilder(sourceCode);
    }

    /// <summary>
    /// Creates a diagram builder for a workflow type, reading its source file.
    /// Requires the source file to be in a conventional location or specified explicitly.
    /// </summary>
    public static WorkflowDiagramBuilder For<TWorkflow>(string sourceFilePath = null)
    {
        if (sourceFilePath != null)
            return FromSourceFile(sourceFilePath);

        // Try to find source file by convention (same name as type)
        var typeName = typeof(TWorkflow).Name;
        var possiblePaths = new[]
        {
            $"{typeName}.cs",
            $"../{typeName}.cs",
            $"../../{typeName}.cs",
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
                return FromSourceFile(path);
        }

        throw new FileNotFoundException(
            $"Could not find source file for {typeName}. Please specify the path explicitly.");
    }
}

/// <summary>
/// Builder for configuring and generating workflow diagrams.
/// </summary>
public class WorkflowDiagramBuilder
{
    private readonly string _sourceCode;
    private readonly WorkflowAnalyzer _analyzer;
    private WorkflowDiagramModel _model;
    private MermaidOptions _options = new();

    internal WorkflowDiagramBuilder(string sourceCode)
    {
        _sourceCode = sourceCode;
        _analyzer = new WorkflowAnalyzer();
    }

    /// <summary>
    /// Configures the Mermaid generation options.
    /// </summary>
    public WorkflowDiagramBuilder WithOptions(MermaidOptions options)
    {
        _options = options;
        return this;
    }

    /// <summary>
    /// Configures the Mermaid generation options.
    /// </summary>
    public WorkflowDiagramBuilder WithOptions(Action<MermaidOptionsBuilder> configure)
    {
        var builder = new MermaidOptionsBuilder();
        configure(builder);
        _options = builder.Build();
        return this;
    }

    /// <summary>
    /// Gets the analyzed workflow model (lazy-loaded).
    /// </summary>
    public WorkflowDiagramModel Model => _model ??= _analyzer.Analyze(_sourceCode);

    /// <summary>
    /// Generates a state transition diagram (from InternalEvolve).
    /// </summary>
    public string GenerateStateTransitions()
    {
        var generator = new MermaidGenerator(_options);
        return generator.GenerateStateTransitions(Model);
    }

    /// <summary>
    /// Generates a decision tree diagram (from Decide).
    /// </summary>
    public string GenerateDecisionTree()
    {
        var generator = new MermaidGenerator(_options);
        return generator.GenerateDecisionTree(Model);
    }

    /// <summary>
    /// Generates all diagrams in a single markdown document.
    /// </summary>
    public string GenerateAll()
    {
        var generator = new MermaidGenerator(_options);
        return generator.GenerateAll(Model);
    }

    /// <summary>
    /// Saves the state transitions diagram to a file.
    /// </summary>
    public WorkflowDiagramBuilder SaveStateTransitions(string filePath)
    {
        var content = GenerateStateTransitions();
        File.WriteAllText(filePath, content);
        return this;
    }

    /// <summary>
    /// Saves the decision tree diagram to a file.
    /// </summary>
    public WorkflowDiagramBuilder SaveDecisionTree(string filePath)
    {
        var content = GenerateDecisionTree();
        File.WriteAllText(filePath, content);
        return this;
    }

    /// <summary>
    /// Saves all diagrams to a file.
    /// </summary>
    public WorkflowDiagramBuilder SaveAll(string filePath)
    {
        var content = GenerateAll();
        File.WriteAllText(filePath, content);
        return this;
    }
}

/// <summary>
/// Builder for MermaidOptions.
/// </summary>
public class MermaidOptionsBuilder
{
    private bool _includeTitle = true;
    private bool _includeStyles = true;
    private bool _simplifyTypeNames = false;

    public MermaidOptionsBuilder WithTitle(bool include = true)
    {
        _includeTitle = include;
        return this;
    }

    public MermaidOptionsBuilder WithStyles(bool include = true)
    {
        _includeStyles = include;
        return this;
    }

    public MermaidOptionsBuilder SimplifyTypeNames(bool simplify = true)
    {
        _simplifyTypeNames = simplify;
        return this;
    }

    public MermaidOptions Build() => new()
    {
        IncludeTitle = _includeTitle,
        IncludeStyles = _includeStyles,
        SimplifyTypeNames = _simplifyTypeNames
    };
}
