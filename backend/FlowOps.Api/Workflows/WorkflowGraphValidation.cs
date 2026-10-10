using System.Text.RegularExpressions;

namespace FlowOps.Api.Workflows;

// Structural rules every saved draft must satisfy. A violation rejects the save. Completeness
// (one Start, reachability, connected outputs, expression syntax) is reported separately so
// incomplete drafts can still be saved.
public static partial class WorkflowGraphValidation
{
    public const int MaxNodes = 100;
    public const int MaxConnections = 200;
    public const int MaxLabelLength = 80;
    public const int MaxExpressionLength = 500;
    public const int MaxInstructionsLength = 1000;
    public const double MaxCoordinate = 1_000_000;
    public static readonly IReadOnlySet<string> AssigneeRoles = new HashSet<string> { "Owner", "Admin", "Member" };

    public static IReadOnlyList<string> OutputHandles(string nodeType) => nodeType switch
    {
        WorkflowNodeTypes.Start => ["next"],
        WorkflowNodeTypes.Condition => ["true", "false"],
        WorkflowNodeTypes.Approval => ["approved", "rejected"],
        _ => []
    };

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex IdPattern();

    // formExists answers whether a form ID belongs to the active organization.
    // roleExists answers whether a name is an active custom role of the organization; built-in roles are always accepted.
    public static string? Validate(WorkflowGraph? graph, Func<Guid, bool> formExists, Func<string, bool>? roleExists = null)
    {
        if (graph?.Nodes is null || graph.Connections is null) return "Provide workflow nodes and connections.";
        if (graph.SchemaVersion != WorkflowGraph.CurrentSchemaVersion) return "Unsupported workflow graph version. Reload the designer.";
        if (graph.Nodes.Count > MaxNodes) return $"A workflow can have at most {MaxNodes} nodes.";
        if (graph.Connections.Count > MaxConnections) return $"A workflow can have at most {MaxConnections} connections.";
        var nodes = new Dictionary<string, WorkflowNode>();
        foreach (var node in graph.Nodes)
        {
            if (node?.Id is null || !IdPattern().IsMatch(node.Id) || !nodes.TryAdd(node.Id, node)) return "Each node needs a unique ID of letters, digits, '-' or '_'.";
            if (!WorkflowNodeTypes.All.Contains(node.Type)) return "Unsupported node type.";
            if (string.IsNullOrWhiteSpace(node.Label) || node.Label.Trim().Length > MaxLabelLength) return $"Node labels must be 1–{MaxLabelLength} characters.";
            if (node.Position is null || !double.IsFinite(node.Position.X) || !double.IsFinite(node.Position.Y)
                || Math.Abs(node.Position.X) > MaxCoordinate || Math.Abs(node.Position.Y) > MaxCoordinate) return $"{node.Label}: invalid position.";
            if (ValidateConfig(node, node.Config ?? new(), formExists, roleExists) is string error) return $"{node.Label}: {error}";
        }
        var connectionIds = new HashSet<string>();
        var usedOutputs = new HashSet<(string, string)>();
        foreach (var connection in graph.Connections)
        {
            if (connection?.Id is null || !IdPattern().IsMatch(connection.Id) || !connectionIds.Add(connection.Id)) return "Each connection needs a unique ID of letters, digits, '-' or '_'.";
            if (connection.Source is null || connection.Target is null || !nodes.TryGetValue(connection.Source, out var source) || !nodes.ContainsKey(connection.Target))
                return "Connections must link existing nodes.";
            if (connection.Source == connection.Target) return $"{source.Label}: a node cannot connect to itself.";
            if (connection.SourceHandle is null || !OutputHandles(source.Type).Contains(connection.SourceHandle))
                return source.Type == WorkflowNodeTypes.End ? $"{source.Label}: End nodes have no outputs." : $"{source.Label}: unknown output.";
            if (!usedOutputs.Add((connection.Source, connection.SourceHandle))) return $"{source.Label}: the '{connection.SourceHandle}' output can connect to only one node.";
        }
        return null;
    }

    private static string? ValidateConfig(WorkflowNode node, WorkflowNodeConfig config, Func<Guid, bool> formExists, Func<string, bool>? roleExists)
    {
        // Each type may only carry its own settings, so stale values cannot hide in a node after its type changes.
        bool Only(params string[] allowed) =>
            (config.Trigger is null || allowed.Contains("trigger")) && (config.FormId is null || allowed.Contains("formId"))
            && (config.Expression is null || allowed.Contains("expression")) && (config.AssigneeRole is null || allowed.Contains("assigneeRole"))
            && (config.Instructions is null || allowed.Contains("instructions")) && (config.Outcome is null || allowed.Contains("outcome"));
        switch (node.Type)
        {
            case WorkflowNodeTypes.Start:
                if (!Only("trigger", "formId")) return "Start nodes only configure a trigger.";
                if (config.Trigger == WorkflowTriggers.Manual) return config.FormId is null ? null : "Manual triggers do not use a form.";
                if (config.Trigger != WorkflowTriggers.FormSubmitted) return "Choose a Manual or Form submitted trigger.";
                return config.FormId is Guid formId && formExists(formId) ? null : "Choose a form from this organization.";
            case WorkflowNodeTypes.Condition:
                if (!Only("expression")) return "Condition nodes only configure an expression.";
                return (config.Expression?.Length ?? 0) > MaxExpressionLength ? $"Expressions must be {MaxExpressionLength} characters or fewer." : null;
            case WorkflowNodeTypes.Approval:
                if (!Only("assigneeRole", "instructions")) return "Approval nodes only configure an assignee role and instructions.";
                if (config.AssigneeRole is null || !(AssigneeRoles.Contains(config.AssigneeRole) || roleExists?.Invoke(config.AssigneeRole) == true))
                    return "Choose who approves from the organization roles.";
                return (config.Instructions?.Length ?? 0) > MaxInstructionsLength ? $"Instructions must be {MaxInstructionsLength} characters or fewer." : null;
            default:
                if (!Only("outcome")) return "End nodes only configure an outcome.";
                return (config.Outcome?.Length ?? 0) > MaxLabelLength ? $"Outcomes must be {MaxLabelLength} characters or fewer." : null;
        }
    }
}
