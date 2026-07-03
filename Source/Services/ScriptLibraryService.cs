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
/// external changes with a debounce. Saves made by the app itself are suppressed so a Ctrl+S
/// doesn't trigger a rescan echo.
/// </summary>
public sealed class ScriptLibraryService : IDisposable
{
    private FileSystemWatcher? _watcher;
    private System.Timers.Timer? _debounce;
    private readonly HashSet<string> _selfWrites = new(StringComparer.OrdinalIgnoreCase);
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
            _watcher = new FileSystemWatcher(folder, "*.kql")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
            };
            _watcher.Created += OnFsEvent;
            _watcher.Deleted += OnFsEvent;
            _watcher.Changed += OnFsEvent;
            _watcher.Renamed += OnFsEvent;
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
        lock (_gate) _selfWrites.Add(path);
        await AtomicFile.WriteAllTextAsync(path, text).ConfigureAwait(false);
    }

    public string CreateScript(string folder, string name)
    {
        var fileName = ScriptFileNameFrom(name);
        var path = Path.Combine(folder, fileName);
        if (File.Exists(path)) throw new InvalidOperationException($"\"{fileName}\" already exists here.");
        lock (_gate) _selfWrites.Add(path);
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
        if (File.Exists(newPath)) throw new InvalidOperationException($"\"{fileName}\" already exists here.");
        lock (_gate)
        {
            _selfWrites.Add(path);
            _selfWrites.Add(newPath);
        }
        File.Move(path, newPath);
        return newPath;
    }

    public void DeleteScript(string path)
    {
        lock (_gate) _selfWrites.Add(path);
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

    private void OnFsEvent(object sender, FileSystemEventArgs e)
    {
        lock (_gate)
        {
            // Swallow one echo per app-side write (save/create/rename/delete).
            if (_selfWrites.Remove(e.FullPath)) return;
            if (e is RenamedEventArgs r && _selfWrites.Remove(r.OldFullPath)) return;
        }

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
