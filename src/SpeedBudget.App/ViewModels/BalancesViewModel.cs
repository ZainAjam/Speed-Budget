using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SpeedBudget.Core;
using SpeedBudget.Data;

namespace SpeedBudget.App.ViewModels;

/// <summary>One line of an account kept by its balances. It is changed in place: pressing its pencil turns its cells into boxes.</summary>
public sealed partial class BalanceLine(BalanceRow row) : ObservableObject
{
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

    /// <summary>A line that was just added with Add Entry and is not saved yet: it has no id, and no changes or gains to show.</summary>
    public bool IsNew => Id == Guid.Empty;
    public bool HasFigures => !IsNew;

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
    [ObservableProperty] public partial string BalanceText { get; set; } = "";
    [ObservableProperty] public partial string ChangeText { get; set; } = "";
    [ObservableProperty] public partial Brush? ChangeBrush { get; set; }
    [ObservableProperty] public partial string WithdrawalsText { get; set; } = "";
    [ObservableProperty] public partial string GainsText { get; set; } = "";
    [ObservableProperty] public partial Brush? GainsBrush { get; set; }

    /// <summary>Which table shows: the daily lines, or the summary by month.</summary>
    [ObservableProperty] public partial bool ShowMonths { get; set; }
    public bool ShowDaily => !ShowMonths;
    public bool ShowDailyEmpty => ShowDaily && Lines.Count == 0;
    partial void OnShowMonthsChanged(bool value) { OnPropertyChanged(nameof(ShowDaily)); OnPropertyChanged(nameof(ShowDailyEmpty)); }

    public BalancesViewModel() => Lines.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ShowDailyEmpty));

    [ObservableProperty] public partial string Message { get; set; } = "";
    [ObservableProperty] public partial InfoBarSeverity MessageSeverity { get; set; } = InfoBarSeverity.Warning;
    public bool HasMessage => Message != "";
    partial void OnMessageChanged(string value) => OnPropertyChanged(nameof(HasMessage));

    private BalanceLine? _editing;

    private void Warn(string text) { MessageSeverity = InfoBarSeverity.Warning; Message = text; }
    public void Tell(string text) { MessageSeverity = InfoBarSeverity.Success; Message = text; }

    public async Task LoadAsync(Guid accountId)
    {
        await _gate.WaitAsync();
        try
        {
            AccountId = accountId;
            Title = (await _store.GetAccountsAsync()).FirstOrDefault(a => a.Id == accountId)?.Name ?? "";
            var report = await _store.GetBalanceReportAsync(accountId);

            // Newest first, so the latest balance is at the top beside the boxes for the next one.
            ApplyLines(report.Rows.Select(r => new BalanceLine(r)).Reverse().ToList());
            Months.Clear();
            foreach (var month in report.Months.Reverse()) Months.Add(new BalanceMonthLine(month));

            _latestDate = report.Rows.LastOrDefault()?.Date;
            BalanceText = report.Rows.LastOrDefault() is { } latest ? Format.Money(latest.Balance) : "";
            ChangeText = Format.Money(report.Totals.DailyChanges);
            ChangeBrush = Format.Signed(report.Totals.DailyChanges);
            WithdrawalsText = Format.Money(-report.Totals.Withdrawals);
            GainsText = Format.Money(report.Totals.GainsLosses);
            GainsBrush = Format.Signed(report.Totals.GainsLosses);
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
    /// Add Entry: puts a new line at the top of the list with its cells already open as boxes, dated the day after the latest balance (or today).
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
        Lines.Insert(0, line);
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
