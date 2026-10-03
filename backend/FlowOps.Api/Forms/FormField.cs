using System.Globalization;

namespace FlowOps.Api.Forms;

public sealed record FormField(Guid Id, string Type, string Label, bool Required = false,
    string? HelpText = null, int? MaxLength = null, decimal? Min = null, decimal? Max = null,
    string? MinDate = null, string? MaxDate = null, List<string>? Options = null,
    int? MaxFileSizeMb = null, List<string>? AcceptedExtensions = null);

public static class FormFieldValidation
{
    public static readonly string[] Types = ["Text", "Textarea", "Number", "Currency", "Date", "Dropdown", "Checkbox", "File"];
    public static string? Validate(List<FormField>? fields)
    {
        if (fields is null || fields.Count > 50) return "A form can have at most 50 fields.";
        var ids = new HashSet<Guid>();
        foreach (var field in fields)
        {
            if (field is null || field.Id == Guid.Empty || !ids.Add(field.Id)) return "Each field needs a unique ID.";
            if (!Types.Contains(field.Type)) return "Unsupported field type.";
            if (string.IsNullOrWhiteSpace(field.Label) || field.Label.Trim().Length > 120) return "Field labels must be 1–120 characters.";
            if ((field.HelpText?.Length ?? 0) > 500) return "Help text must be 500 characters or fewer.";
            if (field.MaxLength is not null && (field.Type is not ("Text" or "Textarea") || field.MaxLength is < 1 or > 10000))
                return "Text length must be between 1 and 10,000 characters.";
            if ((field.Min is not null || field.Max is not null) && field.Type is not ("Number" or "Currency")) return "Numeric limits apply only to Number and Currency fields.";
            if (field.Min > field.Max) return "Minimum must not exceed maximum.";
            if (field.Type == "Currency" && (HasFraction(field.Min) || HasFraction(field.Max))) return "Currency limits allow at most two decimal places.";
            if (field.MinDate is not null || field.MaxDate is not null)
            {
                if (field.Type != "Date" || !ValidDate(field.MinDate) || !ValidDate(field.MaxDate)) return "Date limits must use YYYY-MM-DD.";
                if (field.MinDate is not null && field.MaxDate is not null && string.CompareOrdinal(field.MinDate, field.MaxDate) > 0) return "Start date must not exceed end date.";
            }
            if (field.Type == "Dropdown")
            {
                if (field.Options is null || field.Options.Count is < 1 or > 100 || field.Options.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 120)
                    || field.Options.Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != field.Options.Count)
                    return "Dropdowns need 1–100 unique, non-empty options of at most 120 characters.";
            }
            else if (field.Options is not null) return "Options apply only to Dropdown fields.";
            if (field.Type == "File")
            {
                if (field.MaxFileSizeMb is null or < 1 or > 10) return "File size must be between 1 and 10 MB.";
                if (field.AcceptedExtensions is null || field.AcceptedExtensions.Count is < 1 or > 10
                    || field.AcceptedExtensions.Any(x => x is not (".pdf" or ".png" or ".jpg" or ".jpeg" or ".txt" or ".csv")))
                    return "Choose allowed file types: PDF, PNG, JPG, JPEG, TXT or CSV.";
            }
            else if (field.MaxFileSizeMb is not null || field.AcceptedExtensions is not null) return "File limits apply only to File fields.";
        }
        return null;
    }
    private static bool HasFraction(decimal? value) => value is decimal number && decimal.Round(number, 2) != number;
    private static bool ValidDate(string? value) => value is null || DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
}
