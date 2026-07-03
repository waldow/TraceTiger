namespace TraceTiger.Models;

/// <summary>A parameter declared in a script's header comment: <c>// @param NAME default value</c>.</summary>
public sealed record ScriptParameter(string Name, string? DefaultValue);

/// <summary>Metadata parsed from a .kql file's leading comment block
/// (<c>// @name</c>, <c>// @description</c>, <c>// @param</c>).</summary>
public sealed class ScriptMetadata
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<ScriptParameter> Parameters { get; init; } = Array.Empty<ScriptParameter>();

    public static ScriptMetadata Empty { get; } = new();
}
