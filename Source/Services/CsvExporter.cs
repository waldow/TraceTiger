using System.Globalization;
using System.Text;
using TraceTiger.Models;

namespace TraceTiger.Services;

/// <summary>Formats query results as RFC 4180-style CSV, or tab-delimited text for the clipboard
/// (pastes cleanly into Excel).</summary>
public static class CsvExporter
{
    public static string ToCsv(QueryResultTable table)
    {
        var sb = new StringBuilder();
        AppendRow(sb, table.Columns.Select(c => c.Name));
        foreach (var row in table.Rows)
            AppendRow(sb, table.Columns.Select((_, i) => FormatCell(i < row.Length ? row[i] : null)));
        return sb.ToString();
    }

    public static string ToTsv(QueryResultTable table)
    {
        var sb = new StringBuilder();
        AppendTabDelimitedRow(sb, table.Columns.Select(c => NormalizeClipboardCell(c.Name)));
        foreach (var row in table.Rows)
            AppendTabDelimitedRow(sb, table.Columns.Select((_, i) =>
                NormalizeClipboardCell(FormatCell(i < row.Length ? row[i] : null))));
        return sb.ToString();
    }

    private static void AppendRow(StringBuilder sb, IEnumerable<string> values)
    {
        var first = true;
        foreach (var value in values)
        {
            if (!first) sb.Append(',');
            first = false;
            AppendEscaped(sb, value);
        }
        sb.AppendLine();
    }

    private static void AppendTabDelimitedRow(StringBuilder sb, IEnumerable<string> values)
    {
        var first = true;
        foreach (var value in values)
        {
            if (!first) sb.Append('\t');
            first = false;
            sb.Append(value);
        }
        sb.AppendLine();
    }

    private static string FormatCell(object? value) => value switch
    {
        null => string.Empty,
        DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
        _ => value.ToString() ?? string.Empty,
    };

    private static void AppendEscaped(StringBuilder sb, string value)
    {
        var mustQuote = value.Contains(',') || value.Contains('"') || value.Contains('\r') || value.Contains('\n');
        if (!mustQuote)
        {
            sb.Append(value);
            return;
        }

        sb.Append('"');
        sb.Append(value.Replace("\"", "\"\"", StringComparison.Ordinal));
        sb.Append('"');
    }

    private static string NormalizeClipboardCell(string value)
    {
        return value
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('\t', ' ');
    }
}
