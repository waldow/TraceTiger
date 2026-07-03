namespace TraceTiger.Models;

/// <summary>One open tab persisted to tabs.json so the workspace survives restarts.</summary>
public sealed class TabSessionEntry
{
    /// <summary>Library file backing this tab, or null for a scratch tab.</summary>
    public string? FilePath { get; set; }

    /// <summary>Editor text for scratch tabs and unsaved (dirty) file tabs; null = load from file.</summary>
    public string? Text { get; set; }

    public string? Title { get; set; }
    public string ConnectionName { get; set; } = "";
    public TimeRangeKind TimeRange { get; set; } = TimeRangeKind.QueryDefined;
    public string CustomFromText { get; set; } = "";
    public string CustomToText { get; set; } = "";
    public bool IsDirty { get; set; }
}

/// <summary>The whole persisted tab session.</summary>
public sealed class TabSessionModel
{
    public List<TabSessionEntry> Tabs { get; set; } = new();
    public int SelectedIndex { get; set; }
}
