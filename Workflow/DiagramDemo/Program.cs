using Workflow.Visual;

// Read the actual workflow source file
var sourceFile = @"F:\src\workflow\Workflow\Workflow.Samples\GroupCheckoutWorkflow.cs";
var source = File.ReadAllText(sourceFile);

var builder = WorkflowDiagram.FromSource(source);

Console.WriteLine("=== Model Info ===");
Console.WriteLine($"Workflow: {builder.Model.WorkflowName}");
Console.WriteLine($"State Transitions: {builder.Model.StateTransitions.Count}");
Console.WriteLine($"Decision Rules: {builder.Model.DecisionRules.Count}");
Console.WriteLine();

Console.WriteLine("=== State Transitions ===");
Console.WriteLine(builder.GenerateStateTransitions());
Console.WriteLine();

Console.WriteLine("=== Decision Tree ===");
Console.WriteLine(builder.GenerateDecisionTree());
