using System.Collections.ObjectModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TraceTiger.Models;
using TraceTiger.Services;

namespace TraceTiger.ViewModels;

/// <summary>
/// One editor tab: KQL text (backed by a library file or scratch), connection + time range,
/// parameter strip, run/cancel, and the projected results (grid / chart / JSON / inspector).
/// </summary>
public partial class QueryTabViewModel : ObservableObject
{
    private readonly MainViewModel _owner;
    private CancellationTokenSource? _runCts;
    private int _runGeneration;
    private System.Timers.Timer? _paramDebounce;
    private QueryResultTable? _lastTable;
    private string? _lastResolvedKql;
    private bool _jsonStale = true;
    private bool _loadingText;

    public QueryTabViewModel(MainViewModel owner)
    {
        _owner = owner;
        _editorFontSize = owner.EditorFontSize;
        _editorWordWrap = owner.EditorWordWrap;
        _selectedTimeRange = TimeRangeCatalog.OptionFor(owner.DefaultTimeRange);
        _selectedConnection = owner.DefaultConnection();
    }

    // ---- editor / file state -------------------------------------------------------------

    [ObservableProperty] private string? _filePath;
    [ObservableProperty] private string _text = "";
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string _title = "untitled";
    [ObservableProperty] private double _editorFontSize;
    [ObservableProperty] private bool _editorWordWrap;

    public ObservableCollection<ParameterEntryViewModel> Parameters { get; } = new();

    [ObservableProperty] private bool _hasParameters;

    // ---- run configuration ---------------------------------------------------------------

    public ObservableCollection<Connection> Connections => _owner.Connections;
    public IReadOnlyList<TimeRangeOption> TimeRangeOptions { get; } = TimeRangeCatalog.Options;

    [ObservableProperty] private Connection? _selectedConnection;
    [ObservableProperty] private TimeRangeOption _selectedTimeRange;
    [ObservableProperty] private string _customFromText = "";
    [ObservableProperty] private string _customToText = "";
    [ObservableProperty] private bool _isRunning;

    public bool IsCustomRange => SelectedTimeRange.Kind == TimeRangeKind.Custom;

    // ---- results -------------------------------------------------------------------------

    [ObservableProperty] private DataView? _resultsView;
    [ObservableProperty] private ChartModel? _chart;
    [ObservableProperty] private string _jsonText = "";
    [ObservableProperty] private bool _jsonTruncated;
    [ObservableProperty] private ResultViewKind _resultView = ResultViewKind.Grid;
    [ObservableProperty] private ChartKind _chartKind = ChartKind.Line;
    [ObservableProperty] private bool _hasResult;
    [ObservableProperty] private string? _statusText;
    [ObservableProperty] private string? _errorText;
    [ObservableProperty] private string? _warningText;

    // ---- cell inspector ------------------------------------------------------------------

    [ObservableProperty] private bool _isInspectorOpen;
    [ObservableProperty] private string _inspectorTitle = "";
    [ObservableProperty] private string _inspectorText = "";

    public bool ChartOk => Chart?.Ok == true;

    // ---- text / title plumbing -------------------------------------------------------------

    /// <summary>Sets editor text without marking the tab dirty (file open, session restore).</summary>
    public void LoadText(string text, string? filePath)
    {
        _loadingText = true;
        FilePath = filePath;
        Text = text;
        IsDirty = false;
        _loadingText = false;
        RefreshTitle();
        DetectParameters();
    }

    partial void OnTextChanged(string value)
    {
        if (!_loadingText)
        {
            IsDirty = true;
            _owner.ScheduleSessionSave();
            ScheduleParameterDetection();
        }
    }

    partial void OnSelectedConnectionChanged(Connection? value)
    {
        if (value is not null) _owner.NoteConnectionUsed(value);
        _owner.ScheduleSessionSave();
    }

    partial void OnSelectedTimeRangeChanged(TimeRangeOption value)
    {
        OnPropertyChanged(nameof(IsCustomRange));
        _owner.ScheduleSessionSave();
    }

    partial void OnCustomFromTextChanged(string value) => _owner.ScheduleSessionSave();

    partial void OnCustomToTextChanged(string value) => _owner.ScheduleSessionSave();

    partial void OnChartChanged(ChartModel? value) => OnPropertyChanged(nameof(ChartOk));

    partial void OnResultViewChanged(ResultViewKind value)
    {
        if (value == ResultViewKind.Json && _jsonStale) RebuildJson();
    }

    partial void OnChartKindChanged(ChartKind value)
    {
        if (_lastTable is not null)
            Chart = ChartProjector.Project(_lastTable, value);
    }

    public void RefreshTitle()
    {
        var meta = ScriptMetadataParser.Parse(Text);
        Title = meta.Name
            ?? (FilePath is not null ? Path.GetFileNameWithoutExtension(FilePath) : "untitled");
    }

    // ---- parameter strip -------------------------------------------------------------------

    private void ScheduleParameterDetection()
    {
        _paramDebounce?.Stop();
        _paramDebounce?.Dispose();
        _paramDebounce = new System.Timers.Timer(500) { AutoReset = false };
        _paramDebounce.Elapsed += (_, _) =>
            Application.Current?.Dispatcher.Invoke(DetectParameters);
        _paramDebounce.Start();
    }

    /// <summary>Union of &lt;TOKENS&gt; in the KQL body and @param declarations in the header.
    /// Keeps values the user already typed; new tokens are prefilled from remembered values,
    /// then @param defaults.</summary>
    private void DetectParameters()
    {
        var meta = ScriptMetadataParser.Parse(Text);
        var tokens = new List<string>(QueryParameterResolver.FindUnresolvedTokens(Text));
        foreach (var p in meta.Parameters)
            if (!tokens.Contains(p.Name, StringComparer.OrdinalIgnoreCase))
                tokens.Add(p.Name);

        for (var i = Parameters.Count - 1; i >= 0; i--)
            if (!tokens.Contains(Parameters[i].Token, StringComparer.OrdinalIgnoreCase))
                Parameters.RemoveAt(i);

        foreach (var token in tokens)
        {
            if (Parameters.Any(p => string.Equals(p.Token, token, StringComparison.OrdinalIgnoreCase)))
                continue;
            var initial = _owner.RememberedParameterValue(token)
                ?? meta.Parameters.FirstOrDefault(p =>
                    string.Equals(p.Name, token, StringComparison.OrdinalIgnoreCase))?.DefaultValue;
            Parameters.Add(new ParameterEntryViewModel(token, initial));
        }

        HasParameters = Parameters.Count > 0;
        RefreshTitle();
    }

    // ---- run / cancel ------------------------------------------------------------------------

    [RelayCommand]
    private async Task RunOrCancelAsync()
    {
        if (IsRunning)
        {
            _runCts?.Cancel();
            return;
        }

        var connection = SelectedConnection;
        if (connection is null)
        {
            ErrorText = "Pick a connection first (gear icon to add one).";
            return;
        }

        ErrorText = null;
        WarningText = null;

        var values = Parameters.ToDictionary(p => p.Token, p => (string?)p.Value);
        var kql = QueryParameterResolver.Resolve(Text, values);
        var unresolved = QueryParameterResolver.FindUnresolvedTokens(kql);
        if (unresolved.Count > 0)
        {
            ErrorText = "Fill in: " + string.Join(", ", unresolved.Select(t => $"<{t}>"));
            return;
        }

        Azure.Monitor.Query.QueryTimeRange timeRange;
        if (SelectedTimeRange.Kind == TimeRangeKind.Custom)
        {
            if (!DateTime.TryParse(CustomFromText, out var from)
                || !DateTime.TryParse(CustomToText, out var to)
                || to <= from)
            {
                ErrorText = "Custom range needs valid From/To local times, e.g. 2026-07-01 09:00.";
                return;
            }
            timeRange = new Azure.Monitor.Query.QueryTimeRange(new DateTimeOffset(from), new DateTimeOffset(to));
        }
        else
        {
            timeRange = TimeRangeCatalog.ToQueryTimeRange(SelectedTimeRange.Kind);
        }

        _owner.RememberParameters(Parameters);

        var gen = ++_runGeneration;
        _runCts?.Cancel();
        _runCts = new CancellationTokenSource();
        var ct = _runCts.Token;

        IsRunning = true;
        StatusText = $"Running on {connection.Name}…";
        var stopwatch = Stopwatch.StartNew();
        _lastResolvedKql = kql;

        try
        {
            var (table, partialError) = await App.Kusto.RunAsync(connection, kql, timeRange, ct);
            stopwatch.Stop();
            if (gen != _runGeneration) return; // stale run — a newer one owns the UI

            PresentResult(table, connection, stopwatch.Elapsed, partialError);
            AddHistory(connection, stopwatch.Elapsed, table.RowCount, null);
        }
        catch (OperationCanceledException)
        {
            if (gen != _runGeneration) return;
            StatusText = "Cancelled";
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            if (gen != _runGeneration) return;
            var message = FirstLine(ex.Message);
            ErrorText = message;
            StatusText = null;
            AddHistory(connection, stopwatch.Elapsed, 0, message);
        }
        finally
        {
            if (gen == _runGeneration) IsRunning = false;
        }
    }

    private void PresentResult(QueryResultTable table, Connection connection, TimeSpan elapsed, string? partialError)
    {
        _lastTable = table;
        _jsonStale = true;

        ResultsView = DataTableBuilder.Build(table);

        var renderKind = DetectRenderDirective(Text);
        if (renderKind is not null) ChartKind = renderKind.Value;
        Chart = ChartProjector.Project(table, ChartKind);

        if (renderKind is not null && Chart.Ok)
            ResultView = ResultViewKind.Chart;
        else if (ResultView == ResultViewKind.Json)
            RebuildJson();

        HasResult = true;
        WarningText = partialError is null ? null : $"Partial results: {FirstLine(partialError)}";
        StatusText = $"{table.RowCount:N0} rows · {elapsed.TotalSeconds:0.0} s · {connection.Name}";
    }

    private void RebuildJson()
    {
        if (_lastTable is null) return;
        var (json, truncated) = JsonResultFormatter.ToJson(_lastTable);
        JsonText = json;
        JsonTruncated = truncated;
        _jsonStale = false;
    }

    private void AddHistory(Connection connection, TimeSpan elapsed, int rowCount, string? error)
    {
        App.History.Add(new RunHistoryEntry
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            ScriptName = Title,
            ConnectionName = connection.Name,
            ConnectionColorHex = connection.ColorHex,
            TimeRangeLabel = SelectedTimeRange.Label,
            DurationMs = elapsed.TotalMilliseconds,
            RowCount = rowCount,
            ErrorMessage = error,
            KqlText = Text,
            ResolvedKql = _lastResolvedKql ?? "",
        });
    }

    /// <summary>A trailing <c>| render timechart</c> (etc.) preselects the chart view.</summary>
    private static ChartKind? DetectRenderDirective(string kql)
    {
        var m = Regex.Match(kql, @"\|\s*render\s+(\w+)", RegexOptions.IgnoreCase | RegexOptions.RightToLeft);
        if (!m.Success) return null;
        return m.Groups[1].Value.ToLowerInvariant() switch
        {
            "barchart" or "columnchart" => Models.ChartKind.Column,
            "timechart" or "linechart" or "areachart" or "anomalychart" or "scatterchart" => Models.ChartKind.Line,
            _ => null,
        };
    }

    private static string FirstLine(string text)
    {
        var i = text.IndexOfAny(['\r', '\n']);
        return i < 0 ? text : text[..i];
    }

    // ---- save ---------------------------------------------------------------------------------

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (FilePath is null)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "KQL scripts (*.kql)|*.kql",
                DefaultExt = ".kql",
                FileName = Title == "untitled" ? "new-query.kql" : Title + ".kql",
                InitialDirectory = Directory.Exists(_owner.ScriptsFolder) ? _owner.ScriptsFolder : null,
            };
            if (dialog.ShowDialog() != true) return;
            FilePath = dialog.FileName;
        }

        await App.Library.SaveScriptAsync(FilePath, Text);
        IsDirty = false;
        RefreshTitle();
        _owner.RefreshLibrary();
        _owner.ScheduleSessionSave();
    }

    // ---- inspector -----------------------------------------------------------------------------

    /// <summary>Called by the view when a grid cell is double-clicked.</summary>
    public void InspectCell(string columnName, object? value)
    {
        var text = value is null or DBNull ? "" : value.ToString() ?? "";
        InspectorTitle = columnName;
        InspectorText = JsonResultFormatter.TryPrettyPrintCell(text);
        IsInspectorOpen = true;
    }

    [RelayCommand]
    private void CloseInspector() => IsInspectorOpen = false;

    [RelayCommand]
    private void CopyInspector()
    {
        if (InspectorText.Length > 0) Clipboard.SetText(InspectorText);
    }

    // ---- export ---------------------------------------------------------------------------------

    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        if (_lastTable is null) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv",
            DefaultExt = ".csv",
            FileName = $"{Title}-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
        };
        if (dialog.ShowDialog() != true) return;
        await File.WriteAllTextAsync(dialog.FileName, CsvExporter.ToCsv(_lastTable));
        StatusText = $"Exported {_lastTable.RowCount:N0} rows to {Path.GetFileName(dialog.FileName)}";
    }

    [RelayCommand]
    private void CopyTsv()
    {
        if (_lastTable is null) return;
        Clipboard.SetText(CsvExporter.ToTsv(_lastTable));
        StatusText = $"Copied {_lastTable.RowCount:N0} rows (paste into Excel)";
    }

    [RelayCommand]
    private void CopyKql()
    {
        var kql = _lastResolvedKql
            ?? QueryParameterResolver.Resolve(Text, Parameters.ToDictionary(p => p.Token, p => (string?)p.Value));
        Clipboard.SetText(kql);
        StatusText = "Copied resolved KQL";
    }

    // ---- session --------------------------------------------------------------------------------

    public TabSessionEntry ToSessionEntry() => new()
    {
        FilePath = FilePath,
        Text = FilePath is null || IsDirty ? Text : null,
        Title = Title,
        ConnectionName = SelectedConnection?.Name ?? "",
        TimeRange = SelectedTimeRange.Kind,
        CustomFromText = CustomFromText,
        CustomToText = CustomToText,
        IsDirty = IsDirty,
    };

    /// <summary>Stops any in-flight run when the tab is closed.</summary>
    public void Shutdown()
    {
        _runGeneration++;
        _runCts?.Cancel();
        _paramDebounce?.Dispose();
    }
}
