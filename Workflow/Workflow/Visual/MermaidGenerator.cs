using System.Text;

namespace Workflow.Visual;

/// <summary>
/// Generates Mermaid flowchart diagrams from workflow diagram models.
/// </summary>
public class MermaidGenerator
{
    private readonly MermaidOptions _options;

    public MermaidGenerator(MermaidOptions options = null)
    {
        _options = options ?? new MermaidOptions();
    }

    /// <summary>
    /// Generates a state transition diagram from InternalEvolve patterns.
    /// </summary>
    public string GenerateStateTransitions(WorkflowDiagramModel model)
    {
        var sb = new StringBuilder();

        if (_options.IncludeTitle)
        {
            sb.AppendLine($"# {model.WorkflowName} - State Transitions");
            sb.AppendLine();
        }

        sb.AppendLine("```mermaid");
        sb.AppendLine("flowchart TD");

        // Add start node
        sb.AppendLine("    Start([Start])");

        // Find initial state (first FromState that doesn't appear as a ToState)
        var toStates = model.StateTransitions.Select(t => t.ToState).ToHashSet();
        var initialStates = model.StateTransitions
            .Select(t => t.FromState)
            .Where(s => !toStates.Contains(s))
            .Distinct()
            .ToList();

        foreach (var initial in initialStates)
        {
            sb.AppendLine($"    Start --> {SanitizeNodeId(initial)}[{initial}]");
        }

        // Group transitions by FromState for cleaner output
        var groupedTransitions = model.StateTransitions
            .GroupBy(t => t.FromState)
            .OrderBy(g => g.Key);

        var addedStates = new HashSet<string>();

        foreach (var group in groupedTransitions)
        {
            foreach (var transition in group)
            {
                var fromId = SanitizeNodeId(transition.FromState);
                var toId = SanitizeNodeId(transition.ToState);

                // Build edge label
                var label = FormatTransitionLabel(transition);

                sb.AppendLine($"    {fromId} -->|{label}| {toId}[{transition.ToState}]");

                addedStates.Add(transition.FromState);
                addedStates.Add(transition.ToState);
            }
        }

        // Find terminal states (appear as ToState but never as FromState)
        var fromStates = model.StateTransitions.Select(t => t.FromState).ToHashSet();
        var terminalStates = model.StateTransitions
            .Select(t => t.ToState)
            .Where(s => !fromStates.Contains(s))
            .Distinct()
            .ToList();

        // Add end connections for terminal states
        if (terminalStates.Any())
        {
            sb.AppendLine("    End([End])");
            foreach (var terminal in terminalStates)
            {
                sb.AppendLine($"    {SanitizeNodeId(terminal)} --> End");
            }
        }

        // Add styling
        if (_options.IncludeStyles)
        {
            sb.AppendLine();
            AddStateStyles(sb, model, initialStates, terminalStates);
        }

        sb.AppendLine("```");

        return sb.ToString();
    }

    /// <summary>
    /// Generates a decision tree diagram from Decide patterns.
    /// </summary>
    public string GenerateDecisionTree(WorkflowDiagramModel model)
    {
        var sb = new StringBuilder();

        if (_options.IncludeTitle)
        {
            sb.AppendLine($"# {model.WorkflowName} - Decision Tree (Decide Method)");
            sb.AppendLine();
        }

        sb.AppendLine("```mermaid");
        sb.AppendLine("flowchart TD");

        // Central decision point
        sb.AppendLine("    Start{Input Message + State}");
        sb.AppendLine();

        int nodeIndex = 1;
        var nodeColors = new List<(string NodeId, string Color)>();

        foreach (var rule in model.DecisionRules)
        {
            var nodeId = $"D{nodeIndex}";
            var inputLabel = FormatTypeName(rule.InputType);
            var stateLabel = FormatTypeName(rule.StateType);

            // Build command list
            var commandsText = FormatCommands(rule.Commands);

            // Edge label: Input + State
            var edgeLabel = $"{inputLabel}<br/>+ {stateLabel}";

            // Node content: Commands
            var nodeContent = $"Commands:<br/>{commandsText}";

            sb.AppendLine($"    Start -->|{edgeLabel}| {nodeId}[{nodeContent}]");

            // Determine color based on commands
            var color = DetermineNodeColor(rule);
            nodeColors.Add((nodeId, color));

            nodeIndex++;
        }

        // Add styling
        if (_options.IncludeStyles)
        {
            sb.AppendLine();
            foreach (var (nodeId, color) in nodeColors)
            {
                sb.AppendLine($"    style {nodeId} fill:{color}");
            }
        }

        sb.AppendLine("```");

        return sb.ToString();
    }

    /// <summary>
    /// Generates both diagrams combined into a single markdown document.
    /// </summary>
    public string GenerateAll(WorkflowDiagramModel model)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"# {model.WorkflowName} Diagrams");
        sb.AppendLine();
        sb.AppendLine($"Auto-generated from source code analysis.");
        sb.AppendLine();

        sb.AppendLine("## State Transitions (InternalEvolve)");
        sb.AppendLine();
        sb.AppendLine("Shows how workflow states change in response to events.");
        sb.AppendLine();

        // Generate state transitions without title (we added our own)
        var stateOptions = _options with { IncludeTitle = false };
        var stateGen = new MermaidGenerator(stateOptions);
        sb.Append(stateGen.GenerateStateTransitions(model));

        sb.AppendLine();
        sb.AppendLine("## Decision Rules (Decide)");
        sb.AppendLine();
        sb.AppendLine("Shows what commands are generated for each (Input, State) combination.");
        sb.AppendLine();

        sb.Append(stateGen.GenerateDecisionTree(model));

        return sb.ToString();
    }

    private string FormatTransitionLabel(StateTransition transition)
    {
        var eventType = transition.EventType;
        var inputType = FormatTypeName(transition.InputType);

        // Shorten common event types
        eventType = eventType switch
        {
            "InitiatedBy" => "InitiatedBy",
            "Received" => "Received",
            _ => eventType
        };

        return $"{eventType}<br/>{inputType}";
    }

    private string FormatTypeName(string typeName)
    {
        if (string.IsNullOrEmpty(typeName)) return typeName;

        // Remove common suffixes for cleaner display if configured
        if (_options.SimplifyTypeNames)
        {
            typeName = typeName
                .Replace("InputMessage", "")
                .Replace("Message", "")
                .Replace("Output", "");
        }

        return typeName;
    }

    private string FormatCommands(List<CommandInfo> commands)
    {
        if (commands.Count == 0) return "None";

        var parts = commands.Select(c => FormatCommand(c));
        return string.Join("<br/>", parts.Select(p => $"• {p}"));
    }

    private string FormatCommand(CommandInfo command)
    {
        var messageType = FormatTypeName(command.MessageType);

        return command.Kind switch
        {
            CommandKind.Complete => "Complete",
            CommandKind.Schedule when !string.IsNullOrEmpty(command.Details)
                => $"Schedule {messageType} after {FormatTimeSpan(command.Details)}",
            _ => $"{command.Kind} {messageType}"
        };
    }

    private string FormatTimeSpan(string timeSpanExpr)
    {
        // Try to extract a readable format from TimeSpan.FromMinutes(15) etc.
        if (timeSpanExpr.Contains("FromMinutes"))
        {
            var match = System.Text.RegularExpressions.Regex.Match(timeSpanExpr, @"FromMinutes\((\d+)\)");
            if (match.Success) return $"{match.Groups[1].Value}min";
        }
        if (timeSpanExpr.Contains("FromSeconds"))
        {
            var match = System.Text.RegularExpressions.Regex.Match(timeSpanExpr, @"FromSeconds\((\d+)\)");
            if (match.Success) return $"{match.Groups[1].Value}s";
        }
        if (timeSpanExpr.Contains("FromHours"))
        {
            var match = System.Text.RegularExpressions.Regex.Match(timeSpanExpr, @"FromHours\((\d+)\)");
            if (match.Success) return $"{match.Groups[1].Value}h";
        }
        return timeSpanExpr;
    }

    private string DetermineNodeColor(DecisionRule rule)
    {
        // Color based on command types
        var hasComplete = rule.Commands.Any(c => c.Kind == CommandKind.Complete);
        var hasReply = rule.Commands.Any(c => c.Kind == CommandKind.Reply);
        var hasCancel = rule.Commands.Any(c =>
            c.MessageType.Contains("Cancel", StringComparison.OrdinalIgnoreCase) ||
            c.MessageType.Contains("Timeout", StringComparison.OrdinalIgnoreCase));

        if (hasCancel && hasComplete) return "#ffcccc"; // Light red - cancellation
        if (hasComplete) return "#90EE90"; // Green - successful completion
        if (hasReply) return "#fff4e1"; // Light yellow - query
        if (rule.StateType.Contains("NoOrder") || rule.StateType.Contains("New") || rule.StateType.Contains("Initial"))
            return "#e1f5ff"; // Light blue - initial

        return "#e1ffe1"; // Light green - normal progress
    }

    private void AddStateStyles(StringBuilder sb, WorkflowDiagramModel model,
        List<string> initialStates, List<string> terminalStates)
    {
        var stateColors = new Dictionary<string, string>();

        // Initial states - light blue
        foreach (var state in initialStates)
        {
            stateColors[state] = "#e1f5ff";
        }

        // Terminal states
        foreach (var state in terminalStates)
        {
            if (state.Contains("Cancel", StringComparison.OrdinalIgnoreCase) ||
                state.Contains("Failed", StringComparison.OrdinalIgnoreCase))
            {
                stateColors[state] = "#ffcccc"; // Light red
            }
            else if (state.Contains("Delivered", StringComparison.OrdinalIgnoreCase) ||
                     state.Contains("Completed", StringComparison.OrdinalIgnoreCase) ||
                     state.Contains("Finished", StringComparison.OrdinalIgnoreCase))
            {
                stateColors[state] = "#90EE90"; // Green
            }
        }

        // Intermediate states - assign colors based on position in flow
        var allStates = model.StateTransitions
            .SelectMany(t => new[] { t.FromState, t.ToState })
            .Distinct()
            .Where(s => !stateColors.ContainsKey(s))
            .ToList();

        var intermediateColors = new[] { "#fff4e1", "#e1ffe1", "#ffe1f5" };
        int colorIndex = 0;
        foreach (var state in allStates)
        {
            stateColors[state] = intermediateColors[colorIndex % intermediateColors.Length];
            colorIndex++;
        }

        foreach (var (state, color) in stateColors)
        {
            sb.AppendLine($"    style {SanitizeNodeId(state)} fill:{color}");
        }
    }

    private string SanitizeNodeId(string name)
    {
        // Mermaid node IDs can't have spaces or special characters
        return name.Replace(" ", "_").Replace("-", "_");
    }
}

/// <summary>
/// Options for Mermaid diagram generation.
/// </summary>
public record MermaidOptions
{
    /// <summary>Include title/header in output.</summary>
    public bool IncludeTitle { get; init; } = true;

    /// <summary>Include node styling.</summary>
    public bool IncludeStyles { get; init; } = true;

    /// <summary>Simplify type names by removing common suffixes.</summary>
    public bool SimplifyTypeNames { get; init; } = false;
}
