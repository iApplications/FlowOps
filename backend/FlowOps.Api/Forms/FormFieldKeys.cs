using System.Text;
using System.Text.RegularExpressions;

namespace FlowOps.Api.Forms;

// Short field names used by workflow conditions (request.<key>). A key is generated from the label and can be
// edited; versions published before keys existed derive them from their labels the same way (PHASE4 decision 2).
public static partial class FormFieldKeys
{
    public const int MaxLength = 40;

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,39}$")]
    private static partial Regex Pattern();

    public static bool IsValid(string? key) => key is not null && Pattern().IsMatch(key) && key is not ("true" or "false");

    // "Amount (USD)" → "amountUsd"; labels without letters fall back to "field".
    public static string FromLabel(string label)
    {
        var words = Regex.Split(label, "[^A-Za-z0-9]+").Where(x => x.Length > 0).ToList();
        var builder = new StringBuilder();
        foreach (var word in words)
            builder.Append(builder.Length == 0 ? word.ToLowerInvariant() : char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant());
        var key = builder.ToString();
        if (key.Length == 0 || char.IsAsciiDigit(key[0])) key = "field" + key;
        if (key is "true" or "false") key += "Value";
        return key.Length > MaxLength ? key[..MaxLength] : key;
    }

    // Fields keep their own valid, unused keys; the rest get label-derived keys made unique with a number suffix.
    public static List<FormField> Assign(List<FormField> fields)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var keep = fields.Select(x => IsValid(x.Key) && used.Add(x.Key!)).ToList();
        return fields.Select((field, index) =>
        {
            if (keep[index]) return field;
            var stem = FromLabel(field.Label);
            var key = stem;
            for (var n = 2; !used.Add(key); n++)
            {
                var suffix = n.ToString();
                key = (stem.Length + suffix.Length > MaxLength ? stem[..(MaxLength - suffix.Length)] : stem) + suffix;
            }
            return field with { Key = key };
        }).ToList();
    }
}
