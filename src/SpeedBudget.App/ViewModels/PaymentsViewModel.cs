using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SpeedBudget.Core;
using SpeedBudget.Data;

namespace SpeedBudget.App.ViewModels;

/// <summary>An item to pay this month. Paid is what the month's transactions on it add up to.</summary>
public sealed partial class PaymentRow(Guid itemId, string name, string? accountRef) : ObservableObject
{
    public Guid ItemId { get; } = itemId;
    public string Name { get; } = name;
    public string AccountRef { get; } = accountRef ?? "";
    public long ToPayCents { get; private set; }

    [ObservableProperty] public partial string BudgetedText { get; set; } = "";
    [ObservableProperty] public partial string PaidText { get; set; } = "";
    [ObservableProperty] public partial string StatusText { get; set; } = "";
    [ObservableProperty] public partial bool IsPaid { get; set; }
    [ObservableProperty] public partial bool IsVisible { get; set; } = true;

    public void Apply(ItemMonth m)
    {
        var paid = -m.Activity;
        ToPayCents = Math.Max(0, m.Budgeted - paid);
        BudgetedText = Format.Money(m.Budgeted);
        PaidText = Format.Money(paid);
        IsPaid = m.Budgeted > 0 && paid >= m.Budgeted;
        StatusText = IsPaid ? "Paid" : ToPayCents > 0 ? $"To pay {Format.Money(ToPayCents)}" : "";
        IsVisible = m.Budgeted != 0 || paid != 0;
    }
}

public sealed partial class PaymentSection(string heading) : ObservableObject
{
    public string Heading { get; } = heading;
    public ObservableCollection<PaymentRow> Rows { get; } = new();

    /// <summary>A heading with nothing to pay under it is hidden.</summary>
    [ObservableProperty] public partial bool IsVisible { get; set; } = true;
}

public sealed partial class PaymentsViewModel : ObservableObject
{
    private readonly BudgetStore _store = AppServices.Store;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<MonthSummary> _months = Array.Empty<MonthSummary>();
    private readonly Dictionary<Guid, PaymentRow> _rows = new();
    private List<AccountEntity> _budgetAccounts = new();

    public ObservableCollection<PaymentSection> Sections { get; } = new();
    public IReadOnlyList<AccountOption> BudgetAccounts { get; private set; } = Array.Empty<AccountOption>();

    [ObservableProperty] public partial int MonthIndex { get; set; }
    [ObservableProperty] public partial string MonthLabel { get; set; } = "";
    [ObservableProperty] public partial bool CanGoBack { get; set; }
    [ObservableProperty] public partial bool CanGoForward { get; set; }
    [ObservableProperty] public partial bool HasPayments { get; set; }
    [ObservableProperty] public partial string TotalToPayText { get; set; } = "";
    [ObservableProperty] public partial string Message { get; set; } = "";

    public bool HasMessage => Message != "";

    partial void OnMessageChanged(string value) => OnPropertyChanged(nameof(HasMessage));

    public async Task LoadAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var items = (await _store.GetItemsAsync())
                .Where(i => i.Kind == ItemKind.Expense && i.PaymentMethod is PaymentMethod.Eft or PaymentMethod.Transfer).ToList();
            _budgetAccounts = (await _store.GetAccountsAsync()).Where(a => a.IsOnBudget && !a.IsClosed).ToList();
            BudgetAccounts = _budgetAccounts.Select(a => new AccountOption(a.Id, a.Name, true)).ToList();

            Sections.Clear();
            _rows.Clear();
            foreach (var heading in items.Select(i => string.IsNullOrWhiteSpace(i.ReportHeading) ? "Other payments" : i.ReportHeading!.Trim())
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var section = new PaymentSection(heading);
                foreach (var item in items.Where(i => string.Equals(
                             string.IsNullOrWhiteSpace(i.ReportHeading) ? "Other payments" : i.ReportHeading!.Trim(), heading, StringComparison.OrdinalIgnoreCase)))
                {
                    var row = new PaymentRow(item.Id, item.Name, item.AccountRef);
                    section.Rows.Add(row);
                    _rows[item.Id] = row;
                }
                Sections.Add(section);
            }

            _months = BudgetEngine.Calculate(await _store.LoadSnapshotAsync());
            MonthIndex = _months.Count - 1;
            ApplyMonth();
        }
        finally { _gate.Release(); }
    }

    public void GoBack() { if (MonthIndex > 0) { MonthIndex--; ApplyMonth(); } }

    public void GoForward() { if (MonthIndex < _months.Count - 1) { MonthIndex++; ApplyMonth(); } }

    private void ApplyMonth()
    {
        if (MonthIndex < 0 || MonthIndex >= _months.Count) return;
        var month = _months[MonthIndex];
        MonthLabel = month.Label;
        CanGoBack = MonthIndex > 0;
        CanGoForward = MonthIndex < _months.Count - 1;

        var items = month.Items.ToDictionary(i => i.ItemId);
        foreach (var (id, row) in _rows) row.Apply(items.GetValueOrDefault(id) ?? new ItemMonth(id, 0, 0));
        foreach (var section in Sections) section.IsVisible = section.Rows.Any(r => r.IsVisible);
        HasPayments = _rows.Values.Any(r => r.IsVisible);
        TotalToPayText = Format.Money(_rows.Values.Sum(r => r.ToPayCents));
    }

    /// <summary>Records an outflow on the item from a budget account. Returns an error sentence, or null on success.</summary>
    public async Task<string?> RecordPaymentAsync(PaymentRow row, Guid? accountId, DateTimeOffset date, double rands, string payee)
    {
        if (accountId is not { } account) return "Choose the account the payment came from.";
        var cents = AppServices.ToCents(rands);
        if (cents <= 0) return "Enter an amount above zero.";

        await _gate.WaitAsync();
        try
        {
            var day = DateOnly.FromDateTime(date.DateTime);
            await _store.AddTransactionAsync(
                new TransactionEntity
                {
                    AccountId = account, Date = day, DayOrder = await _store.NextDayOrderAsync(day),
                    Payee = payee.Trim(), Amount = -cents,
                },
                new[] { new SplitLineEntity { BudgetItemId = row.ItemId, Amount = -cents } });
            _months = BudgetEngine.Calculate(await _store.LoadSnapshotAsync());
            MonthIndex = Math.Min(MonthIndex, _months.Count - 1);
            ApplyMonth();
            return null;
        }
        catch (Exception ex) { return $"Could not save: {ex.Message}"; }
        finally { _gate.Release(); }
    }
}
