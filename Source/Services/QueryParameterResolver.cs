using System.Text.RegularExpressions;

namespace TraceTiger.Services;

/// <summary>
/// Substitutes <c>&lt;TOKEN&gt;</c> placeholders in KQL with user-supplied values (KQL-escaped)
/// and reports any token left unresolved so a run can refuse instead of sending bad KQL.
/// </summary>
public static partial class QueryParameterResolver
{
    /// <summary>Replaces each token that has a non-empty value. Call
    /// <see cref="FindUnresolvedTokens"/> on the result to confirm nothing was left behind.</summary>
    public static string Resolve(string kql, IEnumerable<KeyValuePair<string, string?>> values)
    {
        foreach (var (token, value) in values)
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            kql = kql.Replace($"<{token}>", EscapeKqlStringContent(value.Trim()), StringComparison.Ordinal);
        }
        return kql;
    }

    private static string EscapeKqlStringContent(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);

    /// <summary>Returns the distinct placeholder token names (without angle brackets) present in the KQL.
    /// Header comment lines (<c>// …</c>) are ignored so <c>// @param FOO</c> docs don't count.</summary>
    public static IReadOnlyList<string> FindUnresolvedTokens(string kql)
    {
        var found = new List<string>();
        foreach (var line in kql.Split('\n'))
        {
            if (line.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
            foreach (Match m in PlaceholderRegex().Matches(line))
            {
                var name = m.Value[1..^1];
                if (!found.Contains(name)) found.Add(name);
            }
        }
        return found;
    }

    // Requires a letter first and no whitespace inside, so KQL comparisons like `x > 3` or
    // generics-looking text never match.
    [GeneratedRegex(@"<[A-Za-z][A-Za-z0-9_\-]*>")]
    private static partial Regex PlaceholderRegex();
}
