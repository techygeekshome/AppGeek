using System.Text.Json;
using AppGeek.Models;

namespace AppGeek.Services;

/// <summary>Keeps the short "what happened recently" list shown on the dashboard.</summary>
public sealed class ActivityService
{
    private const int MaxEntries = 60;
    private readonly List<ActivityEntry> _entries = new();
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public IReadOnlyList<ActivityEntry> Entries => _entries;

    public void Load()
    {
        try
        {
            if (!File.Exists(AppPaths.ActivityFile)) return;
            var list = JsonSerializer.Deserialize<List<ActivityEntry>>(
                File.ReadAllText(AppPaths.ActivityFile), Options);
            if (list is not null)
            {
                _entries.Clear();
                _entries.AddRange(list.OrderByDescending(e => e.WhenUtc));
            }
        }
        catch (Exception ex) { Log.Warn("Activity log unreadable: " + ex.Message); }
    }

    public void Add(ActivityKind kind, string text)
    {
        _entries.Insert(0, new ActivityEntry { Kind = kind, Text = text });
        while (_entries.Count > MaxEntries) _entries.RemoveAt(_entries.Count - 1);
        Save();
    }

    private void Save()
    {
        try { File.WriteAllText(AppPaths.ActivityFile, JsonSerializer.Serialize(_entries, Options)); }
        catch (Exception ex) { Log.Debug("Activity log not saved: " + ex.Message); }
    }
}
