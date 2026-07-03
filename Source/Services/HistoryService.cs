using System.Collections.ObjectModel;
using TraceTiger.Models;

namespace TraceTiger.Services;

/// <summary>Bounded, persisted run history. Newest first, capped at 200 entries.</summary>
public sealed class HistoryService
{
    private const int MaxEntries = 200;
    private readonly PersistenceService _persistence;

    public ObservableCollection<RunHistoryEntry> Entries { get; } = new();

    public HistoryService(PersistenceService persistence)
    {
        _persistence = persistence;
        foreach (var e in persistence.LoadHistory().Take(MaxEntries))
            Entries.Add(e);
    }

    public void Add(RunHistoryEntry entry)
    {
        Entries.Insert(0, entry);
        while (Entries.Count > MaxEntries)
            Entries.RemoveAt(Entries.Count - 1);
        Save();
    }

    public void Clear()
    {
        Entries.Clear();
        Save();
    }

    private void Save() => _persistence.SaveHistory(Entries);
}
