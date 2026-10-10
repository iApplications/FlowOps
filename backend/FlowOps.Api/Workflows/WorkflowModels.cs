using System.Text.Json;

namespace FlowOps.Api.Workflows;

// Logical workflow identity. Name and description are not versioned; the graph lives on WorkflowVersion.
public sealed class WorkflowDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public required string Name { get; set; }
    public string Description { get; set; } = "";
    public int Revision { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class WorkflowVersionStatus
{
    public const string Draft = "Draft";
    public const string Published = "Published";
}

// A versioned graph snapshot. Draft versions are editable; once stored as Published a version is
// immutable (enforced by FlowOpsDbContext) and runtime instances pin to its Id from Phase 4.
public sealed class WorkflowVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkflowDefinitionId { get; set; }
    public Guid OrganizationId { get; set; }
    public int VersionNumber { get; set; }
    public string Status { get; set; } = WorkflowVersionStatus.Draft;
    public int DraftRevision { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string GraphJson { get; set; } = "{}";
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public WorkflowGraph Graph => JsonSerializer.Deserialize<WorkflowGraph>(GraphJson) ?? WorkflowGraph.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PublishedAt { get; set; }
}

// Variables are declared per graph (PHASE6) and live under vars.<name> in a run. Older graphs have none.
public sealed record WorkflowGraph(int SchemaVersion, List<WorkflowNode> Nodes, List<WorkflowConnection> Connections, List<WorkflowVariable>? Variables = null)
{
    public const int CurrentSchemaVersion = 1;
    public static WorkflowGraph Empty => new(CurrentSchemaVersion, [], []);

    // New workflows start with a connected Start → End graph so the canvas is never blank.
    public static WorkflowGraph Starter() => new(CurrentSchemaVersion,
        [
            new("start", WorkflowNodeTypes.Start, "Start", new(0, 0), new(Trigger: WorkflowTriggers.Manual)),
            new("end", WorkflowNodeTypes.End, "End", new(320, 0), new(Outcome: "Completed"))
        ],
        [new("start-end", "start", "next", "end")]);
}

public sealed record WorkflowNode(string Id, string Type, string Label, WorkflowPosition Position, WorkflowNodeConfig? Config);
public sealed record WorkflowPosition(double X, double Y);

// One configuration shape for all node types; each type uses only its own properties (validated per type).
public sealed record WorkflowNodeConfig(string? Trigger = null, Guid? FormId = null, string? Expression = null,
    string? AssigneeRole = null, string? Instructions = null, string? Outcome = null, List<CalculationAssignment>? Assignments = null);

// A declared workflow variable. InitialValue is text that must parse as the type (Number: decimal, Boolean: true/false).
public sealed record WorkflowVariable(string Name, string Type, string? InitialValue = null)
{
    public const string Number = "Number";
    public const string Text = "Text";
    public const string Boolean = "Boolean";
    public static readonly IReadOnlySet<string> Types = new HashSet<string> { Number, Text, Boolean };

    // Whether InitialValue is acceptable for the type; null and empty mean "use the default" (0, empty text, false).
    public static bool InitialValueValid(string type, string? value) => string.IsNullOrEmpty(value) || type switch
    {
        Number => decimal.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _),
        Boolean => value is "true" or "false",
        _ => true
    };

    public System.Text.Json.Nodes.JsonNode InitialNode() => Type switch
    {
        Number => System.Text.Json.Nodes.JsonValue.Create(string.IsNullOrEmpty(InitialValue) ? 0m : decimal.Parse(InitialValue, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture))!,
        Boolean => System.Text.Json.Nodes.JsonValue.Create(InitialValue == "true")!,
        _ => System.Text.Json.Nodes.JsonValue.Create(InitialValue ?? "")!
    };
}

public sealed record CalculationAssignment(string? Variable, string? Expression);

public sealed record WorkflowConnection(string Id, string Source, string SourceHandle, string Target);

public static class WorkflowNodeTypes
{
    public const string Start = "Start";
    public const string Condition = "Condition";
    public const string Approval = "Approval";
    public const string End = "End";
    public const string Calculation = "Calculation";
    public static readonly IReadOnlySet<string> All = new HashSet<string> { Start, Condition, Approval, End, Calculation };
}

public static class WorkflowTriggers
{
    public const string Manual = "Manual";
    public const string FormSubmitted = "FormSubmitted";
}
