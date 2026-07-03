namespace TraceTiger.Models;

/// <summary>One executed run (success or failure) shown in the History panel.</summary>
public sealed class RunHistoryEntry
{
    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;
    public string ScriptName { get; set; } = "";
    public string ConnectionName { get; set; } = "";
    public string ConnectionColorHex { get; set; } = "#58A6FF";
    public string TimeRangeLabel { get; set; } = "";
    public double DurationMs { get; set; }
    public int RowCount { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>The editor text as it was run (placeholders intact) — reopened into a scratch tab.</summary>
    public string KqlText { get; set; } = "";

    /// <summary>The exact KQL sent to Azure (placeholders substituted).</summary>
    public string ResolvedKql { get; set; } = "";

    public bool IsError => !string.IsNullOrEmpty(ErrorMessage);
}
