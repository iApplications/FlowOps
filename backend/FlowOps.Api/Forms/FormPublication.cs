namespace FlowOps.Api.Forms;

public sealed class FormPublication
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FormId { get; set; }
    public Guid OrganizationId { get; set; }
    public int VersionNumber { get; set; }
    public required string Name { get; set; }
    public string Description { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore]
    public string FieldsJson { get; set; } = "[]";
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public List<FormField> Fields => System.Text.Json.JsonSerializer.Deserialize<List<FormField>>(FieldsJson) ?? [];
    public DateTimeOffset PublishedAt { get; set; } = DateTimeOffset.UtcNow;
}
