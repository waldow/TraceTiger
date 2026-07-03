using TraceTiger.Models;

namespace TraceTiger.Services;

/// <summary>
/// Canned data source behind the built-in Demo connection so every UI flow — grid typing, chart
/// projection, JSON view, cell inspector, exports, cancellation, history — works without Azure.
/// Returns a time-series shaped like <c>summarize count(), avg(duration) by service, bin(timestamp, 15m)</c>.
/// </summary>
public sealed class DemoQuerySource
{
    private static readonly string[] Services = ["checkout-api", "orders-api", "search-api"];

    public async Task<QueryResultTable> RunAsync(string kql, CancellationToken ct = default)
    {
        // Feel like a real round-trip (and honor Cancel).
        await Task.Delay(700, ct).ConfigureAwait(false);

        var columns = new List<QueryColumn>
        {
            new() { Name = "timestamp", ClrType = typeof(DateTimeOffset) },
            new() { Name = "service", ClrType = typeof(string) },
            new() { Name = "count_", ClrType = typeof(long) },
            new() { Name = "avgDuration", ClrType = typeof(double) },
            new() { Name = "detail", ClrType = typeof(string) },
        };

        // Deterministic per query text so re-runs look stable but different queries differ.
        var seed = kql.GetHashCode(StringComparison.Ordinal);
        var rng = new Random(seed);
        var start = DateTimeOffset.Now.AddHours(-6);

        var rows = new List<object?[]>();
        for (var bin = 0; bin < 24; bin++)
        {
            var ts = start.AddMinutes(bin * 15);
            foreach (var service in Services)
            {
                var count = 40 + rng.Next(0, 60) + (service == "checkout-api" ? bin * 2 : 0);
                var avg = Math.Round(80 + rng.NextDouble() * 240, 1);
                var detail = $$"""{"service":"{{service}}","region":"westeurope","p95":{{Math.Round(avg * 1.8, 1)}},"errors":{{rng.Next(0, 5)}}}""";
                rows.Add([ts, service, (long)count, avg, detail]);
            }
        }

        return new QueryResultTable { Columns = columns, Rows = rows };
    }
}
