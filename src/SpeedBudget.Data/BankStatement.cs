using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace SpeedBudget.Data;

/// <summary>
/// Where in a bank file to read one figure from. Kind 0: not in the file (for the file's date: the date of its latest line). Kind 1: the balance on the
/// latest line of a column. Kind 3: the last figure in a row (<see cref="Row"/> counted from 1; for a line such as "Balance, 2114.88, 2093.88"). Kind 2: one cell of the file (<see cref="Column"/> counted from 0, <see cref="Row"/> counted from 1 as in the sheet).
/// </summary>
public sealed record BalanceSource(int Kind = 0, int Column = -1, int Row = 0)
{
    public static readonly BalanceSource None = new();
}

/// <summary>Where a file says the account's cleared balance and available balance are, and the date they are as at. Remembered for the account.</summary>
public sealed record StatementSettings(BalanceSource Cleared, BalanceSource Available, BalanceSource FileDate)
{
    public static StatementSettings Empty => new(BalanceSource.None, BalanceSource.None, BalanceSource.None);
}

/// <summary>What was read from a file by <see cref="StatementSettings"/>: the date, and the cleared and available balances, each null when not given.</summary>
public sealed record StatementInfo(DateOnly Date, long? Cleared, long? Available)
{
    public bool HasBalances => Cleared is not null || Available is not null;
}

/// <summary>One figure of the file against the same figure in the account on the same date.</summary>
public sealed record BalanceComparison(string Label, long FileBalance, long AppBalance)
{
    public bool Matches => FileBalance == AppBalance;
}

public static class BankStatement
{
    /// <summary>A first guess at the settings: the running balance column for the cleared balance, and cells labelled like the figures wanted.</summary>
    public static StatementSettings Detect(BankTable table, BankFormat format)
    {
        var cleared = format.BalanceColumn >= 0 ? new BalanceSource(1, format.BalanceColumn) : BalanceSource.None;
        if (cleared.Kind == 0 && FindLabelled(table, ["closing balance", "cleared balance", "ledger balance", "statement balance"], IsMoney) is { } c) cleared = new BalanceSource(2, c.Column, c.Row);
        var available = BalanceSource.None;
        if (FindLabelled(table, ["available balance", "available bal"], IsMoney) is { } a) available = new BalanceSource(2, a.Column, a.Row);
        else if (FindBalanceRow(table, format.HeaderRows) is { } balanceRow) available = new BalanceSource(3, -1, balanceRow);
        var date = BalanceSource.None;
        if (FindLabelled(table, ["statement date", "closing date", "balance date", "as at", "as of"], IsDate) is { } d) date = new BalanceSource(2, d.Column, d.Row);
        return new StatementSettings(cleared, available, date);
    }

    /// <summary>The last figure in a row of the file (counted from 1), for a line that holds several, such as "Balance, 2114.88, 2093.88".</summary>
    private static long? LastFigureIn(BankTable table, int row)
    {
        if (row < 1 || row > table.Rows.Count) return null;
        var cells = table.Rows[row - 1];
        for (var c = cells.Length - 1; c >= 0; c--)
            if (cells[c] != "" && BankFile.TryMoney(cells[c]) is { } value) return value;
        return null;
    }

    /// <summary>A line above the transactions labelled as a balance that holds two or more figures: the last of them is taken as the available balance.</summary>
    private static int? FindBalanceRow(BankTable table, int beforeRow)
    {
        for (var r = 0; r < Math.Min(beforeRow, table.Rows.Count); r++)
        {
            var cells = table.Rows[r];
            if (cells.Length < 3 || !cells[0].Contains("balance", StringComparison.OrdinalIgnoreCase)) continue;
            if (cells.Skip(1).Count(c => c != "" && BankFile.TryMoney(c) is not null) >= 2) return r + 1;
        }
        return null;
    }

    private static bool IsMoney(string text) => BankFile.TryMoney(text) is not null;

    private static bool IsDate(string text) => TryAnyDate(text, null, out _);

    /// <summary>Finds a cell whose text says what it is (a label such as "Available balance"), and returns the cell next to it that holds the figure.</summary>
    private static (int Row, int Column)? FindLabelled(BankTable table, string[] labels, Func<string, bool> isValue)
    {
        for (var r = 0; r < table.Rows.Count; r++)
        {
            var row = table.Rows[r];
            for (var c = 0; c < row.Length; c++)
            {
                var text = row[c].ToLowerInvariant();
                if (text == "" || !labels.Any(l => text.Contains(l))) continue;
                for (var next = c + 1; next < row.Length; next++)
                    if (row[next] != "" && isValue(row[next])) return (r + 1, next);
                // a label on one row with its figure below it
                if (r + 1 < table.Rows.Count && c < table.Rows[r + 1].Length && isValue(table.Rows[r + 1][c])) return (r + 2, c);
            }
        }
        return null;
    }

    /// <summary>Reads a date with the file's own layout first, then any of the usual ones.</summary>
    public static bool TryAnyDate(string text, string? preferred, out DateOnly date)
    {
        date = default;
        text = text.Trim();
        if (text == "") return false;
        if (preferred is not null && BankFile.TryDate(text, preferred, out date)) return true;
        foreach (var format in BankFile.DateFormats)
            if (BankFile.TryDate(text, format, out date)) return true;
        return false;
    }

    /// <summary>Reads the date and the balances the settings point at. Null when no date can be found (no lines, and no date cell that reads).</summary>
    public static StatementInfo? Read(BankTable table, BankFormat format, StatementSettings settings, IReadOnlyList<BankLine> lines)
    {
        DateOnly? date = null;
        if (settings.FileDate.Kind == 2 && CellAt(table, settings.FileDate) is { } text && TryAnyDate(text, format.DateFormat, out var fromCell)) date = fromCell;
        date ??= lines.Count > 0 ? lines.Max(l => l.Date) : null;
        if (date is null) return null;
        return new StatementInfo(date.Value, ReadBalance(table, format, settings.Cleared, lines), ReadBalance(table, format, settings.Available, lines));
    }

    private static string? CellAt(BankTable table, BalanceSource source) =>
        source.Row >= 1 && source.Row <= table.Rows.Count && source.Column >= 0 && source.Column < table.Rows[source.Row - 1].Length ? table.Rows[source.Row - 1][source.Column] : null;

    private static long? ReadBalance(BankTable table, BankFormat format, BalanceSource source, IReadOnlyList<BankLine> lines)
    {
        if (source.Kind == 2) return CellAt(table, source) is { } text ? BankFile.TryMoney(text) : null;
        if (source.Kind == 3) return LastFigureIn(table, source.Row);
        if (source.Kind != 1 || source.Column < 0) return null;
        var withColumn = source.Column == format.BalanceColumn ? lines : BankFile.Parse(table, format with { BalanceColumn = source.Column }).Lines;
        return BankBalance.Closing(withColumn)?.Balance;
    }
}

public sealed partial class BudgetStore
{
    private static string StatementKey(Guid accountId) => $"bankstatement:{accountId}";

    public async Task<StatementSettings?> GetStatementSettingsAsync(Guid accountId, CancellationToken ct = default)
    {
        var json = await GetSettingAsync(StatementKey(accountId), ct);
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonSerializer.Deserialize<StatementSettings>(json); } catch (JsonException) { return null; }
    }

    public Task SaveStatementSettingsAsync(Guid accountId, StatementSettings settings, CancellationToken ct = default) =>
        SetSettingAsync(StatementKey(accountId), JsonSerializer.Serialize(settings), ct);

    /// <summary>
    /// Compares the figures the file gave with the account on the file's date: its cleared balance (the opening balance and the cleared
    /// transactions up to that date) and its available balance (the same with the uncleared transactions too).
    /// </summary>
    public async Task<IReadOnlyList<BalanceComparison>> CompareStatementAsync(Guid accountId, StatementInfo info, CancellationToken ct = default)
    {
        // A cleared transaction counts from the day the bank has it (its bank date, else its date); an uncleared one from its own date.
        var opening = (await db.Accounts.AsNoTracking().SingleAsync(a => a.Id == accountId, ct)).OpeningBalance;
        var lines = await db.Transactions.AsNoTracking().Where(t => t.AccountId == accountId && !t.IsUndated).Select(t => new { t.Amount, t.IsCleared, t.Date, t.BankDate }).ToListAsync(ct);
        var cleared = opening + lines.Where(t => t.IsCleared && (t.BankDate ?? t.Date) <= info.Date).Sum(t => t.Amount);
        var uncleared = lines.Where(t => !t.IsCleared && t.Date <= info.Date).Sum(t => t.Amount);
        var result = new List<BalanceComparison>();
        if (info.Cleared is { } statedCleared) result.Add(new BalanceComparison("Cleared", statedCleared, cleared));
        if (info.Available is { } statedAvailable) result.Add(new BalanceComparison("Available", statedAvailable, cleared + uncleared));
        return result;
    }
}
