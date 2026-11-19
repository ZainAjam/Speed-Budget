using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.IO.Compression;
using SpeedBudget.Core;
using SpeedBudget.Data;

namespace SpeedBudget.Data.Tests;

/// <summary>Bank files: the layout is guessed from a sample, lines are read, and lines already in the account are found.</summary>
public sealed class BankImportTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly SpeedBudgetDb _db;
    private readonly BudgetStore _store;

    public BankImportTests()
    {
        _connection.Open();
        _db = new SpeedBudgetDb(new DbContextOptionsBuilder<SpeedBudgetDb>().UseSqlite(_connection).Options, new HlcClock("devicea", () => 1_800_000_000_000));
        _db.Database.Migrate();
        _store = new BudgetStore(_db) { Today = () => new DateOnly(2030, 1, 1) };
    }

    public void Dispose() { _db.Dispose(); _connection.Dispose(); }

    private static string Csv(string text)
    {
        var path = Path.Combine(Path.GetTempPath(), $"bank-{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void Detects_a_single_amount_column()
    {
        var path = Csv("Date,Description,Amount,Balance\n2026-11-03,\"Woolworths, Sandton\",-125.50,1000.00\n2026-11-04,Salary,5000.00,6000.00\n");
        var table = BankFile.ReadTable(path);
        var format = BankFile.Detect(table)!;
        var parse = BankFile.Parse(table, format);
        Assert.Equal(1, format.HeaderRows);
        Assert.Equal(2, parse.Lines.Count);
        Assert.Equal("Woolworths, Sandton", parse.Lines[0].Description);
        Assert.Equal(-12550, parse.Lines[0].Amount);
        Assert.Equal(500000, parse.Lines[1].Amount);
    }

    [Fact]
    public void Detects_separate_debit_and_credit_columns_and_day_first_dates()
    {
        var path = Csv("Date;Details;Debit;Credit;Balance\n25/11/2026;Rent;9 000,00;;100,00\n03/12/2026;Refund;;45,50;145,50\n");
        var parse = BankFile.Parse(BankFile.ReadTable(path), BankFile.Detect(BankFile.ReadTable(path))!);
        Assert.Equal(new DateOnly(2026, 11, 25), parse.Lines[0].Date);
        Assert.Equal(-900000, parse.Lines[0].Amount);
        Assert.Equal(4550, parse.Lines[1].Amount);
    }

    [Fact]
    public async Task Finds_lines_the_account_already_holds_once_each()
    {
        var account = new AccountEntity { Name = "Cheque", IsOnBudget = false };
        _db.Accounts.Add(account);
        await _db.SaveChangesAsync();
        await _store.AddTransactionAsync(new TransactionEntity { AccountId = account.Id, Date = new DateOnly(2026, 11, 3), Amount = -12550, Payee = "Woolies" }, new[] { new SplitLineEntity { Amount = -12550 } });

        BankLine L(int n, int day, long amount) => new(n, new DateOnly(2026, 11, day), "x", null, amount);
        var found = await _store.FindExistingAsync(account.Id, [L(1, 3, -12550), L(2, 3, -12550), L(3, 4, -999)]);
        Assert.Equal([true, false, false], found);
    }

    [Fact]
    public async Task The_layout_is_kept_per_account()
    {
        var id = Guid.NewGuid();
        var format = new BankFormat(1, 0, "dd/MM/yyyy", 1, 2, -1, -1, true);
        await _store.SaveBankFormatAsync(id, format);
        Assert.Equal(format, await _store.GetBankFormatAsync(id));
    }

    [Fact]
    public void Reads_excel_and_zip_files()
    {
        var xlsx = Path.Combine(Path.GetTempPath(), $"bank-{Guid.NewGuid():N}.xlsx");
        using (var book = new ClosedXML.Excel.XLWorkbook())
        {
            var sheet = book.AddWorksheet("Statement");
            sheet.Cell(1, 1).Value = "Date"; sheet.Cell(1, 2).Value = "Description"; sheet.Cell(1, 3).Value = "Amount";
            sheet.Cell(2, 1).Value = new DateTime(2026, 11, 3); sheet.Cell(2, 2).Value = "Salary"; sheet.Cell(2, 3).Value = 5000.5;
            book.SaveAs(xlsx);
        }
        var table = BankFile.ReadTable(xlsx);
        var parse = BankFile.Parse(table, BankFile.Detect(table)!);
        Assert.Equal(new DateOnly(2026, 11, 3), parse.Lines[0].Date);
        Assert.Equal(500050, parse.Lines[0].Amount);

        var zipPath = Path.Combine(Path.GetTempPath(), $"bank-{Guid.NewGuid():N}.zip");
        using (var zip = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create))
        {
            ZipFileExtensions.CreateEntryFromFile(zip, xlsx, "a.xlsx");
            ZipFileExtensions.CreateEntryFromFile(zip, Csv("Date,Description,Amount\n2026-11-04,Rent,-9000\n"), "b.csv");
        }
        var tables = BankFile.ReadTables(zipPath);
        Assert.Equal(2, tables.Count);
        Assert.Equal(-900000, BankFile.Parse(tables[1], BankFile.Detect(tables[1])!).Lines[0].Amount);
    }

    [Fact]
    public async Task Applying_clears_matches_and_adds_the_rest()
    {
        var account = new AccountEntity { Name = "Cheque", IsOnBudget = false };
        _db.Accounts.Add(account);
        await _db.SaveChangesAsync();
        await _store.AddTransactionAsync(new TransactionEntity { AccountId = account.Id, Date = new DateOnly(2026, 11, 3), Amount = -12550, Payee = "Woolies" }, new[] { new SplitLineEntity { Amount = -12550 } });

        BankLine L(int n, int day, long amount) => new(n, new DateOnly(2026, 11, day), "Bank " + n, null, amount);
        var matches = await _store.MatchBankLinesAsync(account.Id, [L(1, 4, -12550), L(2, 5, -999)]);
        Assert.NotNull(matches[0].Existing);
        Assert.Null(matches[1].Existing);
        var (cleared, added) = await _store.ApplyBankMatchesAsync(account.Id, [matches[0]], [matches[1].Line]);
        Assert.Equal((1, 1), (cleared, added));

        var all = await _db.Transactions.Where(t => t.AccountId == account.Id).ToListAsync();
        Assert.Equal(2, all.Count);
        Assert.All(all, t => Assert.True(t.IsCleared));
        Assert.Equal(new DateOnly(2026, 11, 4), all.Single(t => t.Payee == "Woolies").BankDate);
        // Run again: everything is now in the table and cleared.
        Assert.All(await _store.MatchBankLinesAsync(account.Id, [L(1, 4, -12550), L(2, 5, -999)]), m => Assert.NotNull(m.Existing));
    }

    [Fact]
    public async Task Applying_takes_the_chosen_values_from_the_file()
    {
        var account = new AccountEntity { Name = "Cheque", IsOnBudget = false };
        _db.Accounts.Add(account);
        await _db.SaveChangesAsync();
        await _store.AddTransactionAsync(new TransactionEntity { AccountId = account.Id, Date = new DateOnly(2026, 11, 17), Amount = -200000, Payee = "Checkers", Memo = "GROCERIES" }, new[] { new SplitLineEntity { Amount = -200000 } });

        var line = new BankLine(1, new DateOnly(2026, 11, 18), "CHECKERS SANDTON", "bank memo", -200000);
        var match = (await _store.MatchBankLinesAsync(account.Id, [line]))[0];

        await _store.ApplyBankMatchesAsync(account.Id, [new BankClear(match, FileDate: true, FilePayee: true)], []);
        var t = await _db.Transactions.AsNoTracking().SingleAsync(x => x.AccountId == account.Id);
        Assert.Equal(new DateOnly(2026, 11, 18), t.Date);
        Assert.Equal(new DateOnly(2026, 11, 18), t.BankDate);
        Assert.Equal("CHECKERS SANDTON", t.Payee);
        Assert.Equal("GROCERIES", t.Memo);   // the table's memo was kept
        Assert.True(t.IsCleared);
    }

    [Fact]
    public void The_closing_balance_is_the_balance_of_the_latest_line_whichever_way_the_file_runs()
    {
        var oldestFirst = Csv("Date,Description,Amount,Balance\n2026-11-03,A,-10.00,90.00\n2026-11-04,B,-5.00,85.00\n2026-11-04,C,-5.00,80.00\n");
        var table = BankFile.ReadTable(oldestFirst);
        var format = BankFile.Detect(table)!;
        Assert.Equal(3, format.BalanceColumn);
        var closing = BankBalance.Closing(BankFile.Parse(table, format).Lines)!.Value;
        Assert.Equal((new DateOnly(2026, 11, 4), 8000L), closing);

        var newestFirst = Csv("Date,Description,Amount,Balance\n2026-11-04,C,-5.00,80.00\n2026-11-04,B,-5.00,85.00\n2026-11-03,A,-10.00,90.00\n");
        var t2 = BankFile.ReadTable(newestFirst);
        Assert.Equal(8000L, BankBalance.Closing(BankFile.Parse(t2, BankFile.Detect(t2)!).Lines)!.Value.Balance);

        var none = Csv("Date,Description,Amount\n2026-11-03,A,-10.00\n");
        var t3 = BankFile.ReadTable(none);
        Assert.Null(BankBalance.Closing(BankFile.Parse(t3, BankFile.Detect(t3)!).Lines));
    }

    private async Task<Guid> AccountWithAsync(long opening, params (int Day, long Amount, bool Cleared)[] transactions)
    {
        var account = new AccountEntity { Name = "Cheque", IsOnBudget = false, OpeningBalance = opening };
        _db.Accounts.Add(account);
        await _db.SaveChangesAsync();
        foreach (var (day, amount, cleared) in transactions)
            await _store.AddTransactionAsync(new TransactionEntity { AccountId = account.Id, Date = new DateOnly(2026, 11, day), Amount = amount, Payee = $"T{day}", IsCleared = cleared }, new[] { new SplitLineEntity { Amount = amount } });
        return account.Id;
    }

    private static BankLine Line(int n, int day, long amount, long balance) => new(n, new DateOnly(2026, 11, day), "x", null, amount, balance);

    [Fact]
    public async Task A_file_that_agrees_reconciles()
    {
        var id = await AccountWithAsync(100000, (3, -1000, true), (4, -2000, true));
        var diagnosis = await _store.DiagnoseBalanceAsync(id, [Line(1, 3, -1000, 99000), Line(2, 4, -2000, 97000)]);
        Assert.True(diagnosis!.Reconciles);
        Assert.Empty(diagnosis.Items);
    }

    [Fact]
    public async Task A_different_opening_balance_shows_as_a_gap_before_the_first_line()
    {
        var id = await AccountWithAsync(150000, (3, -1000, true));
        var diagnosis = await _store.DiagnoseBalanceAsync(id, [Line(1, 3, -1000, 99000)]);
        Assert.Equal(50000, diagnosis!.Gap);
        var item = Assert.Single(diagnosis.Items);
        Assert.Equal(DiagnosisKind.OpeningGap, item.Kind);
        Assert.Equal(50000, item.Amount);
    }

    [Fact]
    public async Task Finds_a_cleared_transaction_the_file_does_not_have_and_a_line_the_account_does_not_have()
    {
        var id = await AccountWithAsync(100000, (3, -1000, true), (4, -700, true));          // 4th is not in the file
        var diagnosis = await _store.DiagnoseBalanceAsync(id, [Line(1, 3, -1000, 99000), Line(2, 5, -300, 98700)]);   // the 5th is not in the account
        Assert.Contains(diagnosis!.Items, i => i.Kind == DiagnosisKind.ClearedNotInFile && i.Transaction!.Amount == -700);
        Assert.Contains(diagnosis.Items, i => i.Kind == DiagnosisKind.InFileNotInApp && i.Line!.Amount == -300);
        Assert.Equal(diagnosis.Gap, diagnosis.Items.Sum(i => i.Amount));
    }

    [Fact]
    public async Task The_fixes_change_the_account()
    {
        var id = await AccountWithAsync(100000, (4, -700, true));
        var tx = await _db.Transactions.AsNoTracking().SingleAsync();
        Assert.True(await _store.SetBankDateAsync(tx.Id, new DateOnly(2026, 11, 6)));
        Assert.Equal(new DateOnly(2026, 11, 6), (await _db.Transactions.AsNoTracking().SingleAsync()).BankDate);
        Assert.True(await _store.SetClearedAsync(tx.Id, false));
        Assert.False((await _db.Transactions.AsNoTracking().SingleAsync()).IsCleared);
        await _store.SetOpeningBalanceAsync(id, 5);
        Assert.Equal(5, await _store.GetOpeningBalanceAsync(id));
        Assert.True(await _store.DeleteTransactionAsync(tx.Id));
        Assert.Empty(await _db.Transactions.AsNoTracking().ToListAsync());
    }
}
