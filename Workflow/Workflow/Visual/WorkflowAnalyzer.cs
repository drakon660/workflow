using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Workflow.Visual;

/// <summary>
/// Analyzes workflow source code using Roslyn to extract state transitions and decision rules.
/// </summary>
public class WorkflowAnalyzer
{
    /// <summary>
    /// Analyzes the source code of a workflow class and extracts diagram model.
    /// </summary>
    public WorkflowDiagramModel Analyze(string sourceCode)
    {
        var tree = CSharpSyntaxTree.ParseText(sourceCode);
        var root = tree.GetCompilationUnitRoot();

        var workflowClass = FindWorkflowClass(root);
        if (workflowClass == null)
        {
            throw new InvalidOperationException("No workflow class found in source code");
        }

        var model = new WorkflowDiagramModel
        {
            WorkflowName = workflowClass.Identifier.Text,
            StateTransitions = ExtractStateTransitions(workflowClass),
            DecisionRules = ExtractDecisionRules(workflowClass)
        };

        // Try to extract generic type arguments from base class
        ExtractTypeInfo(workflowClass, model);

        return model;
    }

    /// <summary>
    /// Finds a class that inherits from Workflow or AsyncWorkflow.
    /// </summary>
    private ClassDeclarationSyntax FindWorkflowClass(CompilationUnitSyntax root)
    {
        return root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(c => IsWorkflowClass(c));
    }

    private bool IsWorkflowClass(ClassDeclarationSyntax classDecl)
    {
        if (classDecl.BaseList == null) return false;

        return classDecl.BaseList.Types.Any(t =>
        {
            var typeName = t.Type.ToString();
            return typeName.StartsWith("Workflow<") ||
                   typeName.StartsWith("AsyncWorkflow<") ||
                   typeName.Contains("Workflow<");
        });
    }

    private void ExtractTypeInfo(ClassDeclarationSyntax classDecl, WorkflowDiagramModel model)
    {
        if (classDecl.BaseList == null) return;

        var baseType = classDecl.BaseList.Types
            .Select(t => t.Type)
            .OfType<GenericNameSyntax>()
            .FirstOrDefault(g => g.Identifier.Text.Contains("Workflow"));

        if (baseType?.TypeArgumentList != null)
        {
            var args = baseType.TypeArgumentList.Arguments;
            if (args.Count >= 3)
            {
                // Workflow<TInput, TState, TOutput> or AsyncWorkflow<TInput, TState, TOutput, TContext>
                model.GetType().GetProperty("InputType")?.SetValue(model, args[0].ToString());
                model.GetType().GetProperty("StateType")?.SetValue(model, args[1].ToString());
                model.GetType().GetProperty("OutputType")?.SetValue(model, args[2].ToString());
            }
        }
    }

    /// <summary>
    /// Extracts state transitions from the InternalEvolve method.
    /// </summary>
    private List<StateTransition> ExtractStateTransitions(ClassDeclarationSyntax classDecl)
    {
        var transitions = new List<StateTransition>();

        // Find InternalEvolve method
        var evolveMethod = classDecl.Members
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.Text == "InternalEvolve");

        if (evolveMethod == null) return transitions;

        // Find switch expression
        var switchExpr = evolveMethod.DescendantNodes()
            .OfType<SwitchExpressionSyntax>()
            .FirstOrDefault();

        if (switchExpr == null) return transitions;

        foreach (var arm in switchExpr.Arms)
        {
            var transition = ParseEvolveArm(arm);
            if (transition != null)
            {
                transitions.Add(transition);
            }
        }

        return transitions;
    }

    /// <summary>
    /// Parses a switch arm from InternalEvolve.
    /// Pattern: (StateType s, EventType { Message: InputType m }) => new NewState(...)
    /// </summary>
    private StateTransition ParseEvolveArm(SwitchExpressionArmSyntax arm)
    {
        // Skip discard pattern (default case)
        if (arm.Pattern is DiscardPatternSyntax) return null;

        // We expect a recursive pattern with positional clause: (State, Event)
        if (arm.Pattern is not RecursivePatternSyntax { PositionalPatternClause: { } positional })
            return null;

        var subpatterns = positional.Subpatterns;
        if (subpatterns.Count < 2) return null;

        // First element: State type
        var fromState = ExtractTypeName(subpatterns[0].Pattern);
        if (fromState == null) return null;

        // Second element: Event type with nested Message pattern
        var (eventType, inputType) = ExtractEventPattern(subpatterns[1].Pattern);
        if (eventType == null) return null;

        // Result: new StateType(...) or just state (no change)
        var toState = ExtractResultState(arm.Expression);
        if (toState == null || toState == "state") return null; // Skip no-change cases

        return new StateTransition(fromState, eventType, inputType ?? "Unknown", toState);
    }

    /// <summary>
    /// Extracts type name from a pattern like "NoOrder n" or "NoOrder".
    /// </summary>
    private string ExtractTypeName(PatternSyntax pattern)
    {
        return pattern switch
        {
            DeclarationPatternSyntax decl => decl.Type.ToString(),
            TypePatternSyntax type => type.Type.ToString(),
            RecursivePatternSyntax { Type: { } type } => type.ToString(),
            ConstantPatternSyntax constant => constant.Expression.ToString(),
            _ => null
        };
    }

    /// <summary>
    /// Extracts event type and nested message type from pattern like:
    /// InitiatedBy { Message: PlaceOrderInputMessage m }
    /// </summary>
    private (string EventType, string InputType) ExtractEventPattern(PatternSyntax pattern)
    {
        if (pattern is not RecursivePatternSyntax recursive)
            return (null, null);

        var eventType = recursive.Type?.ToString();
        if (eventType == null) return (null, null);

        // Look for property pattern with Message property
        var propertyPatterns = recursive.PropertyPatternClause?.Subpatterns;
        if (propertyPatterns == null) return (eventType, null);

        foreach (var prop in propertyPatterns)
        {
            var propName = prop.NameColon?.Name.ToString();
            if (propName == "Message")
            {
                var inputType = ExtractTypeName(prop.Pattern);
                return (eventType, inputType);
            }
        }

        return (eventType, null);
    }

    /// <summary>
    /// Extracts the result state from expression like "new OrderCreated(...)" or "state".
    /// </summary>
    private string ExtractResultState(ExpressionSyntax expr)
    {
        return expr switch
        {
            ObjectCreationExpressionSyntax creation => creation.Type.ToString(),
            ImplicitObjectCreationExpressionSyntax => "Unknown",
            IdentifierNameSyntax id => id.Identifier.Text, // "state" for no-change
            _ => null
        };
    }

    /// <summary>
    /// Extracts decision rules from the Decide or DecideAsync method.
    /// </summary>
    private List<DecisionRule> ExtractDecisionRules(ClassDeclarationSyntax classDecl)
    {
        var rules = new List<DecisionRule>();

        // Find Decide or DecideAsync method
        var decideMethod = classDecl.Members
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.Text == "Decide" || m.Identifier.Text == "DecideAsync");

        if (decideMethod == null) return rules;

        // Find switch expression or switch statement
        var switchExpr = decideMethod.DescendantNodes()
            .OfType<SwitchExpressionSyntax>()
            .FirstOrDefault();

        if (switchExpr != null)
        {
            foreach (var arm in switchExpr.Arms)
            {
                var rule = ParseDecideArm(arm);
                if (rule != null)
                {
                    rules.Add(rule);
                }
            }
        }
        else
        {
            // Try switch statement (used in async workflow)
            var switchStmt = decideMethod.DescendantNodes()
                .OfType<SwitchStatementSyntax>()
                .FirstOrDefault();

            if (switchStmt != null)
            {
                foreach (var section in switchStmt.Sections)
                {
                    var rule = ParseDecideSection(section);
                    if (rule != null)
                    {
                        rules.Add(rule);
                    }
                }
            }
        }

        return rules;
    }

    /// <summary>
    /// Parses a switch arm from Decide method (switch expression).
    /// Pattern: (InputType p, StateType s) => [Commands...]
    /// </summary>
    private DecisionRule ParseDecideArm(SwitchExpressionArmSyntax arm)
    {
        // Skip discard/default
        if (arm.Pattern is DiscardPatternSyntax) return null;

        // We expect tuple pattern: (InputType, StateType)
        if (arm.Pattern is not RecursivePatternSyntax { PositionalPatternClause: { } positional })
            return null;

        var subpatterns = positional.Subpatterns;
        if (subpatterns.Count < 2) return null;

        // First: Input type, Second: State type
        var inputType = ExtractTypeName(subpatterns[0].Pattern);
        var stateType = ExtractTypeName(subpatterns[1].Pattern);

        if (inputType == null || stateType == null) return null;

        // Extract commands from result expression
        var commands = ExtractCommands(arm.Expression);

        return new DecisionRule(inputType, stateType, commands);
    }

    /// <summary>
    /// Parses a switch section from DecideAsync method (switch statement).
    /// Pattern: case (InputType p, StateType s): return [Commands...];
    /// </summary>
    private DecisionRule ParseDecideSection(SwitchSectionSyntax section)
    {
        // Get the case label pattern
        var caseLabel = section.Labels
            .OfType<CasePatternSwitchLabelSyntax>()
            .FirstOrDefault();

        if (caseLabel?.Pattern is not RecursivePatternSyntax { PositionalPatternClause: { } positional })
            return null;

        var subpatterns = positional.Subpatterns;
        if (subpatterns.Count < 2) return null;

        var inputType = ExtractTypeName(subpatterns[0].Pattern);
        var stateType = ExtractTypeName(subpatterns[1].Pattern);

        if (inputType == null || stateType == null) return null;

        // Find return statement with commands
        var returnStmt = section.Statements
            .OfType<ReturnStatementSyntax>()
            .FirstOrDefault();

        if (returnStmt?.Expression == null)
        {
            // Check for block with return inside
            var block = section.Statements.OfType<BlockSyntax>().FirstOrDefault();
            returnStmt = block?.Statements.OfType<ReturnStatementSyntax>().LastOrDefault();
        }

        var commands = returnStmt?.Expression != null
            ? ExtractCommands(returnStmt.Expression)
            : [];

        return new DecisionRule(inputType, stateType, commands);
    }

    /// <summary>
    /// Extracts commands from an expression like [Send(...), Schedule(...), Complete()].
    /// </summary>
    private List<CommandInfo> ExtractCommands(ExpressionSyntax expr)
    {
        var commands = new List<CommandInfo>();

        // Handle collection expression: [Send(...), ...]
        if (expr is CollectionExpressionSyntax collection)
        {
            foreach (var element in collection.Elements)
            {
                if (element is ExpressionElementSyntax exprElement)
                {
                    var cmd = ParseCommandInvocation(exprElement.Expression);
                    if (cmd != null) commands.Add(cmd);
                }
            }
        }
        // Handle array initializer: new[] { Send(...), ... }
        else if (expr is ArrayCreationExpressionSyntax array && array.Initializer != null)
        {
            foreach (var element in array.Initializer.Expressions)
            {
                var cmd = ParseCommandInvocation(element);
                if (cmd != null) commands.Add(cmd);
            }
        }
        // Handle implicit array: { Send(...), ... }
        else if (expr is InitializerExpressionSyntax initializer)
        {
            foreach (var element in initializer.Expressions)
            {
                var cmd = ParseCommandInvocation(element);
                if (cmd != null) commands.Add(cmd);
            }
        }
        // Single command
        else
        {
            var cmd = ParseCommandInvocation(expr);
            if (cmd != null) commands.Add(cmd);
        }

        return commands;
    }

    /// <summary>
    /// Parses a command invocation like Send(new ProcessPayment(...)) or Complete().
    /// </summary>
    private CommandInfo ParseCommandInvocation(ExpressionSyntax expr)
    {
        if (expr is not InvocationExpressionSyntax invocation)
            return null;

        var methodName = invocation.Expression switch
        {
            IdentifierNameSyntax id => id.Identifier.Text,
            MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
            _ => null
        };

        if (methodName == null) return null;

        var kind = methodName switch
        {
            "Send" => CommandKind.Send,
            "Publish" => CommandKind.Publish,
            "Schedule" => CommandKind.Schedule,
            "Reply" => CommandKind.Reply,
            "Complete" => CommandKind.Complete,
            _ => (CommandKind?)null
        };

        if (kind == null) return null;

        // Extract the message type from arguments
        var args = invocation.ArgumentList.Arguments;
        var messageType = "";
        var details = "";

        if (kind == CommandKind.Complete)
        {
            messageType = "Complete";
        }
        else if (kind == CommandKind.Schedule && args.Count >= 2)
        {
            // Schedule(TimeSpan, Message)
            details = args[0].Expression.ToString();
            messageType = ExtractMessageType(args[1].Expression);
        }
        else if (args.Count > 0)
        {
            messageType = ExtractMessageType(args[0].Expression);
        }

        return new CommandInfo(kind.Value, messageType, details);
    }

    /// <summary>
    /// Extracts message type from expression like "new ProcessPayment(...)" or "new PaymentTimeoutOutMessage(...)".
    /// </summary>
    private string ExtractMessageType(ExpressionSyntax expr)
    {
        return expr switch
        {
            ObjectCreationExpressionSyntax creation => creation.Type.ToString(),
            ImplicitObjectCreationExpressionSyntax impl => "Unknown",
            IdentifierNameSyntax id => id.Identifier.Text,
            _ => expr.ToString()
        };
    }
}
