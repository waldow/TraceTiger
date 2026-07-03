using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using TraceTiger.Models;

namespace TraceTiger.Services;

/// <summary>Renders a <see cref="QueryResultTable"/> as pretty JSON for the JSON result view and
/// the cell inspector. Dynamic KQL columns arrive from the SDK as strings; cells that parse as
/// JSON are re-embedded as real objects instead of escaped strings.</summary>
public static class JsonResultFormatter
{
    /// <summary>Serializes up to <paramref name="maxRows"/> rows as an indented JSON array of objects.
    /// Returns the JSON and whether the output was truncated.</summary>
    public static (string Json, bool Truncated) ToJson(QueryResultTable table, int maxRows = 1000)
    {
        var truncated = table.RowCount > maxRows;
        var rows = truncated ? table.Rows.Take(maxRows) : table.Rows;

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartArray();
            foreach (var row in rows)
            {
                writer.WriteStartObject();
                for (var i = 0; i < table.Columns.Count; i++)
                {
                    writer.WritePropertyName(table.Columns[i].Name);
                    WriteCell(writer, i < row.Length ? row[i] : null);
                }
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }

        return (Encoding.UTF8.GetString(stream.ToArray()), truncated);
    }

    private static void WriteCell(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case bool b:
                writer.WriteBooleanValue(b);
                break;
            case int i:
                writer.WriteNumberValue(i);
                break;
            case long l:
                writer.WriteNumberValue(l);
                break;
            case double d when !double.IsNaN(d) && !double.IsInfinity(d):
                writer.WriteNumberValue(d);
                break;
            case decimal m:
                writer.WriteNumberValue(m);
                break;
            case DateTimeOffset dto:
                writer.WriteStringValue(dto.ToString("O", CultureInfo.InvariantCulture));
                break;
            case DateTime dt:
                writer.WriteStringValue(dt.ToString("O", CultureInfo.InvariantCulture));
                break;
            case string s when LooksLikeJson(s) && TryParse(s, out var doc):
                using (doc)
                    doc.RootElement.WriteTo(writer);
                break;
            default:
                writer.WriteStringValue(value.ToString());
                break;
        }
    }

    /// <summary>Pretty-prints a single cell for the inspector: indented JSON if the value parses,
    /// otherwise the raw text unchanged.</summary>
    public static string TryPrettyPrintCell(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value ?? "";
        if (!LooksLikeJson(value) || !TryParse(value, out var doc)) return value;
        using (doc)
            return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
    }

    private static bool LooksLikeJson(string s)
    {
        var t = s.TrimStart();
        return t.StartsWith('{') || t.StartsWith('[');
    }

    private static bool TryParse(string s, out JsonDocument doc)
    {
        try
        {
            doc = JsonDocument.Parse(s);
            return true;
        }
        catch (JsonException)
        {
            doc = null!;
            return false;
        }
    }
}
