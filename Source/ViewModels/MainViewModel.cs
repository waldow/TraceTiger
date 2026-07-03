using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TraceTiger.Models;
using TraceTiger.Services;
using TraceTiger.Views;

namespace TraceTiger.ViewModels;

/// <summary>
/// The shell: tab lifecycle, script library tree, run history, settings, and session persistence.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly PersistenceService _persistence;
    private readonly ScriptLibraryService _library;
    private readonly HistoryService _history;
    private SettingsModel _settings;
    private Dictionary<string, string> _paramValues;
    private System.Timers.Timer? _sessionSaveDebounce;
    private bool _restoringSession;

    public MainViewModel(PersistenceService persistence, ScriptLibraryService library, HistoryService history)
    {
        _persistence = persistence;
        _library = library;
        _history = history;
        _settings = persistence.LoadSettings();
        _paramValues = persistence.LoadParameterValues();

        foreach (var c in _settings.Connections)
            Connections.Add(c);

        _library.LibraryChanged += () =>
            Application.Current?.Dispatcher.BeginInvoke(RefreshLibrary);
        _library.SetFolder(_settings.ScriptsFolder);
        RefreshLibrary();
    }

    // ---- settings passthroughs ------------------------------------------------------------

    public ObservableCollection<Connection> Connections { get; } = new();
    public string ScriptsFolder => _settings.ScriptsFolder;
    public double EditorFontSize => _settings.EditorFontSize;
    public bool EditorWordWrap => _settings.EditorWordWrap;
    public TimeRangeKind DefaultTimeRange => _settings.DefaultTimeRange;

    public Connection? DefaultConnection() =>
        Connections.FirstOrDefault(c => c.Name == _settings.LastConnectionName)
        ?? Connections.FirstOrDefault();

    public void NoteConnectionUsed(Connection connection)
    {
        if (_settings.LastConnectionName == connection.Name) return;
        _settings.LastConnectionName = connection.Name;
        _persistence.SaveSettings(_settings);
    }

    // ---- parameter memory -------------------------------------------------------------------

    public string? RememberedParameterValue(string token) =>
        _paramValues.TryGetValue(token, out var v) ? v : null;

    public void RememberParameters(IEnumerable<ParameterEntryViewModel> parameters)
    {
        var changed = false;
        foreach (var p in parameters)
        {
            if (string.IsNullOrWhiteSpace(p.Value)) continue;
            if (_paramValues.TryGetValue(p.Token, out var existing) && existing == p.Value) continue;
            _paramValues[p.Token] = p.Value;
            changed = true;
        }
        if (changed) _persistence.SaveParameterValues(_paramValues);
    }

    // ---- tabs ---------------------------------------------------------------------------------

    public ObservableCollection<QueryTabViewModel> Tabs { get; } = new();

    [ObservableProperty] private QueryTabViewModel? _selectedTab;
    [ObservableProperty] private int _leftPanelTabIndex;

    partial void OnSelectedTabChanged(QueryTabViewModel? value) => ScheduleSessionSave();

    [RelayCommand]
    private void NewTab()
    {
        var tab = new QueryTabViewModel(this);
        tab.LoadText("", null);
        Tabs.Add(tab);
        SelectedTab = tab;
    }

    [RelayCommand]
    private void CloseTab(QueryTabViewModel? tab)
    {
        tab ??= SelectedTab;
        if (tab is null) return;

        if (tab.IsDirty && tab.Text.Trim().Length > 0)
        {
            var keep = ConfirmDialog.Show(
                "Close tab?",
                $"\"{tab.Title}\" has unsaved changes that will be lost.",
                confirmLabel: "Close anyway");
            if (!keep) return;
        }

        var index = Tabs.IndexOf(tab);
        tab.Shutdown();
        Tabs.Remove(tab);

        if (Tabs.Count == 0)
            NewTab();
        else if (SelectedTab is null || SelectedTab == tab)
            SelectedTab = Tabs[Math.Clamp(index, 0, Tabs.Count - 1)];

        ScheduleSessionSave();
    }

    [RelayCommand]
    private void CloseSelectedTab() => CloseTab(SelectedTab);

    [RelayCommand]
    private void NextTab() => CycleTab(+1);

    [RelayCommand]
    private void PreviousTab() => CycleTab(-1);

    private void CycleTab(int direction)
    {
        if (Tabs.Count < 2 || SelectedTab is null) return;
        var index = (Tabs.IndexOf(SelectedTab) + direction + Tabs.Count) % Tabs.Count;
        SelectedTab = Tabs[index];
    }

    // ---- script library --------------------------------------------------------------------------

    public ObservableCollection<ScriptTreeItemViewModel> LibraryItems { get; } = new();

    [ObservableProperty] private bool _hasScriptsFolder;

    [RelayCommand]
    public void RefreshLibrary()
    {
        var root = _library.BuildTree();
        HasScriptsFolder = root is not null;

        var old = LibraryItems.ToList();
        LibraryItems.Clear();
        if (root is null) return;

        foreach (var child in root.Children)
        {
            var item = new ScriptTreeItemViewModel(child);
            var match = old.FirstOrDefault(o =>
                string.Equals(o.FullPath, item.FullPath, StringComparison.OrdinalIgnoreCase));
            if (match is not null) item.CopyExpansionFrom(match);
            LibraryItems.Add(item);
        }
    }

    [RelayCommand]
    private void ChooseScriptsFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose your KQL scripts folder" };
        if (Directory.Exists(_settings.ScriptsFolder))
            dialog.InitialDirectory = _settings.ScriptsFolder;
        if (dialog.ShowDialog() != true) return;

        _settings.ScriptsFolder = dialog.FolderName;
        _persistence.SaveSettings(_settings);
        _library.SetFolder(dialog.FolderName);
        RefreshLibrary();
    }

    [RelayCommand]
    private void OpenScript(ScriptTreeItemViewModel? item)
    {
        if (item is null || item.IsFolder) return;
        OpenScriptFile(item.FullPath);
    }

    public void OpenScriptFile(string path)
    {
        var existing = Tabs.FirstOrDefault(t =>
            string.Equals(t.FilePath, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            SelectedTab = existing;
            return;
        }

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "TraceTiger", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var tab = new QueryTabViewModel(this);
        tab.LoadText(text, path);
        Tabs.Add(tab);
        SelectedTab = tab;
        ScheduleSessionSave();
    }

    [RelayCommand]
    private void NewScript(ScriptTreeItemViewModel? parent)
    {
        var folder = parent?.IsFolder == true ? parent.FullPath : _settings.ScriptsFolder;
        if (!Directory.Exists(folder)) return;

        var name = RenameDialog.Show("New script", "Script name:", "new-query");
        if (string.IsNullOrWhiteSpace(name)) return;

        try
        {
            var path = _library.CreateScript(folder, name.Trim());
            RefreshLibrary();
            OpenScriptFile(path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "TraceTiger", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private void NewFolder(ScriptTreeItemViewModel? parent)
    {
        var folder = parent?.IsFolder == true ? parent.FullPath : _settings.ScriptsFolder;
        if (!Directory.Exists(folder)) return;

        var name = RenameDialog.Show("New folder", "Folder name:", "new-folder");
        if (string.IsNullOrWhiteSpace(name)) return;

        try
        {
            _library.CreateFolder(folder, name.Trim());
            RefreshLibrary();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "TraceTiger", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private void RenameScript(ScriptTreeItemViewModel? item)
    {
        if (item is null || item.IsFolder) return;

        var name = RenameDialog.Show("Rename script", "New name:",
            Path.GetFileNameWithoutExtension(item.FileName));
        if (string.IsNullOrWhiteSpace(name)) return;

        try
        {
            var newPath = _library.RenameScript(item.FullPath, name.Trim());
            foreach (var tab in Tabs.Where(t =>
                string.Equals(t.FilePath, item.FullPath, StringComparison.OrdinalIgnoreCase)))
            {
                tab.FilePath = newPath;
                tab.RefreshTitle();
            }
            RefreshLibrary();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "TraceTiger", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private void DeleteScript(ScriptTreeItemViewModel? item)
    {
        if (item is null || item.IsFolder) return;

        if (!ConfirmDialog.Show("Delete script?",
            $"\"{item.Title}\" will be deleted from disk.", confirmLabel: "Delete"))
            return;

        try
        {
            _library.DeleteScript(item.FullPath);

            // An open tab keeps the text but becomes a dirty scratch tab so nothing is lost.
            foreach (var tab in Tabs.Where(t =>
                string.Equals(t.FilePath, item.FullPath, StringComparison.OrdinalIgnoreCase)))
            {
                tab.FilePath = null;
                tab.IsDirty = true;
            }
            RefreshLibrary();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "TraceTiger", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private void RevealInExplorer(ScriptTreeItemViewModel? item)
    {
        var path = item?.FullPath ?? _settings.ScriptsFolder;
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            Process.Start("explorer.exe",
                item?.IsFolder == false ? $"/select,\"{path}\"" : $"\"{path}\"");
        }
        catch (Exception)
        {
            // Explorer refusing to open is not worth a dialog.
        }
    }

    // ---- history ----------------------------------------------------------------------------------

    public ObservableCollection<RunHistoryEntry> HistoryEntries => _history.Entries;

    [RelayCommand]
    private void OpenHistoryEntry(RunHistoryEntry? entry)
    {
        if (entry is null) return;

        var tab = new QueryTabViewModel(this);
        tab.LoadText(entry.KqlText, null);
        tab.SelectedConnection = Connections.FirstOrDefault(c => c.Name == entry.ConnectionName)
            ?? tab.SelectedConnection;
        Tabs.Add(tab);
        SelectedTab = tab;
    }

    [RelayCommand]
    private void ClearHistory()
    {
        if (HistoryEntries.Count == 0) return;
        if (ConfirmDialog.Show("Clear history?",
            $"All {HistoryEntries.Count} run history entries will be removed.", confirmLabel: "Clear"))
            _history.Clear();
    }

    // ---- settings window ---------------------------------------------------------------------------

    [RelayCommand]
    private void OpenSettings()
    {
        var vm = new SettingsViewModel(_settings.Clone());
        var window = new SettingsWindow(vm) { Owner = Application.Current?.MainWindow };
        if (window.ShowDialog() != true) return;

        var updated = vm.ToModel();
        var folderChanged = !string.Equals(_settings.ScriptsFolder, updated.ScriptsFolder,
            StringComparison.OrdinalIgnoreCase);

        _settings = updated;
        _persistence.SaveSettings(_settings);

        Connections.Clear();
        foreach (var c in _settings.Connections)
            Connections.Add(c);

        foreach (var tab in Tabs)
        {
            tab.EditorFontSize = _settings.EditorFontSize;
            tab.EditorWordWrap = _settings.EditorWordWrap;
            if (tab.SelectedConnection is not null
                && !Connections.Contains(tab.SelectedConnection))
            {
                tab.SelectedConnection = Connections.FirstOrDefault(c =>
                    c.Name == tab.SelectedConnection.Name) ?? Connections.FirstOrDefault();
            }
        }

        if (folderChanged)
        {
            _library.SetFolder(_settings.ScriptsFolder);
            RefreshLibrary();
        }
    }

    // ---- session persistence --------------------------------------------------------------------------

    public void ScheduleSessionSave()
    {
        if (_restoringSession) return;
        _sessionSaveDebounce?.Stop();
        _sessionSaveDebounce?.Dispose();
        _sessionSaveDebounce = new System.Timers.Timer(1000) { AutoReset = false };
        _sessionSaveDebounce.Elapsed += (_, _) =>
            Application.Current?.Dispatcher.BeginInvoke(SaveSessionNow);
        _sessionSaveDebounce.Start();
    }

    public void SaveSessionNow()
    {
        _persistence.SaveTabSession(new TabSessionModel
        {
            Tabs = Tabs.Select(t => t.ToSessionEntry()).ToList(),
            SelectedIndex = SelectedTab is null ? 0 : Tabs.IndexOf(SelectedTab),
        });
    }

    public void RestoreSession()
    {
        _restoringSession = true;
        try
        {
            var session = _persistence.LoadTabSession();
            foreach (var entry in session.Tabs)
            {
                var tab = new QueryTabViewModel(this);

                if (entry.FilePath is not null && entry.Text is null && File.Exists(entry.FilePath))
                {
                    try
                    {
                        tab.LoadText(File.ReadAllText(entry.FilePath), entry.FilePath);
                    }
                    catch (IOException)
                    {
                        continue;
                    }
                }
                else if (entry.Text is not null)
                {
                    tab.LoadText(entry.Text, File.Exists(entry.FilePath) ? entry.FilePath : null);
                    tab.IsDirty = entry.IsDirty;
                }
                else
                {
                    continue;
                }

                tab.SelectedConnection = Connections.FirstOrDefault(c => c.Name == entry.ConnectionName)
                    ?? tab.SelectedConnection;
                tab.SelectedTimeRange = TimeRangeCatalog.OptionFor(entry.TimeRange);
                Tabs.Add(tab);
            }

            if (Tabs.Count == 0)
                NewTab();
            SelectedTab = Tabs[Math.Clamp(session.SelectedIndex, 0, Tabs.Count - 1)];
        }
        finally
        {
            _restoringSession = false;
        }
    }
}
