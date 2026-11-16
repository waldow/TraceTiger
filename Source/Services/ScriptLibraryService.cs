using System.IO;

namespace TraceTiger.Services;

/// <summary>A node in the library tree — a folder or a .kql file.</summary>
public sealed class ScriptTreeNode
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required bool IsFolder { get; init; }
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public List<ScriptTreeNode> Children { get; } = new();
}

/// <summary>
/// Scans the scripts folder for .kql files (recursive), exposes them as a tree, and watches for
/// external changes (files and folders) with a debounce. Writes made by the app itself are
/// suppressed for a short window so a Ctrl+S doesn't trigger a rescan echo.
/// </summary>
public sealed class ScriptLibraryService : IDisposable
{
    private FileSystemWatcher? _watcher;
    private System.Timers.Timer? _debounce;
    // Path → time until which watcher events for it are the app's own echo. Time-boxed so an entry
    // whose event never arrives can't swallow a real external change later.
    private static readonly TimeSpan SelfWriteWindow = TimeSpan.FromSeconds(2);
    private readonly Dictionary<string, DateTime> _selfWrites = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();

    /// <summary>Raised (on a worker thread) after external changes settle; UI should rescan.</summary>
    public event Action? LibraryChanged;

    public string Folder { get; private set; } = "";

    public void SetFolder(string folder)
    {
        Folder = folder;
        _watcher?.Dispose();
        _watcher = null;

        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;

        try
        {
            // No "*.kql" filter: it would also filter out folder create/rename/delete events.
            // Relevance is decided per event in OnFsEvent instead.
            _watcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                InternalBufferSize = 64 * 1024,
            };
            _watcher.Created += OnFsEvent;
            _watcher.Deleted += OnFsEvent;
            _watcher.Changed += OnFsEvent;
            _watcher.Renamed += OnFsEvent;
            // Buffer overflow (huge checkout) loses individual events — rescan to catch up.
            _watcher.Error += (_, _) => ScheduleLibraryChanged();
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception)
        {
            _watcher = null; // network/WSL paths can refuse watchers — manual Refresh still works
        }
    }

    /// <summary>Builds the current tree: folders first, ordinal-ignore-case sort, dot-dirs skipped.
    /// Empty folders are kept so new scripts can be created inside them.</summary>
    public ScriptTreeNode? BuildTree()
    {
        if (string.IsNullOrWhiteSpace(Folder) || !Directory.Exists(Folder)) return null;
        return BuildNode(new DirectoryInfo(Folder));
    }

    private static ScriptTreeNode BuildNode(DirectoryInfo dir)
    {
        var node = new ScriptTreeNode { Name = dir.Name, FullPath = dir.FullName, IsFolder = true };

        try
        {
            foreach (var sub in dir.GetDirectories().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (sub.Name.StartsWith('.')) continue;
                node.Children.Add(BuildNode(sub));
            }

            foreach (var file in dir.GetFiles("*.kql").OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
            {
                var fileNode = new ScriptTreeNode { Name = file.Name, FullPath = file.FullName, IsFolder = false };
                try
                {
                    var meta = ScriptMetadataParser.Parse(File.ReadAllText(file.FullName));
                    fileNode.DisplayName = meta.Name;
                    fileNode.Description = meta.Description;
                }
                catch (IOException) { }
                node.Children.Add(fileNode);
            }
        }
        catch (UnauthorizedAccessException) { }

        return node;
    }

    public async Task SaveScriptAsync(string path, string text)
    {
        NoteSelfWrite(path);
        await AtomicFile.WriteAllTextAsync(path, text).ConfigureAwait(false);
    }

    public string CreateScript(string folder, string name)
    {
        var fileName = ScriptFileNameFrom(name);
        var path = Path.Combine(folder, fileName);
        if (File.Exists(path)) throw new InvalidOperationException($"\"{fileName}\" already exists here.");
        NoteSelfWrite(path);
        AtomicFile.WriteAllText(path, ScriptMetadataParser.NewScriptTemplate(
            Path.GetFileNameWithoutExtension(fileName)));
        return path;
    }

    public string CreateFolder(string parent, string name)
    {
        var folderName = ChildNameFrom(name, "folder name");
        var path = Path.Combine(parent, folderName);
        Directory.CreateDirectory(path);
        return path;
    }

    public string RenameScript(string path, string newName)
    {
        var fileName = ScriptFileNameFrom(newName);
        var newPath = Path.Combine(Path.GetDirectoryName(path)!, fileName);
        if (string.Equals(path, newPath, StringComparison.Ordinal)) return path;

        // On case-insensitive file systems a case-only rename "exists" already — as itself.
        var caseOnlyRename = string.Equals(path, newPath, StringComparison.OrdinalIgnoreCase);
        if (!caseOnlyRename && File.Exists(newPath))
            throw new InvalidOperationException($"\"{fileName}\" already exists here.");

        NoteSelfWrite(path, newPath);
        File.Move(path, newPath);
        return newPath;
    }

    public void DeleteScript(string path)
    {
        NoteSelfWrite(path);
        File.Delete(path);
    }

    private static string ScriptFileNameFrom(string name)
    {
        var trimmed = ChildNameFrom(name, "script name");
        var fileName = trimmed.EndsWith(".kql", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : trimmed + ".kql";

        if (string.IsNullOrWhiteSpace(Path.GetFileNameWithoutExtension(fileName)))
            throw new InvalidOperationException("Enter a script name.");

        return ChildNameFrom(fileName, "script name");
    }

    private static string ChildNameFrom(string name, string label)
    {
        var trimmed = name.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new InvalidOperationException($"Enter a {label}.");

        if (trimmed is "." or ".."
            || trimmed.EndsWith(".", StringComparison.Ordinal)
            || Path.IsPathFullyQualified(trimmed)
            || trimmed.IndexOf(Path.DirectorySeparatorChar) >= 0
            || trimmed.IndexOf(Path.AltDirectorySeparatorChar) >= 0
            || trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || !string.Equals(Path.GetFileName(trimmed), trimmed, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"\"{trimmed}\" is not a valid {label}. Use a name only, not a path.");
        }

        return trimmed;
    }

    private void NoteSelfWrite(params string[] paths)
    {
        var now = DateTime.UtcNow;
        lock (_gate)
        {
            foreach (var stale in _selfWrites.Where(p => p.Value < now).Select(p => p.Key).ToList())
                _selfWrites.Remove(stale);
            foreach (var path in paths)
                _selfWrites[path] = now + SelfWriteWindow;
        }
    }

    private bool IsSelfWrite(string path)
    {
        lock (_gate)
        {
            if (!_selfWrites.TryGetValue(path, out var until)) return false;
            if (until >= DateTime.UtcNow) return true;
            _selfWrites.Remove(path);
            return false;
        }
    }

    private void OnFsEvent(object sender, FileSystemEventArgs e)
    {
        var oldPath = (e as RenamedEventArgs)?.OldFullPath;

        var relevant = IsRelevant(e.FullPath, e.ChangeType)
            || (oldPath is not null && IsRelevant(oldPath, WatcherChangeTypes.Deleted));
        if (!relevant) return;

        // Swallow echoes of app-side writes (save/create/rename/delete).
        if (IsSelfWrite(e.FullPath) || (oldPath is not null && IsSelfWrite(oldPath))) return;

        ScheduleLibraryChanged();
    }

    /// <summary>Does this event affect what the tree shows? .kql files always; folders on
    /// create/rename/delete. Dot-directories (.git, …) are not in the tree, so they're ignored.</summary>
    private bool IsRelevant(string fullPath, WatcherChangeTypes change)
    {
        var relative = Path.GetRelativePath(Folder, fullPath);
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            if (segment.Length > 1 && segment.StartsWith('.') && segment != "..") return false;

        if (fullPath.EndsWith(".kql", StringComparison.OrdinalIgnoreCase)) return true;

        // A folder's LastWrite changes whenever anything inside it does — not interesting by itself.
        if (change == WatcherChangeTypes.Changed) return false;

        // Something that is gone can't be probed; the only cheap tell for "was a folder" is that it
        // doesn't look like a file. A spurious rescan is harmless.
        if (change == WatcherChangeTypes.Deleted) return !Path.HasExtension(fullPath);

        return Directory.Exists(fullPath);
    }

    private void ScheduleLibraryChanged()
    {
        // Debounce bursts (editors write temp + rename; git checkouts touch many files).
        lock (_gate)
        {
            _debounce?.Stop();
            _debounce?.Dispose();
            _debounce = new System.Timers.Timer(300) { AutoReset = false };
            _debounce.Elapsed += (_, _) => LibraryChanged?.Invoke();
            _debounce.Start();
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounce?.Dispose();
    }
}
