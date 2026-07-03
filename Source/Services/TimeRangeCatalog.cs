using Azure.Monitor.Query;
using TraceTiger.Models;

namespace TraceTiger.Services;

/// <summary>One pickable server-side time range.</summary>
public sealed record TimeRangeOption(TimeRangeKind Kind, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Maps <see cref="TimeRangeKind"/> to Azure <see cref="QueryTimeRange"/> values and labels.</summary>
public static class TimeRangeCatalog
{
    public static IReadOnlyList<TimeRangeOption> Options { get; } =
    [
        new(TimeRangeKind.QueryDefined, "Query-defined"),
        new(TimeRangeKind.Last15Minutes, "Last 15 minutes"),
        new(TimeRangeKind.LastHour, "Last hour"),
        new(TimeRangeKind.Last6Hours, "Last 6 hours"),
        new(TimeRangeKind.Last24Hours, "Last 24 hours"),
        new(TimeRangeKind.Last7Days, "Last 7 days"),
        new(TimeRangeKind.Last30Days, "Last 30 days"),
        new(TimeRangeKind.Custom, "Custom…"),
    ];

    public static TimeRangeOption OptionFor(TimeRangeKind kind) =>
        Options.FirstOrDefault(o => o.Kind == kind) ?? Options[0];

    public static string LabelFor(TimeRangeKind kind) => OptionFor(kind).Label;

    /// <summary>The Azure-side window. Query-defined sends All so the script's own ago() filters govern.</summary>
    public static QueryTimeRange ToQueryTimeRange(TimeRangeKind kind) => kind switch
    {
        TimeRangeKind.Last15Minutes => new QueryTimeRange(TimeSpan.FromMinutes(15)),
        TimeRangeKind.LastHour => new QueryTimeRange(TimeSpan.FromHours(1)),
        TimeRangeKind.Last6Hours => new QueryTimeRange(TimeSpan.FromHours(6)),
        TimeRangeKind.Last24Hours => new QueryTimeRange(TimeSpan.FromHours(24)),
        TimeRangeKind.Last7Days => new QueryTimeRange(TimeSpan.FromDays(7)),
        TimeRangeKind.Last30Days => new QueryTimeRange(TimeSpan.FromDays(30)),
        _ => QueryTimeRange.All,
    };
}
