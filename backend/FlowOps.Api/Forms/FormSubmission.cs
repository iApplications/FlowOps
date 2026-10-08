using System.Globalization;
using System.Text.Json;

namespace FlowOps.Api.Forms;

public sealed class FormSubmission
{
    public Guid Id { get; set; }
    public Guid FormId { get; set; }
    public Guid PublicationId { get; set; }
    public Guid OrganizationId { get; set; }
    public required string UserId { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string ValuesJson { get; set; } = "{}";
    [System.Text.Json.Serialization.JsonIgnore]
    public string RequestFingerprint { get; set; } = "";
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public Dictionary<Guid, JsonElement> Values => JsonSerializer.Deserialize<Dictionary<Guid, JsonElement>>(ValuesJson) ?? [];
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
}

public static class SubmissionValidation
{
    public static Dictionary<string, string[]> Validate(List<FormField> fields, Dictionary<Guid, JsonElement>? values, ISet<Guid>? uploadedFiles = null)
    {
        var errors = new Dictionary<string, string[]>();
        if (values is null) { errors["values"] = ["Provide form values."]; return errors; }
        if (values.Keys.Any(id => !fields.Any(field => field.Id == id))) errors["values"] = ["Values contain an unknown field."];
        foreach (var field in fields)
        {
            void Error(string message) => errors[field.Id.ToString()] = [$"{field.Label}: {message}"];
            values.TryGetValue(field.Id, out var value);
            var missing = value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                || value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString());
            if (field.Type == "File")
            {
                if (missing) { if (field.Required) Error("Choose a file."); }
                else if (value.ValueKind != JsonValueKind.String || !Guid.TryParse(value.GetString(), out var fileId) || uploadedFiles?.Contains(fileId) != true)
                    Error("Upload a file with this submission.");
                continue;
            }
            if (missing) { if (field.Required) Error("This field is required."); continue; }
            switch (field.Type)
            {
                case "Text": case "Textarea":
                    if (value.ValueKind != JsonValueKind.String) Error("Enter text.");
                    else if (value.GetString()!.Length > (field.MaxLength ?? 10000)) Error($"Use at most {field.MaxLength ?? 10000} characters.");
                    break;
                case "Number": case "Currency":
                    if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var number)) Error("Enter a valid number.");
                    else if (field.Min is decimal min && number < min || field.Max is decimal max && number > max) Error("The number is outside the allowed range.");
                    else if (field.Type == "Currency" && decimal.Round(number, 2) != number) Error("Use at most two decimal places.");
                    break;
                case "Date":
                    if (value.ValueKind != JsonValueKind.String || !DateOnly.TryParseExact(value.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) Error("Enter a valid date.");
                    else if (field.MinDate is not null && string.CompareOrdinal(value.GetString(), field.MinDate) < 0
                        || field.MaxDate is not null && string.CompareOrdinal(value.GetString(), field.MaxDate) > 0) Error("The date is outside the allowed range.");
                    break;
                case "Dropdown":
                    if (value.ValueKind != JsonValueKind.String || field.Options?.Contains(value.GetString()!) != true) Error("Choose an allowed option.");
                    break;
                case "Checkbox":
                    if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) Error("Provide a checkbox value.");
                    else if (field.Required && !value.GetBoolean()) Error("This checkbox must be checked.");
                    break;
                default: Error("Unsupported field type."); break;
            }
        }
        return errors;
    }
}
