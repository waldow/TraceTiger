using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TraceTiger.Models;

namespace TraceTiger.ViewModels;

/// <summary>Editable wrapper around one <see cref="Connection"/> for the Settings form.</summary>
public partial class ConnectionEditViewModel : ObservableObject
{
    public ConnectionEditViewModel(Connection connection)
    {
        _name = connection.Name;
        _kind = connection.Kind;
        _resourceId = connection.ResourceId;
        _workspaceId = connection.WorkspaceId;
        _tenantId = connection.TenantId;
        _colorHex = connection.ColorHex;
    }

    [ObservableProperty] private string _name;
    [ObservableProperty] private ConnectionKind _kind;
    [ObservableProperty] private string _resourceId;
    [ObservableProperty] private string _workspaceId;
    [ObservableProperty] private string _tenantId;
    [ObservableProperty] private string _colorHex;
    [ObservableProperty] private string? _testStatus;
    [ObservableProperty] private bool _testPassed;
    [ObservableProperty] private bool _isTesting;

    public bool IsWorkspace => Kind == ConnectionKind.LogAnalyticsWorkspace;
    public bool IsDemo => Kind == ConnectionKind.Demo;

    partial void OnKindChanged(ConnectionKind value)
    {
        OnPropertyChanged(nameof(IsWorkspace));
        OnPropertyChanged(nameof(IsDemo));
    }

    public Connection ToModel() => new()
    {
        Name = string.IsNullOrWhiteSpace(Name) ? "unnamed" : Name.Trim(),
        Kind = Kind,
        ResourceId = ResourceId.Trim(),
        WorkspaceId = WorkspaceId.Trim(),
        TenantId = TenantId.Trim(),
        ColorHex = ColorHex,
    };

    [RelayCommand]
    private async Task TestAsync()
    {
        IsTesting = true;
        TestStatus = "Signing in & testing…";
        TestPassed = false;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await App.Kusto.TestConnectionAsync(ToModel(), cts.Token);
            TestStatus = "Connected ✓";
            TestPassed = true;
        }
        catch (OperationCanceledException)
        {
            TestStatus = "Timed out — sign-in not completed";
        }
        catch (Exception ex)
        {
            var message = ex.Message;
            var newline = message.IndexOfAny(['\r', '\n']);
            TestStatus = newline < 0 ? message : message[..newline];
        }
        finally
        {
            IsTesting = false;
        }
    }
}

/// <summary>Clone-edit-commit settings editor: works on a copy, produces a new model on Save.</summary>
public partial class SettingsViewModel : ObservableObject
{
    private static readonly string[] BadgeColors =
        ["#58A6FF", "#3FB950", "#F85149", "#D29922", "#BC8CFF", "#F0883E"];

    public SettingsViewModel(SettingsModel working)
    {
        _scriptsFolder = working.ScriptsFolder;
        _editorFontSize = working.EditorFontSize;
        _editorWordWrap = working.EditorWordWrap;
        _defaultTimeRange = working.DefaultTimeRange;
        LastConnectionName = working.LastConnectionName;

        foreach (var c in working.Connections)
            Connections.Add(new ConnectionEditViewModel(c));
        SelectedConnection = Connections.FirstOrDefault();
    }

    public string LastConnectionName { get; }

    public ObservableCollection<ConnectionEditViewModel> Connections { get; } = new();

    [ObservableProperty] private ConnectionEditViewModel? _selectedConnection;
    [ObservableProperty] private string _scriptsFolder;
    [ObservableProperty] private double _editorFontSize;
    [ObservableProperty] private bool _editorWordWrap;
    [ObservableProperty] private Models.TimeRangeKind _defaultTimeRange;

    public IReadOnlyList<Services.TimeRangeOption> TimeRangeOptions { get; } = Services.TimeRangeCatalog.Options;
    public IReadOnlyList<string> ColorChoices { get; } = BadgeColors;

    [RelayCommand]
    private void AddConnection()
    {
        var vm = new ConnectionEditViewModel(new Connection
        {
            Name = $"connection-{Connections.Count + 1}",
            ColorHex = BadgeColors[Connections.Count % BadgeColors.Length],
        });
        Connections.Add(vm);
        SelectedConnection = vm;
    }

    [RelayCommand]
    private void DuplicateConnection(ConnectionEditViewModel? item)
    {
        item ??= SelectedConnection;
        if (item is null) return;
        var copy = item.ToModel();
        copy.Name += "-copy";
        var vm = new ConnectionEditViewModel(copy);
        Connections.Insert(Connections.IndexOf(item) + 1, vm);
        SelectedConnection = vm;
    }

    [RelayCommand]
    private void RemoveConnection(ConnectionEditViewModel? item)
    {
        item ??= SelectedConnection;
        if (item is null) return;
        var index = Connections.IndexOf(item);
        Connections.Remove(item);
        SelectedConnection = Connections.Count == 0
            ? null
            : Connections[Math.Clamp(index, 0, Connections.Count - 1)];
    }

    [RelayCommand]
    private void BrowseScriptsFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose your KQL scripts folder" };
        if (Directory.Exists(ScriptsFolder))
            dialog.InitialDirectory = ScriptsFolder;
        if (dialog.ShowDialog() == true)
            ScriptsFolder = dialog.FolderName;
    }

    public SettingsModel ToModel() => new()
    {
        ScriptsFolder = ScriptsFolder.Trim(),
        Connections = Connections.Select(c => c.ToModel()).ToList(),
        LastConnectionName = LastConnectionName,
        DefaultTimeRange = DefaultTimeRange,
        EditorFontSize = Math.Clamp(EditorFontSize, 10, 22),
        EditorWordWrap = EditorWordWrap,
    };
}
