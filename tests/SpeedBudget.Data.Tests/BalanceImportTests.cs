using ClosedXML.Excel;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SpeedBudget.Data;

namespace SpeedBudget.Data.Tests;

public class BalanceImportTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sb-import-" + Guid.NewGuid().ToString("N"));

    public BalanceImportTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* a temp folder left behind is harmless */ }
    }

    private string Template()
    {
        var path = Path.Combine(_dir, "template.xlsx");
        BalanceImport.CreateTemplate(path);
        return path;
    }

    [Fact]
    public void The_template_is_blank_with_the_four_headings()
    {
        using var book = new XLWorkbook(Template());
        var sheet = book.Worksheet("Balances");
        Assert.Equal(new[] { "Date", "Balance", "Withdrawal", "Comment" }, Enumerable.Range(1, 4).Select(c => sheet.Cell(1, c).GetString()));
        Assert.Equal(1, sheet.LastRowUsed()!.RowNumber());
        Assert.Empty(BalanceImport.Read(Template()).Rows);
    }

    [Fact]
    public void A_filled_in_template_is_read_with_dates_numbers_text_amounts_and_comments()
    {
        var path = Template();
        using (var book = new XLWorkbook(path))
        {
            var sheet = book.Worksheet("Balances");
            sheet.Cell(2, 1).Value = new DateTime(2024, 1, 29);
            sheet.Cell(2, 2).Value = 1582539.55;
            sheet.Cell(3, 1).Value = "30/01/2024";             // written as text
            sheet.Cell(3, 2).Value = "1 580 316.21";            // as the sheet it came from shows it
            sheet.Cell(4, 1).Value = new DateTime(2024, 2, 1);
            sheet.Cell(4, 2).Value = "1,570,829.87";
            sheet.Cell(4, 3).Value = -8700;                      // a minus sign is taken as the amount withdrawn
            sheet.Cell(4, 4).Value = "Pay Day";
            sheet.Cell(6, 1).Value = new DateTime(2024, 2, 2);   // no balance
            sheet.Cell(7, 2).Value = 100;                         // no date
            sheet.Cell(8, 1).Value = new DateTime(2024, 2, 3);
            sheet.Cell(8, 2).Value = "abc";                       // not an amount
            book.Save();
        }

        var file = BalanceImport.Read(path);

        Assert.Equal(new[] { new DateOnly(2024, 1, 29), new DateOnly(2024, 1, 30), new DateOnly(2024, 2, 1) }, file.Rows.Select(r => r.Date));
        Assert.Equal(new long[] { 158_253_955, 158_031_621, 157_082_987 }, file.Rows.Select(r => r.Balance));
        Assert.Equal(new long[] { 0, 0, 870_000 }, file.Rows.Select(r => r.Withdrawal));
        Assert.Equal("Pay Day", file.Rows[2].Comment);
        Assert.Equal(new[] { 6, 7, 8 }, file.Problems.Select(p => p.Line));
    }

    [Theory]
    [InlineData("1 580 316.21", 158_031_621)]
    [InlineData("1,582,539.55", 158_253_955)]
    [InlineData("1.582.539,55", 158_253_955)]
    [InlineData("8700", 870_000)]
    [InlineData("R 8 700,50", 870_050)]
    [InlineData("-2,223.34", -222_334)]
    public void Amounts_typed_as_text_are_understood(string text, long cents)
    {
        Assert.True(BalanceImport.TryMoney(text, out var read));
        Assert.Equal(cents, read);
    }

    [Fact]
    public async Task Importing_adds_new_dates_and_updates_the_ones_the_account_has()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<SpeedBudgetDb>().UseSqlite(connection).Options;
        using var db = new SpeedBudgetDb(options, new HlcClock("devicea", () => 1_800_000_000_000));
        db.Database.Migrate();
        var store = new BudgetStore(db);
        var fund = await store.AddAccountAsync("Retirement", AccountType.Investment, false, trackBalances: true);
        await store.AddBalanceEntryAsync(fund.Id, new(2024, 1, 29), 1_000_000, 0, "first");

        ImportRow Row(int day, long balance, long withdrawal = 0, string? comment = null) => new(day, new DateOnly(2024, 1, day), balance, withdrawal, comment);
        var rows = new[] { Row(29, 1_010_000), Row(30, 1_020_000), Row(31, 1_030_000, 5_000, "month end"), Row(31, 1_040_000, 5_000, "month end") };

        var preview = await store.ImportBalancesAsync(fund.Id, rows, apply: false);
        Assert.Equal(new BalanceImportResult(2, 1), preview);
        Assert.Single(await store.GetBalanceEntriesAsync(fund.Id));   // nothing saved yet

        var result = await store.ImportBalancesAsync(fund.Id, rows);
        Assert.Equal(new BalanceImportResult(2, 1), result);
        var entries = await store.GetBalanceEntriesAsync(fund.Id);
        Assert.Equal(new long[] { 1_010_000, 1_020_000, 1_040_000 }, entries.Select(e => e.Balance));
        Assert.Equal("first", entries[0].Comment);          // a line with no comment leaves the old one
        Assert.Equal("month end", entries[2].Comment);
    }
}
