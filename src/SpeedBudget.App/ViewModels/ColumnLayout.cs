using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;

namespace SpeedBudget.App.ViewModels;

/// <summary>
/// The widths of the transaction table's columns and of the edit panel beside it. The heading and every row bind to
/// the one shared instance, so dragging a column's edge resizes that column all the way down. The Counterparty
/// column takes whatever room is left, so it has no width of its own. The sizes are remembered between runs.
/// </summary>
public sealed partial class ColumnLayout : ObservableObject
{
    public static ColumnLayout Current { get; } = new();

    /// <summary>The last column of the table holds the delete button, and is just wide enough for it.</summary>
    public const double ActionsMinimum = 76;

    [ObservableProperty] public partial double Date { get; set; } = 128;
    [ObservableProperty] public partial double BankDate { get; set; } = 128;
    [ObservableProperty] public partial double Counterparty { get; set; } = 240;
    [ObservableProperty] public partial double Memo { get; set; } = 160;
    [ObservableProperty] public partial double Item { get; set; } = 200;
    [ObservableProperty] public partial double Account { get; set; } = 140;
    [ObservableProperty] public partial double Outflow { get; set; } = 100;
    [ObservableProperty] public partial double Inflow { get; set; } = 100;
    [ObservableProperty] public partial double Cleared { get; set; } = 72;

    /// <summary>The width of the edit panel on the right, including its left padding.</summary>
    [ObservableProperty] public partial double DetailWidth { get; set; } = 344;

    /// <summary>A tracked account has no budget item column, and a single account's page has no account column.</summary>
    [ObservableProperty] public partial bool ShowItem { get; set; } = true;
    [ObservableProperty] public partial bool ShowAccount { get; set; } = true;

    public GridLength DateColumn => new(Date);
    public GridLength BankDateColumn => new(BankDate);
    public GridLength CounterpartyColumn => new(Counterparty);
    public GridLength MemoColumn => new(Memo);
    public GridLength ItemColumn => new(ShowItem ? Item : 0);
    public GridLength AccountColumn => new(ShowAccount ? Account : 0);
    public GridLength OutflowColumn => new(Outflow);
    public GridLength InflowColumn => new(Inflow);
    public GridLength ClearedColumn => new(Cleared);
    public GridLength DetailColumn => new(DetailWidth);

    /// <summary>What the table needs: every column at its own width, and the last one at least wide enough for the delete button.</summary>
    public double FixedTotal => Date + BankDate + Counterparty + Memo + (ShowItem ? Item : 0) + (ShowAccount ? Account : 0) + Outflow + Inflow + Cleared + ActionsMinimum;

    partial void OnDateChanged(double value) => Changed(nameof(DateColumn));
    partial void OnBankDateChanged(double value) => Changed(nameof(BankDateColumn));
    partial void OnCounterpartyChanged(double value) => Changed(nameof(CounterpartyColumn));
    partial void OnMemoChanged(double value) => Changed(nameof(MemoColumn));
    partial void OnItemChanged(double value) => Changed(nameof(ItemColumn));
    partial void OnAccountChanged(double value) => Changed(nameof(AccountColumn));
    partial void OnOutflowChanged(double value) => Changed(nameof(OutflowColumn));
    partial void OnInflowChanged(double value) => Changed(nameof(InflowColumn));
    partial void OnClearedChanged(double value) => Changed(nameof(ClearedColumn));
    partial void OnDetailWidthChanged(double value) => OnPropertyChanged(nameof(DetailColumn));
    partial void OnShowItemChanged(bool value) => Changed(nameof(ItemColumn));
    partial void OnShowAccountChanged(bool value) => Changed(nameof(AccountColumn));

    private void Changed(string column)
    {
        OnPropertyChanged(column);
        OnPropertyChanged(nameof(FixedTotal));
    }

    /// <summary>The narrowest each column may be made, so it can never be dragged out of sight.</summary>
    public static double MinimumOf(string column) => column switch
    {
        nameof(Date) or nameof(BankDate) => 112,   // wide enough for the date box a row shows while it is edited
        nameof(Counterparty) => 120,
        nameof(Memo) => 90,
        nameof(Item) => 90,
        nameof(Account) => 80,
        nameof(Outflow) or nameof(Inflow) => 76,
        nameof(Cleared) => 56,
        nameof(DetailWidth) => 300,
        _ => 40,
    };

    public double Get(string column) => column switch
    {
        nameof(Date) => Date, nameof(BankDate) => BankDate, nameof(Counterparty) => Counterparty, nameof(Memo) => Memo, nameof(Item) => Item, nameof(Account) => Account,
        nameof(Outflow) => Outflow, nameof(Inflow) => Inflow, nameof(Cleared) => Cleared, nameof(DetailWidth) => DetailWidth,
        _ => 0,
    };

    public void Set(string column, double width)
    {
        if (!double.IsFinite(width)) return;
        width = Math.Round(Math.Max(MinimumOf(column), Math.Min(width, column == nameof(DetailWidth) ? 800 : 600)));   // whole pixels, so screens with scaling never get fractional layouts
        switch (column)
        {
            case nameof(Date): Date = width; break;
            case nameof(BankDate): BankDate = width; break;
            case nameof(Counterparty): Counterparty = width; break;
            case nameof(Memo): Memo = width; break;
            case nameof(Item): Item = width; break;
            case nameof(Account): Account = width; break;
            case nameof(Outflow): Outflow = width; break;
            case nameof(Inflow): Inflow = width; break;
            case nameof(Cleared): Cleared = width; break;
            case nameof(DetailWidth): DetailWidth = width; break;
        }
    }

    private static readonly string[] Keys =
        [nameof(Date), nameof(BankDate), nameof(Counterparty), nameof(Memo), nameof(Item), nameof(Account), nameof(Outflow), nameof(Inflow), nameof(Cleared), nameof(DetailWidth)];

    // Bumped when the normal sizes change, so sizes saved by an older version (which were only ever its own defaults or tests) are dropped once.
    private const double LayoutVersion = 2;

    public string Serialize() => JsonSerializer.Serialize(Keys.ToDictionary(k => k, Get).Append(new("v", LayoutVersion)).ToDictionary());

    /// <summary>Puts back sizes saved by an earlier run. Anything missing or out of range keeps its normal size.</summary>
    public void Load(string? saved)
    {
        if (string.IsNullOrWhiteSpace(saved)) return;
        try
        {
            var widths = JsonSerializer.Deserialize<Dictionary<string, double>>(saved);
            if (widths is null || !widths.TryGetValue("v", out var version) || version != LayoutVersion) return;
            foreach (var (key, width) in widths.Where(w => Keys.Contains(w.Key)))
                if (double.IsFinite(width)) Set(key, width);
        }
        catch { /* a damaged setting just means the normal sizes */ }
    }
}
