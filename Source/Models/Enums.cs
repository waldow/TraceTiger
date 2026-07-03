namespace TraceTiger.Models;

/// <summary>What kind of Azure thing a connection points at.</summary>
public enum ConnectionKind
{
    /// <summary>Classic Application Insights resource (ARM resource id) — queried via QueryResourceAsync.</summary>
    AppInsightsResource,

    /// <summary>Log Analytics workspace — workspace GUID via QueryWorkspaceAsync, or ARM id via QueryResourceAsync.</summary>
    LogAnalyticsWorkspace,

    /// <summary>Built-in canned data source; lets every UI flow run without Azure.</summary>
    Demo,
}

/// <summary>Server-side time window applied to a run (intersected with query-side filters).</summary>
public enum TimeRangeKind
{
    QueryDefined,
    Last15Minutes,
    LastHour,
    Last6Hours,
    Last24Hours,
    Last7Days,
    Last30Days,
    Custom,
}

/// <summary>Which projection of the result the user is looking at.</summary>
public enum ResultViewKind
{
    Grid,
    Chart,
    Json,
}

/// <summary>Chart flavor for the chart view.</summary>
public enum ChartKind
{
    Line,
    Column,
}
