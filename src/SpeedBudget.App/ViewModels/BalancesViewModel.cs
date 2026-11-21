using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SpeedBudget.Core;
using SpeedBudget.Data;

namespace SpeedBudget.App.ViewModels;

/// <summary>
/// The widths of the table columns, shared by the heading and every line so that dragging a column's edge in the heading resizes it all the way down.
/// Index 0-6: Date, Balance, Total changes, Daily changes, Withdrawals, Gains / losses, Comment (which takes the room that is left).
/// Index 7-10: Month, Gains / losses, Withdrawals, Money left. Resizing works as in the transactions table, and the sizes are remembered.
/// </summary>
public sealed class BalanceColumns : ObservableObject
{
    public static BalanceColumns Shared { get; } = new();

    public const int CommentIndex = 6;
    public const int DailyFirst = 0, DailyLast = 6, MonthFirst = 7, MonthLast = 10;

    private readonly double[] _widths = { 128, 130, 130, 130, 130, 130, 200, 130, 170, 170, 190 };
    // The widths shown are nudged (by less than a pixel) so that, at the table's zoom, every column edge falls on a whole screen pixel and
    // all the lines between the columns look the same. The lines themselves are always one screen pixel wide.
    private static double Snap(double width) { var zoom = AppServices.TransactionsZoom; return Math.Abs(zoom - 1) < 0.0001 ? width : Math.Round(width * zoom) / zoom; }
    private static GridLength Of(double width) => new(Snap(width));
    private static double LineWidth => Math.Abs(AppServices.TransactionsZoom - 1) < 0.0001 ? 1 : 1 / AppServices.TransactionsZoom;
    public Thickness RightLine => new(0, 0, LineWidth, 0);
    public Thickness BottomLine => new(0, 0, 0, LineWidth);
    public Thickness AllLine => new(LineWidth);
    public bool NotSelecting => !Selecting;
    public double ActionsOpacity => Selecting ? 0 : 1;   // the pencil and bin go invisible in multi-select mode, but the line keeps its height

    private readonly Microsoft.UI.Dispatching.DispatcherQueue? _queue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
    public BalanceColumns() => AppServices.ZoomChanged += () => _queue?.TryEnqueue(() => OnPropertyChanged(string.Empty));

    public GridLength Date => Of(Sq(0));
    public GridLength Balance => Of(Sq(1) + (CommentIcon ? _extra : 0));
    public GridLength TotalChange => Of(HideTotal ? 0 : Sq(2));
    public GridLength DailyChange => Of(HideDaily ? 0 : Sq(3));
    public GridLength Withdrawal => Of(Sq(4));
    public GridLength Gain => Of(Sq(5));
    public double CommentMin => CommentIcon ? CommentIconWidth : Sq(6);   // the Comment column is as wide as the room left, and never narrower than this
    public GridLength Month => Of(Sq(7));
    public GridLength MonthGains => Of(Sq(8));
    public GridLength MonthWithdrawals => Of(Sq(9));
    public GridLength MonthLeft => Of(Sq(10));

    /// <summary>True in selection mode: a column of tick boxes shows at the left of the table.</summary>
    public bool Selecting { get; private set; }
    public GridLength LeadWidth => Of(Selecting ? 46 : 0);
    public void SetSelecting(bool selecting) { Selecting = selecting; OnPropertyChanged(string.Empty); }

    public double Get(int index) => _widths[index];

    /// <summary>The narrowest each column may be made, so it can never be dragged out of sight.</summary>
    public static double MinimumOf(int index) => index switch { 0 => 112, 6 => 120, _ => 90 };

    public void Set(int index, double width)
    {
        if (!double.IsFinite(width)) return;
        _widths[index] = Math.Round(Math.Clamp(width, MinimumOf(index), 600));   // whole pixels
        OnPropertyChanged(string.Empty);   // every width is read again
    }

    public double[] Snapshot() => (double[])_widths.Clone();

    // ---- Fitting the tables to the room they have ----
    // The daily table gives up what it can do without as the room gets smaller: its columns shrink toward their narrowest, then the Comment
    // becomes an icon (its text is the tooltip, a click opens it), then Total changes goes, then Daily changes. Below that the table is scaled.

    public const double CommentIconWidth = 40, ActionsFull = 170, ActionsSmall = 76;

    public bool CommentIcon { get; private set; }
    public bool HideTotal { get; private set; }
    public bool HideDaily { get; private set; }
    public bool NotCommentIcon => !CommentIcon;
    public bool TotalVisible => !HideTotal;
    public bool DailyVisible => !HideDaily;
    public GridLength ActionsWidth => new(_fitted ? ActionsSmall : ActionsFull);

    private bool _fitted;          // true once the room is too small for every column at its own width
    private double _squeeze = 1;   // daily table: 1 = every column at its own width, 0 = every column at its narrowest
    private double _monthSqueeze = 1;
    private double _extra;

    private static double Narrowest(int index) => index switch { 0 => 112, 6 => 120, 7 => 90, 8 or 9 => 100, 10 => 110, _ => 90 };
    private double Sq(int index) { var user = _widths[index]; var min = Math.Min(user, Narrowest(index)); return min + (user - min) * (index >= MonthFirst ? _monthSqueeze : _squeeze); }

    private double DailyTotal(bool commentIcon, bool hideTotal, bool hideDaily, double squeeze, bool small)
    {
        double W(int i) { var user = _widths[i]; var min = Math.Min(user, Narrowest(i)); return min + (user - min) * squeeze; }
        return (Selecting ? 46 : 0) + W(0) + W(1) + (hideTotal ? 0 : W(2)) + (hideDaily ? 0 : W(3)) + W(4) + W(5) + (commentIcon ? CommentIconWidth : W(6)) + (small ? ActionsSmall : ActionsFull);
    }

    /// <summary>The least width the daily table can be fitted into without being scaled.</summary>
    public double DailyLeast => DailyTotal(true, true, true, 0, true);

    /// <summary>The width of the month by month table at its narrowest.</summary>
    public double MonthLeast => Narrowest(7) + Narrowest(8) + Narrowest(9) + Narrowest(10);

    /// <summary>The width the table in view needs in the state it is in now.</summary>
    public double NeededNow(bool months) => months ? Sq(7) + Sq(8) + Sq(9) + Sq(10) : DailyTotal(CommentIcon, HideTotal, HideDaily, _squeeze, _fitted);

    public void Fit(double room)
    {
        if (!double.IsFinite(room) || room <= 0) return;
        bool icon = false, noTotal = false, noDaily = false;
        var steps = new (bool Icon, bool NoTotal, bool NoDaily)[] { (false, false, false), (true, false, false), (true, true, false), (true, true, true) };
        foreach (var step in steps)
        {
            (icon, noTotal, noDaily) = step;
            if (DailyTotal(icon, noTotal, noDaily, 0, true) <= room) break;
        }
        var narrowest = DailyTotal(icon, noTotal, noDaily, 0, true);
        var widest = DailyTotal(icon, noTotal, noDaily, 1, false);
        var fitted = widest > room;
        var squeeze = fitted ? Math.Clamp((room - narrowest) / Math.Max(1, DailyTotal(icon, noTotal, noDaily, 1, true) - narrowest), 0, 1) : 1;
        var extra = icon ? Math.Max(0, room - DailyTotal(icon, noTotal, noDaily, squeeze, fitted)) : 0;

        var monthWidest = _widths[7] + _widths[8] + _widths[9] + _widths[10];
        var monthSqueeze = monthWidest <= room ? 1 : Math.Clamp((room - MonthLeast) / Math.Max(1, monthWidest - MonthLeast), 0, 1);

        if (CommentIcon == icon && HideTotal == noTotal && HideDaily == noDaily && _fitted == fitted && Math.Abs(_squeeze - squeeze) < 0.001
            && Math.Abs(_extra - extra) < 0.5 && Math.Abs(_monthSqueeze - monthSqueeze) < 0.001) return;
        CommentIcon = icon; HideTotal = noTotal; HideDaily = noDaily; _fitted = fitted; _squeeze = squeeze; _monthSqueeze = monthSqueeze; _extra = Math.Floor(extra);
        OnPropertyChanged(string.Empty);
    }

    public string Serialize() => string.Join(",", _widths.Select(w => w.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    public void Load(string? saved)
    {
        var parts = (saved ?? "").Split(',');
        if (parts.Length != _widths.Length) return;
        for (var i = 0; i < parts.Length; i++)
            if (double.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var w)) Set(i, w);
    }

    /// <summary>
    /// Dragging the right edge of a heading, as in Word and in the transactions table: the divider moves and the columns either side of it
    /// give and take room, so the table keeps its width. Dragging right, this column grows and the ones to its right shrink, nearest first,
    /// each down to its minimum; dragging left, this column and those before it shrink and the one on the right grows. With Shift held, this
    /// column changes on its own. The last column has no neighbour, so the table itself grows or shrinks.
    /// <paramref name="start"/> are the widths when the drag began and <paramref name="dx"/> how far the pointer has moved since.
    /// </summary>
    public void Resize(int first, int last, int column, double dx, bool shift, double[] start)
    {
        var widths = (double[])start.Clone();
        var isLast = column == last;
        if (shift || (isLast && dx >= 0))
        {
            widths[column] = start[column] + dx;
        }
        else if (dx >= 0)
        {
            var room = 0.0;
            for (var j = column + 1; j <= last; j++) room += start[j] - MinimumOf(j);
            var amount = Math.Min(Math.Min(dx, room), 600 - start[column]);
            widths[column] = start[column] + amount;
            for (var j = column + 1; j <= last && amount > 0; j++)
            {
                var take = Math.Min(amount, start[j] - MinimumOf(j));
                widths[j] = start[j] - take;
                amount -= take;
            }
        }
        else
        {
            var room = 0.0;
            for (var j = isLast ? column : first; j <= column; j++) room += start[j] - MinimumOf(j);
            var amount = Math.Min(-dx, room);
            if (!isLast) amount = Math.Min(amount, 600 - start[column + 1]);
            if (!isLast) widths[column + 1] = start[column + 1] + amount;
            for (var j = column; j >= first && amount > 0; j--)
            {
                var take = Math.Min(amount, start[j] - MinimumOf(j));
                widths[j] = start[j] - take;
                amount -= take;
            }
        }
        for (var i = first; i <= last; i++) Set(i, widths[i]);
    }
}

/// <summary>One line of an account kept by its balances. It is changed in place: pressing its pencil turns its cells into boxes.</summary>
public sealed partial class BalanceLine(BalanceRow row) : ObservableObject
{
    public BalanceColumns Columns => BalanceColumns.Shared;

    public Guid Id { get; } = row.Id;
    public DateOnly Date { get; } = row.Date;
    public long BalanceCents { get; } = row.Balance;
    public long WithdrawalCents { get; } = row.Withdrawal;
    public string? Comment { get; } = row.Comment;

    public string DateText { get; } = Format.Date(row.Date);
    public string BalanceText { get; } = Format.Money(row.Balance);
    public string TotalChangeText { get; } = Format.Money(row.TotalChange);
    public Brush TotalChangeBrush { get; } = Format.Signed(row.TotalChange);
    public string DailyChangeText { get; } = Format.Money(row.DailyChange);
    public Brush DailyChangeBrush { get; } = Format.Signed(row.DailyChange);

    /// <summary>What was withdrawn, shown as a minus amount as in the spreadsheet; nothing when there was no withdrawal.</summary>
    public string WithdrawalText { get; } = row.Withdrawal == 0 ? "" : Format.Money(-row.Withdrawal);
    public Brush WithdrawalBrush { get; } = Format.Signed(-row.Withdrawal);
    public string GainText { get; } = Format.Money(row.GainLoss);
    public Brush GainBrush { get; } = Format.Signed(row.GainLoss);
    public string CommentText { get; } = row.Comment ?? "";
    public string CommentTip => CommentText == "" ? "Add a comment" : CommentText;
    public double CommentIconOpacity => CommentText == "" ? 0.35 : 1;

    /// <summary>A line that was just added with Add Entry and is not saved yet: it has no id, and no changes or gains to show.</summary>
    public bool IsNew => Id == Guid.Empty;
    public bool HasFigures => !IsNew;

    /// <summary>Whether the line has a tick box: a new line that is not saved yet has none.</summary>
    public bool ShowTick => !IsNew;

    /// <summary>Ticked lines are deleted together.</summary>
    [ObservableProperty] public partial bool IsTicked { get; set; }
    partial void OnIsTickedChanged(bool value) => TickChanged?.Invoke(this, EventArgs.Empty);
    public static event EventHandler? TickChanged;

    /// <summary>True while the line's own cells are boxes to type in.</summary>
    [ObservableProperty] public partial bool IsEditing { get; set; }
    public bool IsViewing => !IsEditing;
    partial void OnIsEditingChanged(bool value) => OnPropertyChanged(nameof(IsViewing));

    [ObservableProperty] public partial DateTimeOffset? EditDate { get; set; }
    [ObservableProperty] public partial double EditBalance { get; set; } = double.NaN;
    [ObservableProperty] public partial double EditWithdrawal { get; set; } = double.NaN;
    [ObservableProperty] public partial string EditComment { get; set; } = "";

    /// <summary>Everything shown for the line, so a reload can tell which lines really changed.</summary>
    public string Signature => string.Join("|", Id, DateText, BalanceText, TotalChangeText, DailyChangeText, WithdrawalText, GainText, CommentText);
}

/// <summary>One month of an account kept by its balances.</summary>
public sealed class BalanceMonthLine(MonthGains month)
{
    public BalanceColumns Columns => BalanceColumns.Shared;
    public string Month { get; } = month.Month;
    public string GainsText { get; } = Format.Money(month.GainsLosses);
    public Brush GainsBrush { get; } = Format.Signed(month.GainsLosses);
    public string WithdrawalsText { get; } = Format.Money(month.Withdrawals);
    public Brush WithdrawalsBrush { get; } = Format.Signed(month.Withdrawals);
    public string LeftText { get; } = Format.Money(month.LeftToReinvest);
    public Brush LeftBrush { get; } = Format.Signed(month.LeftToReinvest);
}

/// <summary>
/// An account kept by its balances: the user types in the balance on a date (usually every day), what was withdrawn, and a comment, and
/// the lines show the daily changes, the changes to date and the gains and losses, with the totals and a summary by month.
/// </summary>
public sealed partial class BalancesViewModel : ObservableObject
{
    private readonly BudgetStore _store = AppServices.Store;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public Guid AccountId { get; private set; }

    public ObservableCollection<BalanceLine> Lines { get; } = new();
    public ObservableCollection<BalanceMonthLine> Months { get; } = new();

    [ObservableProperty] public partial string Title { get; set; } = "";
    [ObservableProperty] public partial string OriginalText { get; set; } = "";
    [ObservableProperty] public partial string BalanceText { get; set; } = "";
    [ObservableProperty] public partial string ChangeText { get; set; } = "";
    [ObservableProperty] public partial Brush? ChangeBrush { get; set; }
    [ObservableProperty] public partial string WithdrawalsText { get; set; } = "";
    [ObservableProperty] public partial string GainsText { get; set; } = "";
    [ObservableProperty] public partial Brush? GainsBrush { get; set; }

    /// <summary>Which table shows: the daily lines, or the summary by month.</summary>
    [ObservableProperty] public partial bool ShowMonths { get; set; }

    private static int s_sortIndex;   // newest first, unless the user chooses otherwise (remembered while the app is open)

    /// <summary>0 = newest first, 1 = oldest first.</summary>
    [ObservableProperty] public partial int SortIndex { get; set; } = s_sortIndex;
    /// <summary>The sort button's tip: which way the table runs now, and what pressing it does.</summary>
    public string SortTip => SortIndex == 0 ? "Showing newest first. Click to show oldest first" : "Showing oldest first. Click to show newest first";

    partial void OnSortIndexChanged(int value)
    {
        s_sortIndex = value;
        OnPropertyChanged(nameof(SortTip));
        if (AccountId != Guid.Empty) _ = ResortAsync();
    }


    private static int s_range;   // 0 every transaction, 1 the last week, 2 the last month, 3 the last quarter (remembered while the app is open)

    /// <summary>How far back the table goes.</summary>
    [ObservableProperty] public partial int RangeIndex { get; set; } = s_range;

    partial void OnRangeIndexChanged(int value)
    {
        s_range = value;
        OnPropertyChanged(nameof(RangeTip));
        OnPropertyChanged(nameof(RangeBrush));
        if (AccountId != Guid.Empty) _ = ResortAsync();
    }

    /// <summary>The eye's tip: how far back the table goes now.</summary>
    public string RangeTip => RangeIndex switch
    {
        1 => "Showing the last week",
        2 => "Showing the last month",
        3 => "Showing the last quarter",
        _ => "Showing every transaction. Click to choose how far back to show",
    };

    /// <summary>The eye is coloured while the table is cut to a recent stretch.</summary>
    public Brush RangeBrush => Format.Theme(RangeIndex != 0 ? "AccentTextFillColorPrimaryBrush" : "TextFillColorPrimaryBrush");

    /// <summary>The first date shown, or null when everything is.</summary>
    private DateOnly? RangeStart()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        return RangeIndex switch { 1 => today.AddDays(-7), 2 => today.AddMonths(-1), 3 => today.AddMonths(-3), _ => null };
    }
    /// <summary>Raised once the lines are in their new order, so the page can show the end of the table that is now the latest.</summary>
    public event Action? SortReloaded;

    private async Task ResortAsync()
    {
        try { await LoadAsync(AccountId); }
        finally { SortReloaded?.Invoke(); }
    }
    public bool ShowDaily => !ShowMonths;
    public bool ShowDailyEmpty => ShowDaily && Lines.Count == 0;
    partial void OnShowMonthsChanged(bool value) { OnPropertyChanged(nameof(ShowDaily)); OnPropertyChanged(nameof(ShowDailyEmpty)); }

    /// <summary>Selection mode: tick boxes show beside the lines, so several can be ticked and deleted together. Entered from the right-click menu's Select.</summary>
    [ObservableProperty] public partial bool IsSelecting { get; set; }
    partial void OnIsSelectingChanged(bool value) => BalanceColumns.Shared.SetSelecting(value);

    public BalancesViewModel() => Lines.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ShowDailyEmpty));

    [ObservableProperty] public partial string Message { get; set; } = "";
    [ObservableProperty] public partial InfoBarSeverity MessageSeverity { get; set; } = InfoBarSeverity.Warning;
    public bool HasMessage => Message != "";
    partial void OnMessageChanged(string value) => OnPropertyChanged(nameof(HasMessage));

    private BalanceLine? _editing;

    private void Warn(string text) { MessageSeverity = InfoBarSeverity.Warning; Message = text; }
    /// <summary>A confirmation: it goes away by itself after ten seconds (a warning stays until something else is done).</summary>
    public void Tell(string text)
    {
        MessageSeverity = InfoBarSeverity.Success;
        Message = text;
        _messageTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _messageTimer.Tick -= ClearConfirmation;
        _messageTimer.Tick += ClearConfirmation;
        _messageTimer.Stop();
        _messageTimer.Start();
    }

    private DispatcherTimer? _messageTimer;

    private void ClearConfirmation(object? sender, object e)
    {
        _messageTimer?.Stop();
        if (MessageSeverity == InfoBarSeverity.Success) Message = "";
    }

    public async Task LoadAsync(Guid accountId)
    {
        await _gate.WaitAsync();
        try
        {
            AccountId = accountId;
            Title = (await _store.GetAccountsAsync()).FirstOrDefault(a => a.Id == accountId)?.Name ?? "";
            var report = await _store.GetBalanceReportAsync(accountId);

            // The figures above the table show first; the lines (the part that can be long) follow.
            _latestDate = report.Rows.LastOrDefault()?.Date;
            OriginalText = report.Rows.FirstOrDefault() is { } original ? Format.Money(original.Balance) : "";
            BalanceText = report.Rows.LastOrDefault() is { } latest ? Format.Money(latest.Balance) : "";
            ChangeText = Format.Money(report.Totals.DailyChanges);
            ChangeBrush = Format.Signed(report.Totals.DailyChanges);
            WithdrawalsText = Format.Money(-report.Totals.Withdrawals);
            GainsText = Format.Money(report.Totals.GainsLosses);
            GainsBrush = Format.Signed(report.Totals.GainsLosses);
            await Task.Delay(1);   // a moment for them to be drawn

            // Newest first, so the latest balance is at the top beside the boxes for the next one.
            var start = RangeStart();   // the eye: only a recent stretch, when chosen
            var shown = start is null ? report.Rows : report.Rows.Where(r => r.Date >= start).ToList();
            var shownMonths = shown.Select(r => r.Month).ToHashSet();
            var lines = shown.Select(r => new BalanceLine(r)).ToList();
            if (SortIndex == 0) lines.Reverse();   // newest first: the latest balance is at the top, beside Add Entry
            ApplyLines(lines);
            Months.Clear();
            // The months follow the sort order too: the latest first, or the latest last.
            var monthOrder = SortIndex == 0 ? report.Months.Reverse() : report.Months;
            foreach (var month in monthOrder.Where(m => start is null || shownMonths.Contains(m.Month))) Months.Add(new BalanceMonthLine(month));
        }
        catch (Exception ex) { Warn($"Could not load the balances: {ex.Message}"); }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Shows the lines. When the same lines are in the same order only the ones that changed are replaced, so the table stays where it is
    /// scrolled to (changing a line in place must not throw the user back to the top).
    /// </summary>
    private void ApplyLines(List<BalanceLine> lines)
    {
        if (Lines.Select(l => l.Id).SequenceEqual(lines.Select(l => l.Id)))
        {
            for (var i = 0; i < lines.Count; i++)
                if (Lines[i].Signature != lines[i].Signature || Lines[i].IsEditing) Lines[i] = lines[i];
            return;
        }
        Lines.Clear();
        foreach (var line in lines) Lines.Add(line);
    }

    private DateOnly? _latestDate;

    /// <summary>
    /// Add Entry: puts a new line at the latest end of the list with its cells already open as boxes, dated the day after the latest balance (or today).
    /// It is only saved when its tick (or Enter) is pressed. Pressing Add Entry again goes back to the line already open.
    /// </summary>
    public BalanceLine? BeginAdd()
    {
        if (Lines.FirstOrDefault(l => l.IsNew) is { } open) return open;
        if (_editing is { } other) other.IsEditing = false;

        var today = DateOnly.FromDateTime(DateTime.Today);
        var next = _latestDate?.AddDays(1) ?? today;
        var date = next > today ? today : next;
        var line = new BalanceLine(new BalanceRow(Guid.Empty, date, 0, 0, 0, 0, 0, null, BalanceTracker.MonthOf(date)))
        {
            EditDate = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue)),
            IsEditing = true,
        };
        _editing = line;
        Message = "";
        if (SortIndex == 0) Lines.Insert(0, line); else Lines.Add(line);   // the latest line: at the top when newest is first, at the bottom when oldest is first
        return line;
    }

    /// <summary>Turns a line's cells into boxes. Any other line being changed goes back to normal first.</summary>
    public void BeginChange(BalanceLine line)
    {
        if (_editing is { } other && !ReferenceEquals(other, line)) CancelChange(other);
        _editing = line;
        line.EditDate = new DateTimeOffset(line.Date.ToDateTime(TimeOnly.MinValue));
        line.EditBalance = (double)AppServices.ToRands(line.BalanceCents);
        line.EditWithdrawal = line.WithdrawalCents == 0 ? double.NaN : (double)AppServices.ToRands(line.WithdrawalCents);
        line.EditComment = line.Comment ?? "";
        line.IsEditing = true;
        Message = "";
    }

    public void CancelChange(BalanceLine line)
    {
        if (line.IsNew) Lines.Remove(line);   // a line that was never saved just goes away
        line.IsEditing = false;
        if (ReferenceEquals(_editing, line)) _editing = null;
    }

    /// <summary>Saves the boxes of the line being changed, and puts its cells back to normal.</summary>
    public async Task SaveChangeAsync(BalanceLine line)
    {
        if (line.EditDate is not { } date) { Warn("Choose the date of the balance."); return; }
        if (double.IsNaN(line.EditBalance)) { Warn("Type in the balance on that date."); return; }
        try
        {
            var day = DateOnly.FromDateTime(date.DateTime);
            if (line.IsNew)
            {
                await _store.AddBalanceEntryAsync(AccountId, day, AppServices.ToCents(line.EditBalance), AppServices.ToCents(line.EditWithdrawal), line.EditComment);
                Lines.Remove(line);
            }
            else await _store.UpdateBalanceEntryAsync(line.Id, day, AppServices.ToCents(line.EditBalance), AppServices.ToCents(line.EditWithdrawal), line.EditComment);
            Message = "";
            line.IsEditing = false;
            if (ReferenceEquals(_editing, line)) _editing = null;
            await LoadAsync(AccountId);
            AppServices.RaiseBalancesChanged();
        }
        catch (Exception ex) { Warn($"Could not save: {ex.Message}"); }
    }

    public async Task DeleteAsync(BalanceLine line)
    {
        try
        {
            await _store.DeleteBalanceEntryAsync(line.Id);
            if (ReferenceEquals(_editing, line)) _editing = null;
            await LoadAsync(AccountId);
            AppServices.RaiseBalancesChanged();
        }
        catch (Exception ex) { Warn($"Could not delete: {ex.Message}"); }
    }

    /// <summary>Deletes all the selected lines in one go.</summary>
    public async Task DeleteManyAsync(IReadOnlyList<BalanceLine> lines)
    {
        var saved = lines.Where(l => !l.IsNew).ToList();
        try
        {
            if (saved.Count > 0) await _store.DeleteBalanceEntriesAsync(saved.Select(l => l.Id));
            if (_editing is { } e && lines.Contains(e)) _editing = null;
            foreach (var unsaved in lines.Where(l => l.IsNew)) Lines.Remove(unsaved);
            await LoadAsync(AccountId);
            AppServices.RaiseBalancesChanged();
            Tell($"Deleted {saved.Count} balance{(saved.Count == 1 ? "" : "s")}.");
        }
        catch (Exception ex) { Warn($"Could not delete: {ex.Message}"); }
    }

    public Task ExportAsync(string path) => _store.ExportBalancesAsync(path, AccountId);

    // ---- Import from Excel ----

    /// <summary>What importing the lines would do: how many dates are new and how many the account already has.</summary>
    public Task<BalanceImportResult> PreviewImportAsync(IEnumerable<ImportRow> rows) => _store.ImportBalancesAsync(AccountId, rows, apply: false);

    public async Task ImportAsync(IEnumerable<ImportRow> rows)
    {
        try
        {
            var result = await _store.ImportBalancesAsync(AccountId, rows);
            await LoadAsync(AccountId);
            AppServices.RaiseBalancesChanged();
            Tell($"Imported: {result.Added} added, {result.Updated} updated.");
        }
        catch (Exception ex) { Warn($"The import failed: {ex.Message}"); }
    }
}
