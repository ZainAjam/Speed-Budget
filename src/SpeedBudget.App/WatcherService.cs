using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using SpeedBudget.App.ViewModels;
using SpeedBudget.Core;
using SpeedBudget.Data;

namespace SpeedBudget.App;

public sealed class WatcherChip(string name, string remainingText, Brush brush)
{
    public string Name { get; } = name;
    public string RemainingText { get; } = remainingText;
    public Brush Brush { get; } = brush;
}

/// <summary>
/// Keeps the pinned-items strip current. It listens for saves on the main database, then recalculates on its
/// own read-only context so it never collides with whatever the user is editing.
/// </summary>
public sealed partial class WatcherService : ObservableObject
{
    private readonly BudgetStore _reader;
    private int _version;

    public ObservableCollection<WatcherChip> Chips { get; } = new();

    [ObservableProperty]
    public partial bool HasChips { get; set; }

    public WatcherService(SpeedBudgetDb main, SpeedBudgetDb readerDb)
    {
        _reader = new BudgetStore(readerDb);
        main.SavedChanges += (_, _) => _ = RefreshSoonAsync();
        _ = RefreshSoonAsync();
    }

    /// <summary>Waits for a burst of saves to settle, then refreshes once.</summary>
    private async Task RefreshSoonAsync()
    {
        var version = Interlocked.Increment(ref _version);
        await Task.Delay(250);
        if (version != _version) return;
        try { await RefreshAsync(); }
        catch { /* the strip is a convenience; a failed refresh leaves the previous chips in place */ }
    }

    private async Task RefreshAsync()
    {
        var pinned = await _reader.GetPinnedItemIdsAsync();
        var snapshot = await _reader.LoadSnapshotAsync();
        var current = BudgetEngine.Calculate(snapshot)[^1];
        var names = snapshot.Items.ToDictionary(i => i.Id, i => i.Name);
        var remaining = current.Items.ToDictionary(i => i.ItemId, i => i.Remaining);

        Chips.Clear();
        foreach (var id in pinned.Where(names.ContainsKey))
        {
            var left = remaining.GetValueOrDefault(id);
            Chips.Add(new WatcherChip(names[id], Format.Money(left), Format.Signed(left)));
        }
        HasChips = Chips.Count > 0;
    }
}
