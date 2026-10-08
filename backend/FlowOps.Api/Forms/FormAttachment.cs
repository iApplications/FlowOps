namespace FlowOps.Api.Forms;

public sealed class FormAttachment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubmissionId { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid FieldId { get; set; }
    public required string FileName { get; set; }
    public long Size { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string Sha256 { get; set; } = "";
}
