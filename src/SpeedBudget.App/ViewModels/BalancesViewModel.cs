using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using SpeedBudget.Core;

namespace SpeedBudget.App.ViewModels;

/// <summary>One line of an account kept by its balances.</summary>
public sealed class BalanceLine(BalanceRow row)
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
    private readonly SpeedBudget.Data.BudgetStore _store = AppServices.Store;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public Guid AccountId { get; private set; }

    public ObservableCollection<BalanceLine> Lines { get; } = new();
    public ObservableCollection<BalanceMonthLine> Months { get; } = new();

    [ObservableProperty] public partial string Title { get; set; } = "";
    [ObservableProperty] public partial string BalanceText { get; set; } = "";
    [ObservableProperty] public partial string SinceText { get; set; } = "";
    [ObservableProperty] public partial string ChangeText { get; set; } = "";
    [ObservableProperty] public partial Brush? ChangeBrush { get; set; }
    [ObservableProperty] public partial string WithdrawalsText { get; set; } = "";
    [ObservableProperty] public partial string GainsText { get; set; } = "";
    [ObservableProperty] public partial Brush? GainsBrush { get; set; }
    [ObservableProperty] public partial bool HasLines { get; set; }

    /// <summary>Which table shows: the daily lines, or the summary by month.</summary>
    [ObservableProperty] public partial bool ShowMonths { get; set; }
    public bool ShowDaily => !ShowMonths;
    public bool ShowDailyEmpty => ShowDaily && !HasLines;
    partial void OnShowMonthsChanged(bool value) { OnPropertyChanged(nameof(ShowDaily)); OnPropertyChanged(nameof(ShowDailyEmpty)); }
    partial void OnHasLinesChanged(bool value) => OnPropertyChanged(nameof(ShowDailyEmpty));

    // The boxes for a new balance, or for the one being changed.
    [ObservableProperty] public partial DateTimeOffset? FormDate { get; set; } = DateTimeOffset.Now;
    [ObservableProperty] public partial double FormBalance { get; set; } = double.NaN;
    [ObservableProperty] public partial double FormWithdrawal { get; set; } = double.NaN;
    [ObservableProperty] public partial string FormComment { get; set; } = "";
    [ObservableProperty] public partial string Message { get; set; } = "";
    [ObservableProperty] public partial string SaveText { get; set; } = "Add";
    [ObservableProperty] public partial bool IsChanging { get; set; }
    public bool HasMessage => Message != "";
    partial void OnMessageChanged(string value) => OnPropertyChanged(nameof(HasMessage));

    private Guid? _changing;

    public async Task LoadAsync(Guid accountId)
    {
        await _gate.WaitAsync();
        try
        {
            AccountId = accountId;
            Title = (await _store.GetAccountsAsync()).FirstOrDefault(a => a.Id == accountId)?.Name ?? "";
            var report = await _store.GetBalanceReportAsync(accountId);

            // Newest first, so the latest balance is at the top beside the boxes for the next one.
            var ordered = report.Rows.Select(r => new BalanceLine(r)).Reverse().ToList();
            Lines.Clear();
            foreach (var line in ordered) Lines.Add(line);
            Months.Clear();
            foreach (var month in report.Months.Reverse()) Months.Add(new BalanceMonthLine(month));

            HasLines = report.Rows.Count > 0;
            var latest = report.Rows.LastOrDefault();
            BalanceText = latest is null ? "" : Format.Money(latest.Balance);
            SinceText = report.Rows.Count == 0 ? "" : $"Totals since {Format.Date(report.Rows[0].Date)}";
            ChangeText = Format.Money(report.Totals.DailyChanges);
            ChangeBrush = Format.Signed(report.Totals.DailyChanges);
            WithdrawalsText = Format.Money(-report.Totals.Withdrawals);
            GainsText = Format.Money(report.Totals.GainsLosses);
            GainsBrush = Format.Signed(report.Totals.GainsLosses);

            // The next balance is usually for the day after the last one (or today).
            if (_changing is null && latest is not null) FormDate = new DateTimeOffset(latest.Date.AddDays(1).ToDateTime(TimeOnly.MinValue));
        }
        catch (Exception ex) { Message = $"Could not load the balances: {ex.Message}"; }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync()
    {
        if (FormDate is not { } date) { Message = "Choose the date of the balance."; return; }
        if (double.IsNaN(FormBalance)) { Message = "Type in the balance on that date."; return; }
        var balance = AppServices.ToCents(FormBalance);
        var withdrawal = Math.Abs(AppServices.ToCents(FormWithdrawal));
        try
        {
            if (_changing is { } id) await _store.UpdateBalanceEntryAsync(id, DateOnly.FromDateTime(date.DateTime), balance, withdrawal, FormComment);
            else await _store.AddBalanceEntryAsync(AccountId, DateOnly.FromDateTime(date.DateTime), balance, withdrawal, FormComment);
            Message = "";
            ClearForm();
            await LoadAsync(AccountId);
            AppServices.RaiseAccountsChanged();   // the balance beside the account's name changes
        }
        catch (Exception ex) { Message = $"Could not save: {ex.Message}"; }
    }

    public void BeginChange(BalanceLine line)
    {
        _changing = line.Id;
        IsChanging = true;
        SaveText = "Save";
        FormDate = new DateTimeOffset(line.Date.ToDateTime(TimeOnly.MinValue));
        FormBalance = (double)AppServices.ToRands(line.BalanceCents);
        FormWithdrawal = line.WithdrawalCents == 0 ? double.NaN : (double)AppServices.ToRands(line.WithdrawalCents);
        FormComment = line.Comment ?? "";
        Message = "";
    }

    public async Task CancelChangeAsync()
    {
        ClearForm();
        await LoadAsync(AccountId);
    }

    private void ClearForm()
    {
        _changing = null;
        IsChanging = false;
        SaveText = "Add";
        FormBalance = double.NaN;
        FormWithdrawal = double.NaN;
        FormComment = "";
    }

    public async Task DeleteAsync(BalanceLine line)
    {
        try
        {
            await _store.DeleteBalanceEntryAsync(line.Id);
            if (_changing == line.Id) ClearForm();
            await LoadAsync(AccountId);
            AppServices.RaiseAccountsChanged();
        }
        catch (Exception ex) { Message = $"Could not delete: {ex.Message}"; }
    }
}
