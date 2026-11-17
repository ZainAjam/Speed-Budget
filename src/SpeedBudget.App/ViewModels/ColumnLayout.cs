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
    [ObservableProperty] public partial double Cleared { get; set; } = 96;

    /// <summary>The width of the edit panel on the right, including its left padding.</summary>
    [ObservableProperty] public partial double DetailWidth { get; set; } = 344;

    /// <summary>A tracked account has no budget item column, and a single account's page has no account column.</summary>
    [ObservableProperty] public partial bool ShowItem { get; set; } = true;
    [ObservableProperty] public partial bool ShowAccount { get; set; } = true;

    // The widths shown are nudged (by less than a pixel) so that, at the table's zoom, every column edge falls on a whole screen pixel and
    // all the lines between the columns look the same.
    private static double Snap(double width)
    {
        var zoom = AppServices.TransactionsZoom;
        return Math.Abs(zoom - 1) < 0.0001 ? width : Math.Round(width * zoom) / zoom;
    }

    /// <summary>True while the table is in selection mode: a column of tick boxes shows at the left of the Date column.</summary>
    [ObservableProperty] public partial bool Selecting { get; set; }
    public const double SelectWidth = 46;
    private double SelectExtra => Selecting ? SelectWidth : 0;
    public GridLength SelectColumn => new(SelectExtra);
    public bool NotSelecting => !Selecting;
    partial void OnSelectingChanged(bool value) { Changed(nameof(DateColumn)); OnPropertyChanged(nameof(SelectColumn)); OnPropertyChanged(nameof(NotSelecting)); }

    public GridLength DateColumn => new(Snap(Squeezed(nameof(Date))) + SelectExtra);
    public GridLength BankDateColumn => new(HideBankDate ? 0 : Snap(Squeezed(nameof(BankDate))));
    public GridLength CounterpartyColumn => new(Snap(Squeezed(nameof(Counterparty))) + (MemoIcon ? _extra : 0));
    public GridLength MemoColumn => new(MemoMin);
    public GridLength ItemColumn => new(ShowItem ? Snap(Squeezed(nameof(Item))) : 0);
    public GridLength AccountColumn => new(AccountVisible ? Snap(Squeezed(nameof(Account))) : 0);
    public GridLength OutflowColumn => new(Snap(Squeezed(nameof(Outflow))));
    public GridLength InflowColumn => new(Snap(Squeezed(nameof(Inflow))));
    public GridLength ClearedColumn => new(ClearedTick ? ClearedTickWidth : Snap(Squeezed(nameof(Cleared))));

    // The lines of the table are always one screen pixel wide, however far the table is zoomed.
    private static double LineWidth => Math.Abs(AppServices.TransactionsZoom - 1) < 0.0001 ? 1 : 1 / AppServices.TransactionsZoom;
    public Thickness CellLine => new(0, 0, LineWidth, LineWidth);
    public Thickness HeadingLine => new(0, 0, LineWidth, 0);
    public Thickness ActionLine => new(0, 0, 0, LineWidth);

    private readonly Microsoft.UI.Dispatching.DispatcherQueue? _queue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

    public ColumnLayout() =>
        AppServices.ZoomChanged += () => _queue?.TryEnqueue(() =>
        {
            foreach (var name in new[] { nameof(DateColumn), nameof(BankDateColumn), nameof(CounterpartyColumn), nameof(MemoColumn), nameof(ItemColumn),
                                         nameof(AccountColumn), nameof(OutflowColumn), nameof(InflowColumn), nameof(ClearedColumn), nameof(MemoMin),
                                         nameof(CellLine), nameof(HeadingLine), nameof(ActionLine) })
                OnPropertyChanged(name);
        });
    public GridLength DetailColumn => new(DetailWidth);

    // ---- Fitting the table to the room it has ----
    // Instead of running off the side, the table gives up what it can do without as the room gets smaller: first its columns shrink
    // toward their narrowest, then, one after the other, the Bank date column goes, the Memo becomes a small icon (its text is in a tooltip,
    // and a click opens it to edit), the Account column goes and the Cleared column shrinks to a tick. Only then is the table scaled down.

    public const double MemoIconWidth = 40, ClearedTickWidth = 44;

    /// <summary>The room the table has, in the table's own units (0 until it is known).</summary>
    public double Room { get; private set; }

    public bool HideBankDate { get; private set; }
    public bool MemoIcon { get; private set; }
    public bool HideAccount { get; private set; }
    public bool ClearedTick { get; private set; }
    public bool NotMemoIcon => !MemoIcon;
    public bool BankDateVisible => !HideBankDate;
    public bool AccountVisible => ShowAccount && !HideAccount;
    public string ClearedTitle => ClearedTick ? "✓" : "Cleared";

    private double _squeeze = 1;   // 1: every column at its own width; 0: every column at its narrowest
    private double _extra;         // room left over when the Memo is an icon, which goes to the Counterparty

    private static double Narrowest(string column) => column switch
    {
        nameof(Date) or nameof(BankDate) => 112,
        nameof(Counterparty) => 100,
        nameof(Memo) => 90,
        nameof(Item) or nameof(Account) => 80,
        nameof(Outflow) or nameof(Inflow) => 84,
        nameof(Cleared) => 90,
        _ => 40,
    };

    private double Squeezed(string column) { var user = Get(column); var min = Math.Min(user, Narrowest(column)); return min + (user - min) * _squeeze; }

    private double Total(bool bankDate, bool memoIcon, bool account, bool clearedTick, double squeeze)
    {
        double W(string c) { var user = Get(c); var min = Math.Min(user, Narrowest(c)); return min + (user - min) * squeeze; }
        return W(nameof(Date)) + SelectExtra + (bankDate ? W(nameof(BankDate)) : 0) + W(nameof(Counterparty)) + (memoIcon ? MemoIconWidth : W(nameof(Memo)))
            + (ShowItem ? W(nameof(Item)) : 0) + (ShowAccount && account ? W(nameof(Account)) : 0) + W(nameof(Outflow)) + W(nameof(Inflow))
            + (clearedTick ? ClearedTickWidth : W(nameof(Cleared))) + ActionsMinimum;
    }

    /// <summary>The width the table needs, in the state it is in now.</summary>
    public double NeededNow => Total(!HideBankDate, MemoIcon, !HideAccount, ClearedTick, _squeeze);

    /// <summary>The least width the table can be fitted into without being scaled: everything that can be given up has been.</summary>
    public double NeededLeast => Total(false, true, false, true, 0);

    /// <summary>Works out what shows in the room the table has, and how wide each column is.</summary>
    public void Fit(double room)
    {
        if (!double.IsFinite(room) || room <= 0) return;
        Room = room;
        bool bank = true, memo = false, account = true, tick = false;
        var steps = new (bool Bank, bool Memo, bool Account, bool Tick)[]
        {
            (true, false, true, false), (false, false, true, false), (false, true, true, false), (false, true, false, false), (false, true, false, true),
        };
        foreach (var step in steps)
        {
            (bank, memo, account, tick) = step;
            if (Total(bank, memo, account, tick, 0) <= room) break;
        }
        var narrowest = Total(bank, memo, account, tick, 0);
        var widest = Total(bank, memo, account, tick, 1);
        var squeeze = widest <= narrowest ? 1 : Math.Clamp((room - narrowest) / (widest - narrowest), 0, 1);
        var extra = memo ? Math.Max(0, room - (narrowest + (widest - narrowest) * squeeze)) : 0;

        if (HideBankDate == !bank && MemoIcon == memo && HideAccount == !account && ClearedTick == tick && Math.Abs(_squeeze - squeeze) < 0.001 && Math.Abs(_extra - extra) < 0.5) return;
        HideBankDate = !bank; MemoIcon = memo; HideAccount = !account; ClearedTick = tick; _squeeze = squeeze; _extra = Math.Floor(extra);
        foreach (var name in new[] { nameof(HideBankDate), nameof(MemoIcon), nameof(HideAccount), nameof(ClearedTick), nameof(NotMemoIcon), nameof(BankDateVisible), nameof(AccountVisible), nameof(ClearedTitle),
                                     nameof(DateColumn), nameof(BankDateColumn), nameof(CounterpartyColumn), nameof(MemoColumn), nameof(MemoMin), nameof(ItemColumn), nameof(AccountColumn),
                                     nameof(OutflowColumn), nameof(InflowColumn), nameof(ClearedColumn), nameof(NeededNow), nameof(NeededLeast) })
            OnPropertyChanged(name);
    }

    /// <summary>The Memo column's least width: the star column takes whatever room is left above it.</summary>
    public double MemoMin => MemoIcon ? MemoIconWidth : Snap(Squeezed(nameof(Memo)));

    /// <summary>What the table needs: every column at its own width, and the last one at least wide enough for the delete button.</summary>
    public double FixedTotal => Date + SelectExtra + BankDate + Counterparty + Memo + (ShowItem ? Item : 0) + (ShowAccount ? Account : 0) + Outflow + Inflow + Cleared + ActionsMinimum;

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
    partial void OnShowAccountChanged(bool value) { Changed(nameof(AccountColumn)); OnPropertyChanged(nameof(AccountVisible)); }

    private void Changed(string column)
    {
        OnPropertyChanged(column);
        OnPropertyChanged(nameof(FixedTotal));
    }

    /// <summary>The narrowest each column may be made, so it can never be dragged out of sight.</summary>
    public static double MinimumOf(string column) => column switch
    {
        nameof(Date) => 112,   // wide enough for the date box a row shows while it is edited
        nameof(BankDate) => 112,   // wide enough for the date box a row shows while it is edited
        nameof(Counterparty) => 120,
        nameof(Memo) => 90,
        nameof(Item) => 90,
        nameof(Account) => 80,
        nameof(Outflow) or nameof(Inflow) => 92,
        nameof(Cleared) => 90,
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
