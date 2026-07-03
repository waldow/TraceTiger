using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using TraceTiger.Models;

namespace TraceTiger.Services;

/// <summary>
/// Owns everything under %AppData%\TraceTiger\ — settings.json, history.json, tabs.json,
/// paramvalues.json. camelCase indented JSON, atomic writes, corruption-tolerant loads
/// (a bad file yields defaults, never a startup crash).
/// </summary>
public sealed class PersistenceService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string _dir;

    public PersistenceService()
    {
        _dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TraceTiger");
        Directory.CreateDirectory(_dir);
    }

    private string PathFor(string file) => Path.Combine(_dir, file);

    public SettingsModel LoadSettings() => Load<SettingsModel>("settings.json") ?? SeedDefaultSettings();
    public void SaveSettings(SettingsModel settings) => Save("settings.json", settings);

    public List<RunHistoryEntry> LoadHistory() => Load<List<RunHistoryEntry>>("history.json") ?? new();
    public void SaveHistory(IEnumerable<RunHistoryEntry> entries) => Save("history.json", entries);

    public TabSessionModel LoadTabSession() => Load<TabSessionModel>("tabs.json") ?? new();
    public void SaveTabSession(TabSessionModel session) => Save("tabs.json", session);

    public Dictionary<string, string> LoadParameterValues() =>
        Load<Dictionary<string, string>>("paramvalues.json") ?? new(StringComparer.OrdinalIgnoreCase);
    public void SaveParameterValues(Dictionary<string, string> values) => Save("paramvalues.json", values);

    private T? Load<T>(string file) where T : class
    {
        try
        {
            var path = PathFor(file);
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions);
        }
        catch
        {
            return null; // corrupt/unreadable file → caller falls back to defaults
        }
    }

    private void Save<T>(string file, T value)
    {
        try
        {
            AtomicFile.WriteAllText(PathFor(file), JsonSerializer.Serialize(value, JsonOptions));
        }
        catch
        {
            // Persistence is best-effort; never take the app down over a save.
        }
    }

    private static SettingsModel SeedDefaultSettings() => new()
    {
        Connections =
        {
            new Connection
            {
                Name = "Demo",
                Kind = ConnectionKind.Demo,
                ColorHex = "#BC8CFF",
            },
        },
        LastConnectionName = "Demo",
    };
}
