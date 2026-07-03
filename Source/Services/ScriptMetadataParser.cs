using System.Text.RegularExpressions;
using TraceTiger.Models;

namespace TraceTiger.Services;

/// <summary>
/// Parses the leading <c>//</c> comment block of a .kql file for
/// <c>// @name …</c>, <c>// @description …</c> and <c>// @param NAME default…</c> lines.
/// Everything is optional; parsing stops at the first non-comment, non-blank line.
/// </summary>
public static partial class ScriptMetadataParser
{
    public static ScriptMetadata Parse(string text)
    {
        string? name = null, description = null;
        var parameters = new List<ScriptParameter>();

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (!line.TrimStart().StartsWith("//", StringComparison.Ordinal)) break;

            var m = TagRegex().Match(line);
            if (!m.Success) continue;

            var value = m.Groups[2].Value.Trim();
            switch (m.Groups[1].Value.ToLowerInvariant())
            {
                case "name": name = value; break;
                case "description": description = value; break;
                case "param":
                    var space = value.IndexOfAny([' ', '\t']);
                    if (space < 0)
                        parameters.Add(new ScriptParameter(value, null));
                    else
                        parameters.Add(new ScriptParameter(value[..space], value[(space + 1)..].Trim()));
                    break;
            }
        }

        return name is null && description is null && parameters.Count == 0
            ? ScriptMetadata.Empty
            : new ScriptMetadata { Name = name, Description = description, Parameters = parameters };
    }

    /// <summary>Header template inserted into newly created scripts.</summary>
    public static string NewScriptTemplate(string name) =>
        $"""
        // @name {name}
        // @description
        requests
        | where timestamp > ago(1h)
        | take 100
        """;

    [GeneratedRegex(@"^\s*//\s*@(name|description|param)\s+(.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex TagRegex();
}
