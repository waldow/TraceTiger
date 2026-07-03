namespace TraceTiger.Models;

/// <summary>All user preferences, persisted to %AppData%\TraceTiger\settings.json.</summary>
public sealed class SettingsModel
{
    public string ScriptsFolder { get; set; } = "";
    public List<Connection> Connections { get; set; } = new();
    public string LastConnectionName { get; set; } = "";
    public TimeRangeKind DefaultTimeRange { get; set; } = TimeRangeKind.QueryDefined;
    public double EditorFontSize { get; set; } = 13;
    public bool EditorWordWrap { get; set; } = false;

    public SettingsModel Clone() => new()
    {
        ScriptsFolder = ScriptsFolder,
        Connections = Connections.Select(c => c.Clone()).ToList(),
        LastConnectionName = LastConnectionName,
        DefaultTimeRange = DefaultTimeRange,
        EditorFontSize = EditorFontSize,
        EditorWordWrap = EditorWordWrap,
    };
}
