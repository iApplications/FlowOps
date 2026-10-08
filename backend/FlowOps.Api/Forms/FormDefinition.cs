namespace FlowOps.Api.Forms;

public sealed class FormDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public required string Name { get; set; }
    public string Description { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore]
    public string FieldsJson { get; set; } = "[]";
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public List<FormField> Fields => System.Text.Json.JsonSerializer.Deserialize<List<FormField>>(FieldsJson) ?? [];
    public int DraftRevision { get; set; }
    // Whether the saved draft differs from the latest published version. Computed by form endpoints that load
    // a single draft (null where not computed, such as the form list); never stored.
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public bool? HasUnpublishedChanges { get; set; }
    public int PublicationCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
