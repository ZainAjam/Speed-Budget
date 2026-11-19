using ClosedXML.Excel;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SpeedBudget.Core;
using SpeedBudget.Data;

namespace SpeedBudget.Data.Tests;

public class TransactionImportTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sb-tximport-" + Guid.NewGuid().ToString("N"));
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly SpeedBudgetDb _db;
    private readonly BudgetStore _store;

    public TransactionImportTests()
    {
        Directory.CreateDirectory(_dir);
        _connection.Open();
        _db = new SpeedBudgetDb(new DbContextOptionsBuilder<SpeedBudgetDb>().UseSqlite(_connection).Options, new HlcClock("devicea", () => 1_800_000_000_000));
        _db.Database.Migrate();
        _store = new BudgetStore(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch { /* a temp folder left behind is harmless */ }
    }

    /// <summary>A file with only the headings, as a sheet of the user's own (or an export with nothing in it) would be.</summary>
    private string HeadingsOnly(string name, bool withAccount)
    {
        var path = Path.Combine(_dir, name);
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("Transactions");
        var headings = withAccount
            ? new[] { "Date", "Counterparty", "Memo", "Budget item", "Account", "Outflow", "Inflow", "Cleared" }
            : new[] { "Date", "Counterparty", "Memo", "Budget item", "Outflow", "Inflow", "Cleared" };
        for (var i = 0; i < headings.Length; i++) sheet.Cell(1, i + 1).Value = headings[i];
        book.SaveAs(path);
        return path;
    }

    [Fact]
    public void A_file_with_only_headings_has_no_lines_and_all_accounts_needs_an_account_column()
    {
        Assert.Empty(TransactionImport.Read(HeadingsOnly("all.xlsx", withAccount: true), true).Rows);
        var missing = TransactionImport.Read(HeadingsOnly("one.xlsx", withAccount: false), withAccount: true);
        Assert.Empty(missing.Rows);
        Assert.Single(missing.Problems);
    }

    [Fact]
    public async Task A_filled_in_file_is_read_and_imported_in_one_go_with_items_found_by_name()
    {
        var account = await _store.AddAccountAsync("Cheque", AccountType.Cheque, true);
        var group = await _store.AddGroupAsync("Everyday", ItemKind.Expense);
        var groceries = await _store.AddItemAsync(group.Id, null, "Groceries", ItemKind.Expense);

        var path = Path.Combine(_dir, "tx.xlsx");
        path = HeadingsOnly("tx.xlsx", withAccount: false);
        using (var book = new XLWorkbook(path))
        {
            var sheet = book.Worksheet("Transactions");
            sheet.Cell(2, 1).Value = new DateTime(2024, 3, 5);
            sheet.Cell(2, 2).Value = "Checkers";
            sheet.Cell(2, 3).Value = "weekly shop";
            sheet.Cell(2, 4).Value = "groceries";
            sheet.Cell(2, 5).Value = "1 250,50";
            sheet.Cell(2, 7).Value = "Yes";
            sheet.Cell(3, 1).Value = "2024-03-06";
            sheet.Cell(3, 2).Value = "Employer";
            sheet.Cell(3, 4).Value = "Nonexistent";
            sheet.Cell(3, 6).Value = 15000;
            sheet.Cell(4, 1).Value = "not a date";
            sheet.Cell(4, 2).Value = "Broken";
            sheet.Cell(4, 5).Value = 10;
            sheet.Cell(5, 1).Value = new DateTime(2024, 3, 7);
            sheet.Cell(5, 2).Value = "No amount";
            book.Save();
        }

        var read = TransactionImport.Read(path, withAccount: false);
        Assert.Equal(2, read.Rows.Count);
        Assert.Equal(2, read.Problems.Count);
        Assert.Equal(-125_050, read.Rows[0].Amount);
        Assert.True(read.Rows[0].Cleared);
        Assert.Equal(1_500_000, read.Rows[1].Amount);

        var preview = await _store.ImportTransactionsAsync(account.Id, read.Rows, apply: false);
        Assert.Equal(2, preview.Added);
        Assert.Empty(await _store.GetTransactionsAsync(account.Id));

        var result = await _store.ImportTransactionsAsync(account.Id, read.Rows);
        Assert.Equal(2, result.Added);
        Assert.Single(result.Notes);   // the item that was not found
        var saved = await _store.GetTransactionsAsync(account.Id);
        Assert.Equal(["Checkers", "Employer"], saved.Select(t => t.Transaction.Payee));
        Assert.Equal(groceries.Id, saved[0].Lines.Single().BudgetItemId);
        Assert.Null(saved[1].Lines.Single().BudgetItemId);
    }

    [Fact]
    public async Task Importing_into_all_accounts_uses_the_account_names_and_skips_unknown_ones()
    {
        var a = await _store.AddAccountAsync("Cheque", AccountType.Cheque, true);
        await _store.AddAccountAsync("Savings", AccountType.Savings, true);
        TransactionImportRow Row(int line, string account) => new(line, new DateOnly(2024, 3, 5), "Shop", null, null, account, -1_000, false);

        var result = await _store.ImportTransactionsAsync(null, [Row(2, "savings"), Row(3, "Nowhere")]);
        Assert.Equal(1, result.Added);
        Assert.Single(result.Skipped);
        Assert.Empty(await _store.GetTransactionsAsync(a.Id));
        Assert.Single(await _store.GetTransactionsAsync());
    }

    [Fact]
    public async Task Several_transactions_are_deleted_in_one_go_and_exported_with_their_figures()
    {
        var account = await _store.AddAccountAsync("Cheque", AccountType.Cheque, true);
        var other = await _store.AddAccountAsync("Savings", AccountType.Savings, true);
        await _store.AddTransactionAsync(new TransactionEntity { AccountId = account.Id, Date = new(2024, 3, 5), Payee = "Shop", Amount = -5_000 }, [new SplitLineEntity { Amount = -5_000 }]);
        await _store.AddTransactionAsync(new TransactionEntity { AccountId = account.Id, Date = new(2024, 3, 6), Payee = "Employer", Amount = 20_000, IsCleared = true }, [new SplitLineEntity { Amount = 20_000 }]);
        await _store.AddTransferAsync(account.Id, other.Id, new(2024, 3, 7), 1_000, null, null);

        var path = Path.Combine(_dir, "export.xlsx");
        await _store.ExportTransactionsAsync(path, account.Id);
        using (var book = new XLWorkbook(path))
        {
            var sheet = book.Worksheet("Transactions");
            Assert.Equal("New Month", sheet.Cell(2, 3).GetString());   // the first month heads the table
            Assert.Equal("Shop", sheet.Cell(3, 3).GetString());
            Assert.Equal(50, sheet.Cell(3, 7).GetDouble());
            Assert.Equal(200, sheet.Cell(4, 8).GetDouble());
            Assert.Equal("Yes", sheet.Cell(4, 9).GetString());
            Assert.Equal(5, sheet.LastRowUsed()!.RowNumber());
        }

        var all = await _store.GetTransactionsAsync(account.Id);
        await _store.DeleteTransactionsAsync(all.Select(t => t.Transaction));
        Assert.Empty(await _store.GetTransactionsAsync());   // the transfer's other side went too
    }


    [Fact]
    public async Task Importing_an_export_of_the_table_again_adds_nothing()
    {
        var account = await _store.AddAccountAsync("Cheque", AccountType.Cheque, true);
        var other = await _store.AddAccountAsync("Savings", AccountType.Savings, true);
        await _store.AddTransactionAsync(new TransactionEntity { AccountId = account.Id, Date = new(2024, 3, 5), Payee = "Shop", Memo = "milk", Amount = -5_000 }, [new SplitLineEntity { Amount = -5_000 }]);
        await _store.AddTransactionAsync(new TransactionEntity { AccountId = account.Id, Date = new(2024, 3, 5), Payee = "Shop", Memo = "milk", Amount = -5_000 }, [new SplitLineEntity { Amount = -5_000 }]);   // two the same: both are there
        await _store.AddTransactionAsync(new TransactionEntity { AccountId = account.Id, Date = new(2024, 3, 6), Payee = "Employer", Amount = 20_000, IsCleared = true }, [new SplitLineEntity { Amount = 20_000 }]);
        await _store.AddTransferAsync(account.Id, other.Id, new(2024, 3, 7), 1_000, null, null);
        var before = (await _store.GetTransactionsAsync()).Count;

        var path = Path.Combine(_dir, "again.xlsx");
        await _store.ExportTransactionsAsync(path, null);
        var read = TransactionImport.Read(path, withAccount: true);
        var preview = await _store.ImportTransactionsAsync(null, read.Rows, apply: false);
        Assert.Equal(0, preview.Added);
        Assert.Contains(preview.Notes, n => n.Contains("already in the table"));
        await _store.ImportTransactionsAsync(null, read.Rows);
        Assert.Equal(before, (await _store.GetTransactionsAsync()).Count);

        // A line that is not in the table is still added.
        var more = new List<TransactionImportRow>(read.Rows) { new(99, new(2024, 3, 8), "New shop", null, null, "Cheque", -100, false) };
        Assert.Equal(1, (await _store.ImportTransactionsAsync(null, more)).Added);
        Assert.Equal(before + 1, (await _store.GetTransactionsAsync()).Count);
    }
    [Fact]
    public async Task A_balance_account_exports_its_lines_with_the_figures_worked_out_and_can_be_imported_again()
    {
        var fund = await _store.AddAccountAsync("Retirement", AccountType.Investment, false, trackBalances: true);
        await _store.AddBalanceEntryAsync(fund.Id, new(2024, 1, 29), 1_000_000, 0, "first");
        await _store.AddBalanceEntryAsync(fund.Id, new(2024, 1, 30), 1_040_000, 5_000, null);

        var path = Path.Combine(_dir, "balances.xlsx");
        await _store.ExportBalancesAsync(path, fund.Id);
        using (var book = new XLWorkbook(path))
        {
            var sheet = book.Worksheet("Balances");
            Assert.Equal(10_400, sheet.Cell(3, 2).GetDouble());
            Assert.Equal(400, sheet.Cell(3, 6).GetDouble());
            Assert.Equal(450, sheet.Cell(3, 7).GetDouble());
        }
        var read = BalanceImport.Read(path);
        Assert.Equal(2, read.Rows.Count);
        Assert.Equal(5_000, read.Rows[1].Withdrawal);
    }

    [Fact]
    public async Task New_month_lines_are_exported_in_place_and_imported_in_place()
    {
        var account = await _store.AddAccountAsync("Cheque", AccountType.Cheque, true);
        await _store.SetFirstMonthAsync(new DateOnly(2024, 3, 1));
        TransactionImportRow Tx(int line, int day, string payee, int month = 3) => new(line, new DateOnly(2024, month, day), payee, null, null, null, -1_000, false);
        TransactionImportRow Month(int line, string label) => new(line, default, TransactionImport.MonthRowName, null, null, null, 0, false, label);

        var preview = await _store.ImportTransactionsAsync(account.Id, [Tx(2, 5, "A"), Tx(3, 20, "B"), Month(4, "April 2024"), Tx(5, 3, "C", month: 4), Month(6, "March 2024")], apply: false);
        Assert.Equal(1, preview.MonthLines);   // March is the first month: it already has its place
        Assert.Single(preview.Notes);

        var result = await _store.ImportTransactionsAsync(account.Id, [Tx(2, 5, "A"), Tx(3, 20, "B"), Month(4, "April 2024"), Tx(5, 3, "C", month: 4)]);
        Assert.Equal(3, result.Added);
        var snapshot = await _store.LoadSnapshotAsync();
        var line = Assert.Single(snapshot.Markers);
        Assert.Equal("April 2024", line.Label);

        // The line sits after B (20 March) and before C (3 April).
        var key = (line.Date, line.DayOrder);
        var all = await _store.GetTransactionsAsync(account.Id);
        Assert.True(all.Single(t => t.Transaction.Payee == "B").Transaction is { } b && (b.Date, b.DayOrder).CompareTo(key) < 0);
        Assert.True(all.Single(t => t.Transaction.Payee == "C").Transaction is { } c && (c.Date, c.DayOrder).CompareTo(key) > 0);

        var path = Path.Combine(_dir, "months.xlsx");
        await _store.ExportTransactionsAsync(path, account.Id);
        var read = TransactionImport.Read(path, withAccount: false);
        Assert.Equal(["March 2024", null, null, "April 2024", null], read.Rows.Select(r => r.MonthLabel));   // the first month, A, B, the new month, C
    }
}
