using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TraceTiger.Services;

namespace TraceTiger.ViewModels;

/// <summary>A folder or .kql file in the library TreeView.</summary>
public partial class ScriptTreeItemViewModel : ObservableObject
{
    public ScriptTreeItemViewModel(ScriptTreeNode node)
    {
        Node = node;
        foreach (var child in node.Children)
            Children.Add(new ScriptTreeItemViewModel(child));
    }

    public ScriptTreeNode Node { get; }

    public string FullPath => Node.FullPath;
    public bool IsFolder => Node.IsFolder;
    public string FileName => Node.Name;

    /// <summary>@name metadata when present, else the file/folder name.</summary>
    public string Title => Node.DisplayName ?? Node.Name;

    public string? Description => Node.Description;

    public ObservableCollection<ScriptTreeItemViewModel> Children { get; } = new();

    [ObservableProperty]
    private bool _isExpanded = true;

    /// <summary>Preserves expansion state across rescans (matched by full path).</summary>
    public void CopyExpansionFrom(ScriptTreeItemViewModel old)
    {
        if (string.Equals(old.FullPath, FullPath, StringComparison.OrdinalIgnoreCase))
            IsExpanded = old.IsExpanded;

        foreach (var child in Children)
        {
            var match = old.Children.FirstOrDefault(c =>
                string.Equals(c.FullPath, child.FullPath, StringComparison.OrdinalIgnoreCase));
            if (match is not null) child.CopyExpansionFrom(match);
        }
    }
}
