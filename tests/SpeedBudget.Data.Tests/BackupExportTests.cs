using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SpeedBudget.Core;
using SpeedBudget.Data;

namespace SpeedBudget.Data.Tests;

/// <summary>Backups need a real database file, so these use a temporary folder instead of an in-memory database.</summary>
public sealed class BackupExportTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "speedbudget-tests-" + Guid.NewGuid().ToString("N"));
    private readonly SpeedBudgetDb _db;
    private readonly BudgetStore _store;

    public BackupExportTests()
    {
        _db = LocalDatabase.Open(_dir);
        _store = new BudgetStore(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* a leftover temp folder is harmless */ }
    }

    [Fact]
    public async Task A_backup_is_a_complete_openable_copy_of_the_database()
    {
        await _store.AddAccountAsync("Cheque", AccountType.Cheque, true);

        var path = BackupService.CreateBackup(_db, _dir, new DateTime(2027, 1, 5, 8, 0, 0));

        var options = new DbContextOptionsBuilder<SpeedBudgetDb>().UseSqlite($"Data Source={path}").Options;
        using var copy = new SpeedBudgetDb(options, new HlcClock("copy"));
        Assert.Equal("Cheque", Assert.Single(copy.Accounts.ToList()).Name);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }

    [Fact]
    public void Backup_is_only_made_when_the_last_one_is_old_enough_and_old_ones_are_pruned()
    {
        var day1 = new DateTime(2027, 1, 1, 9, 0, 0);

        Assert.NotNull(BackupService.BackupIfDue(_db, _dir, TimeSpan.FromHours(24), keep: 2, now: day1));
        Assert.NotNull(BackupService.BackupIfDue(_db, _dir, TimeSpan.FromHours(24), keep: 2, now: day1.AddDays(1)));
        Assert.NotNull(BackupService.BackupIfDue(_db, _dir, TimeSpan.FromHours(24), keep: 2, now: day1.AddDays(2)));

        var files = Directory.GetFiles(BackupService.BackupsDirectory(_dir)).Select(Path.GetFileName).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "budget-20270102-090000.db", "budget-20270103-090000.db" }, files);
    }

    [Fact]
    public async Task Csv_export_has_one_row_per_split_line_and_month_lines_like_the_sheet()
    {
        var cheque = await _store.AddAccountAsync("Cheque, main", AccountType.Cheque, true);
        var group = await _store.AddGroupAsync("Daily", ItemKind.Expense);
        var food = await _store.AddItemAsync(group.Id, null, "Food", ItemKind.Expense);
        var petrol = await _store.AddItemAsync(group.Id, null, "Petrol", ItemKind.Expense);
        var tx = await _store.AddTransactionAsync(
            new TransactionEntity { AccountId = cheque.Id, Date = new(2027, 1, 5), Payee = "Shop \"Big\"", Amount = -500_00, IsCleared = true },
            new[] { new SplitLineEntity { BudgetItemId = food.Id, Amount = -350_00 }, new SplitLineEntity { BudgetItemId = petrol.Id, Amount = -150_00 } });
        await _store.AddMarkerAsync("February 2027", new(2027, 1, 25), copyPreviousBudget: false);

        var lines = (await ExportService.TransactionsCsvAsync(_store)).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("Date,Bank date,Account,Counterparty,Memo,Budget item,Outflow,Inflow,Cleared", lines[0]);
        Assert.Equal(4, lines.Length);
        Assert.Contains(lines, l => l == "2027-01-05,,\"Cheque, main\",\"Shop \"\"Big\"\"\",,Food,350.00,,TRUE");
        Assert.Contains(lines, l => l == "2027-01-05,,\"Cheque, main\",\"Shop \"\"Big\"\"\",,Petrol,150.00,,TRUE");
        Assert.Equal("2027-01-25,,,NEW MONTH,February 2027,,,,", lines[^1]);
        Assert.NotNull(tx);
    }

    [Fact]
    public async Task Json_export_contains_every_table_and_skips_deleted_rows()
    {
        var keep = await _store.AddAccountAsync("Keep", AccountType.Cheque, true);
        var gone = await _store.AddAccountAsync("Gone", AccountType.Cash, false);
        await _store.DeleteOrCloseAccountAsync(gone);

        using var json = JsonDocument.Parse(await ExportService.EverythingJsonAsync(_db));

        var accounts = json.RootElement.GetProperty("accounts");
        Assert.Equal(1, accounts.GetArrayLength());
        Assert.Equal("Keep", accounts[0].GetProperty("Name").GetString());
        foreach (var table in new[] { "groups", "items", "monthLines", "allocations", "incomeHolds", "transactions", "splitLines", "settings" })
            Assert.True(json.RootElement.TryGetProperty(table, out _), table);
        Assert.NotEqual(Guid.Empty, keep.Id);
    }
}
