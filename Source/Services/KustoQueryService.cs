using System.IO;
using Azure.Core;
using Azure.Identity;
using Azure.Monitor.Query;
using TraceTiger.Models;

namespace TraceTiger.Services;

/// <summary>
/// Runs KQL against Azure via <see cref="LogsQueryClient"/>. One client per Entra tenant, built on
/// an <see cref="InteractiveBrowserCredential"/> with a DPAPI-persisted token cache — constructing
/// the credential does NOT sign in; the first run opens the browser. The resulting
/// <see cref="AuthenticationRecord"/> (account id only, no secrets) is saved next to the settings
/// so later launches can find the cached token silently instead of prompting again.
/// </summary>
public sealed class KustoQueryService
{
    // Azure Monitor Logs scope; the sign-in for it also yields the refresh token the client reuses.
    private static readonly string[] SignInScopes = ["https://api.loganalytics.io/.default"];

    // Without this a truncated/partially failed query throws and the rows Azure did return are lost.
    private static readonly LogsQueryOptions QueryOptions = new() { AllowPartialErrors = true };

    private readonly Dictionary<string, TenantSession> _sessions = new();
    private readonly DemoQuerySource _demo = new();

    /// <summary>Executes resolved KQL against the connection's target. Routing:
    /// workspace GUID → QueryWorkspaceAsync; ARM resource id → QueryResourceAsync; Demo → canned data.
    /// Returns the table plus a partial-error message when Azure returns incomplete results.</summary>
    public async Task<(QueryResultTable Table, string? PartialError)> RunAsync(
        Connection connection, string kql, QueryTimeRange timeRange, CancellationToken ct = default)
    {
        if (connection.Kind == ConnectionKind.Demo)
            return (await _demo.RunAsync(kql, ct), null);

        var session = SessionFor(connection);
        await EnsureSignedInAsync(session, ct).ConfigureAwait(false);
        var client = session.Client;

        if (connection.Kind == ConnectionKind.LogAnalyticsWorkspace
            && Guid.TryParse(connection.WorkspaceId.Trim(), out var workspaceGuid))
        {
            var resp = await client.QueryWorkspaceAsync(workspaceGuid.ToString(), kql, timeRange,
                QueryOptions, ct).ConfigureAwait(false);
            return (MapTable(resp.Value.Table), PartialErrorOf(resp.Value));
        }

        var resourceId = RequireResourceId(connection);
        var resource = await client.QueryResourceAsync(resourceId, kql, timeRange,
            QueryOptions, ct).ConfigureAwait(false);
        return (MapTable(resource.Value.Table), PartialErrorOf(resource.Value));
    }

    /// <summary>Cheap query that forces the interactive sign-in and proves RBAC on the target.</summary>
    public Task TestConnectionAsync(Connection connection, CancellationToken ct = default) =>
        RunAsync(connection, "print ok = 1", QueryTimeRange.All, ct);

    private TenantSession SessionFor(Connection connection)
    {
        var key = string.IsNullOrWhiteSpace(connection.TenantId) ? "" : connection.TenantId.Trim();
        lock (_sessions)
        {
            if (_sessions.TryGetValue(key, out var session)) return session;

            var recordPath = AuthRecordPathFor(key);
            var record = LoadAuthRecord(recordPath);
            var credential = new InteractiveBrowserCredential(new InteractiveBrowserCredentialOptions
            {
                TenantId = key.Length == 0 ? null : key,
                TokenCachePersistenceOptions = new TokenCachePersistenceOptions { Name = "TraceTiger" },
                AuthenticationRecord = record,
            });
            return _sessions[key] = new TenantSession(credential, new LogsQueryClient(credential), recordPath)
            {
                HasRecord = record is not null,
            };
        }
    }

    /// <summary>First use of a tenant with no saved record: sign in explicitly so the record can be
    /// captured and persisted. Serialized per tenant so two tabs don't open two browser prompts.</summary>
    private static async Task EnsureSignedInAsync(TenantSession session, CancellationToken ct)
    {
        if (session.HasRecord) return;

        await session.SignInGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (session.HasRecord) return;
            var record = await session.Credential
                .AuthenticateAsync(new TokenRequestContext(SignInScopes), ct).ConfigureAwait(false);
            session.HasRecord = true;
            SaveAuthRecord(session.RecordPath, record);
        }
        finally
        {
            session.SignInGate.Release();
        }
    }

    private static string AuthRecordPathFor(string tenantKey)
    {
        var name = tenantKey.Length == 0 ? "default" : tenantKey.ToLowerInvariant();
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TraceTiger", $"auth-{name}.json");
    }

    private static AuthenticationRecord? LoadAuthRecord(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var stream = File.OpenRead(path);
            return AuthenticationRecord.Deserialize(stream);
        }
        catch
        {
            return null; // unreadable record → just sign in again
        }
    }

    private static void SaveAuthRecord(string path, AuthenticationRecord record)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var stream = File.Create(path);
            record.Serialize(stream);
        }
        catch
        {
            // Best-effort: failing to save only means another prompt next launch.
        }
    }

    private sealed class TenantSession(
        InteractiveBrowserCredential credential, LogsQueryClient client, string recordPath)
    {
        public InteractiveBrowserCredential Credential { get; } = credential;
        public LogsQueryClient Client { get; } = client;
        public string RecordPath { get; } = recordPath;
        public SemaphoreSlim SignInGate { get; } = new(1, 1);
        public volatile bool HasRecord;
    }

    private static ResourceIdentifier RequireResourceId(Connection connection)
    {
        var id = connection.Kind == ConnectionKind.LogAnalyticsWorkspace && string.IsNullOrWhiteSpace(connection.ResourceId)
            ? connection.WorkspaceId
            : connection.ResourceId;
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidOperationException(
                $"Connection \"{connection.Name}\" has no resource id — set it in Settings.");
        try
        {
            return new ResourceIdentifier(id.Trim());
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException(
                $"Connection \"{connection.Name}\" has an invalid ARM resource id: {ex.Message}");
        }
    }

    private static string? PartialErrorOf(Azure.Monitor.Query.Models.LogsQueryResult result)
    {
        try
        {
            return result.Error?.Message;
        }
        catch
        {
            return null;
        }
    }

    private static QueryResultTable MapTable(Azure.Monitor.Query.Models.LogsTable table)
    {
        var columns = new List<QueryColumn>(table.Columns.Count);
        foreach (var c in table.Columns)
            columns.Add(new QueryColumn { Name = c.Name, ClrType = ClrTypeFor(c.Type) });

        var rows = new List<object?[]>(table.Rows.Count);
        foreach (var row in table.Rows)
        {
            var cells = new object?[table.Columns.Count];
            for (var i = 0; i < table.Columns.Count; i++)
                cells[i] = GetCell(row, i, table.Columns[i].Type);
            rows.Add(cells);
        }

        return new QueryResultTable { Columns = columns, Rows = rows };
    }

    private static Type ClrTypeFor(Azure.Monitor.Query.Models.LogsColumnType type)
    {
        if (type == Azure.Monitor.Query.Models.LogsColumnType.Bool) return typeof(bool);
        if (type == Azure.Monitor.Query.Models.LogsColumnType.Datetime) return typeof(DateTimeOffset);
        if (type == Azure.Monitor.Query.Models.LogsColumnType.Decimal) return typeof(decimal);
        if (type == Azure.Monitor.Query.Models.LogsColumnType.Int) return typeof(int);
        if (type == Azure.Monitor.Query.Models.LogsColumnType.Long) return typeof(long);
        if (type == Azure.Monitor.Query.Models.LogsColumnType.Real) return typeof(double);
        return typeof(string); // String, Dynamic, Guid, Timespan → render as text
    }

    private static object? GetCell(Azure.Monitor.Query.Models.LogsTableRow row, int i,
        Azure.Monitor.Query.Models.LogsColumnType type)
    {
        try
        {
            if (type == Azure.Monitor.Query.Models.LogsColumnType.Bool) return row.GetBoolean(i);
            if (type == Azure.Monitor.Query.Models.LogsColumnType.Datetime) return row.GetDateTimeOffset(i);
            if (type == Azure.Monitor.Query.Models.LogsColumnType.Decimal) return row.GetDecimal(i);
            if (type == Azure.Monitor.Query.Models.LogsColumnType.Int) return row.GetInt32(i);
            if (type == Azure.Monitor.Query.Models.LogsColumnType.Long) return row.GetInt64(i);
            if (type == Azure.Monitor.Query.Models.LogsColumnType.Real) return row.GetDouble(i);
            return row.GetString(i);
        }
        catch
        {
            return null;
        }
    }
}
