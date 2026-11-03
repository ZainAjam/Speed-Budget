using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace SpeedBudget.Data;

/// <summary>Plain-file copies of the budget, so the data is never locked into the app.</summary>
public static class ExportService
{
    private static string Escape(string? value)
    {
        value ??= "";
        return value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }

    private static string Money(long cents) => (cents / 100m).ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>
    /// Every transaction as CSV, oldest first, one row per split line. Month lines appear as rows with the payee
    /// NEW MONTH and the month's name in the memo, like the Google Sheets version.
    /// </summary>
    public static async Task<string> TransactionsCsvAsync(BudgetStore store, CancellationToken ct = default)
    {
        var accounts = (await store.GetAccountsAsync(ct)).ToDictionary(a => a.Id, a => a.Name);
        var items = (await store.GetItemsAsync(ct)).ToDictionary(i => i.Id, i => i.Name);
        var markers = await store.GetMarkersAsync(ct);
        var transactions = await store.GetTransactionsAsync(null, ct);

        var rows = new List<(DateOnly Date, int Order, int Rank, string Line)>();
        foreach (var m in markers)
            rows.Add((m.Date, m.DayOrder, 0, string.Join(",", m.Date.ToString("yyyy-MM-dd"), "", "", "NEW MONTH", Escape(m.Label), "", "", "", "")));

        foreach (var d in transactions)
        {
            var t = d.Transaction;
            foreach (var line in d.Lines.DefaultIfEmpty(null!))
            {
                var amount = line?.Amount ?? t.Amount;
                var item = line?.BudgetItemId is { } id && items.TryGetValue(id, out var name) ? name : "";
                var memo = string.Join(" ", new[] { t.Memo, line?.Memo }.Where(s => !string.IsNullOrWhiteSpace(s)));
                rows.Add((t.Date, t.DayOrder, 1, string.Join(",",
                    t.Date.ToString("yyyy-MM-dd"),
                    t.BankDate?.ToString("yyyy-MM-dd") ?? "",
                    Escape(accounts.GetValueOrDefault(t.AccountId, "")),
                    Escape(t.Payee),
                    Escape(memo),
                    Escape(item),
                    amount < 0 ? Money(-amount) : "",
                    amount > 0 ? Money(amount) : "",
                    t.IsCleared ? "TRUE" : "FALSE")));
            }
        }

        var csv = new StringBuilder("Date,Bank date,Account,Payee,Memo,Budget item,Outflow,Inflow,Cleared\r\n");
        foreach (var row in rows.OrderBy(r => r.Date).ThenBy(r => r.Order).ThenBy(r => r.Rank)) csv.Append(row.Line).Append("\r\n");
        return csv.ToString();
    }

    /// <summary>Everything in the budget as one JSON document, for archiving or moving to another tool.</summary>
    public static async Task<string> EverythingJsonAsync(SpeedBudgetDb db, CancellationToken ct = default)
    {
        var document = new
        {
            exportedAt = DateTimeOffset.Now,
            accounts = await db.Accounts.AsNoTracking().ToListAsync(ct),
            groups = await db.Groups.AsNoTracking().ToListAsync(ct),
            items = await db.Items.AsNoTracking().ToListAsync(ct),
            monthLines = await db.Markers.AsNoTracking().ToListAsync(ct),
            allocations = await db.Allocations.AsNoTracking().ToListAsync(ct),
            incomeHolds = await db.Holds.AsNoTracking().ToListAsync(ct),
            transactions = await db.Transactions.AsNoTracking().ToListAsync(ct),
            splitLines = await db.SplitLines.AsNoTracking().ToListAsync(ct),
            settings = await db.Settings.AsNoTracking().ToListAsync(ct),
        };
        return JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
    }
}
