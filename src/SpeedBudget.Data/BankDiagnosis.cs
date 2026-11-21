using Microsoft.EntityFrameworkCore;

namespace SpeedBudget.Data;

public enum DiagnosisKind
{
    /// <summary>The account and the file already disagree before the file's first line: the opening balance, or history the file does not cover.</summary>
    OpeningGap,

    /// <summary>A cleared transaction in the account on a day where the file has no such line.</summary>
    ClearedNotInFile,

    /// <summary>A line of the file on a day where the account has no such cleared transaction.</summary>
    InFileNotInApp,

    /// <summary>The same amount is in both, a few days apart.</summary>
    DatedDifferently,
}

/// <summary>One thing found to differ between the account and the file. <see cref="Amount"/> is how much it moves the account's balance away from the file's.</summary>
public sealed record DiagnosisItem(DiagnosisKind Kind, DateOnly Date, long Amount, TransactionEntity? Transaction, BankLine? Line);

/// <summary>Where the account's cleared balance and a bank file's running balance disagree, and what is probably behind it.</summary>
public sealed record BalanceDiagnosis(DateOnly LastDate, long FileBalance, long AppBalance, long OpeningGap, long AfterLastDate, IReadOnlyList<DiagnosisItem> Items)
{
    /// <summary>The account's cleared balance minus the file's, as at the file's last date. Zero when they agree.</summary>
    public long Gap => AppBalance - FileBalance;
    public bool Reconciles => Gap == 0;
}

public sealed partial class BudgetStore
{
    /// <summary>The most days apart that a line of the file and a transaction can be and still be taken as the same one, dated differently.</summary>
    private const int SameTransactionDays = 5;

    /// <summary>
    /// Compares the file's running balance with the account's cleared balance at the end of each day the file covers. The gap before the first
    /// line points at the opening balance; a change in the gap on a day points at that day: the lines of the file and the cleared transactions of
    /// the account are paired off by amount, and what is left over on either side is the difference. Null when the file has no balances.
    /// </summary>
    public async Task<BalanceDiagnosis?> DiagnoseBalanceAsync(Guid accountId, IReadOnlyList<BankLine> lines, (DateOnly Date, long Balance)? statement = null, CancellationToken ct = default)
    {
        var fileClosing = BankBalance.Closing(lines);
        if ((statement ?? fileClosing) is not { } closing) return null;

        var account = await db.Accounts.AsNoTracking().SingleAsync(a => a.Id == accountId, ct);
        var cleared = await db.Transactions.AsNoTracking().Where(t => t.AccountId == accountId && t.IsCleared && !t.IsUndated).ToListAsync(ct);
        long AppAsOf(DateOnly date) => account.OpeningBalance + cleared.Where(t => t.Date <= date).Sum(t => t.Amount);

        // The file's running balance can be followed day by day only when its last line agrees with the balance the file states. Otherwise (a balance
        // read from a cell, and no balance on each line) the whole file is compared in one go: lines and cleared transactions are paired off by amount.
        if (fileClosing is not { } followed || followed.Balance != closing.Balance)
        {
            var from = lines.Min(l => l.Date);
            var to = lines.Max(l => l.Date);
            var theirs = cleared.Where(t => t.Date >= from && t.Date <= to).ToList();
            var leftOver = new List<BankLine>();
            foreach (var line in lines)
            {
                var match = theirs.Where(t => t.Amount == line.Amount).OrderBy(t => Math.Abs(t.Date.DayNumber - line.Date.DayNumber)).FirstOrDefault();
                if (match is not null) theirs.Remove(match); else leftOver.Add(line);
            }
            var found = new List<DiagnosisItem>();
            found.AddRange(theirs.Select(t => new DiagnosisItem(DiagnosisKind.ClearedNotInFile, t.Date, t.Amount, t, null)));
            found.AddRange(leftOver.Select(l => new DiagnosisItem(DiagnosisKind.InFileNotInApp, l.Date, -l.Amount, null, l)));
            var residual = (AppAsOf(closing.Date) - closing.Balance) - found.Sum(i => i.Amount);
            if (residual != 0) found.Add(new DiagnosisItem(DiagnosisKind.OpeningGap, from, residual, null, null));
            return new BalanceDiagnosis(closing.Date, closing.Balance, AppAsOf(closing.Date), residual, AppAsOf(DateOnly.MaxValue) - AppAsOf(closing.Date), found.OrderBy(i => i.Date).ToList());
        }

        // The file's balance at the end of each day: the balance of the day's last line (the first, when the file runs newest first).
        var oldestFirst = lines[0].Date <= lines[^1].Date;
        var days = new List<(DateOnly Date, List<BankLine> Lines, long Balance)>();
        long? running = null;
        foreach (var group in lines.GroupBy(l => l.Date).OrderBy(g => g.Key))
        {
            var dayLines = group.ToList();
            var withBalance = dayLines.Where(l => l.Balance is not null).ToList();
            long balance;
            if (withBalance.Count > 0) balance = (oldestFirst ? withBalance[^1] : withBalance[0]).Balance!.Value;
            else if (running is { } before) balance = before + dayLines.Sum(l => l.Amount);   // a day with no balance of its own carries on from the day before
            else continue;
            days.Add((group.Key, dayLines, balance));
            running = balance;
        }
        if (days.Count == 0) return null;

        var items = new List<DiagnosisItem>();
        var first = days[0];
        var fileBefore = first.Balance - first.Lines.Sum(l => l.Amount);   // the file's balance before its first day
        var openingGap = AppAsOf(first.Date.AddDays(-1)) - fileBefore;
        if (openingGap != 0) items.Add(new DiagnosisItem(DiagnosisKind.OpeningGap, first.Date, openingGap, null, null));

        var gapBefore = openingGap;
        var appLeft = new List<TransactionEntity>();
        var fileLeft = new List<BankLine>();
        var previous = DateOnly.MinValue;
        foreach (var (date, dayLines, balance) in days)
        {
            var gap = AppAsOf(date) - balance;
            if (gap != gapBefore)
            {
                // Something differs on this day: pair the file's lines with the account's cleared transactions by amount.
                // (the account's transactions since the last day the file covers count too, as the file may have them under a later date)
                var theirs = cleared.Where(t => previous == DateOnly.MinValue ? t.Date == date : t.Date > previous && t.Date <= date).ToList();
                foreach (var line in dayLines)
                {
                    var match = theirs.FirstOrDefault(t => t.Amount == line.Amount);
                    if (match is not null) theirs.Remove(match); else fileLeft.Add(line);
                }
                appLeft.AddRange(theirs);
            }
            gapBefore = gap;
            previous = date;
        }

        // The same amount left over on both sides a few days apart is one transaction dated differently.
        foreach (var tx in appLeft.ToList())
        {
            var line = fileLeft.Where(l => l.Amount == tx.Amount && Math.Abs(l.Date.DayNumber - tx.Date.DayNumber) <= SameTransactionDays)
                .OrderBy(l => Math.Abs(l.Date.DayNumber - tx.Date.DayNumber)).FirstOrDefault();
            if (line is null) continue;
            appLeft.Remove(tx);
            fileLeft.Remove(line);
            items.Add(new DiagnosisItem(DiagnosisKind.DatedDifferently, tx.Date, 0, tx, line));
        }
        items.AddRange(appLeft.Select(t => new DiagnosisItem(DiagnosisKind.ClearedNotInFile, t.Date, t.Amount, t, null)));
        items.AddRange(fileLeft.Select(l => new DiagnosisItem(DiagnosisKind.InFileNotInApp, l.Date, -l.Amount, null, l)));


        return new BalanceDiagnosis(closing.Date, closing.Balance, AppAsOf(closing.Date), openingGap, AppAsOf(DateOnly.MaxValue) - AppAsOf(closing.Date), items.OrderBy(i => i.Date).ToList());
    }

    /// <summary>Marks a transaction cleared or not cleared. A transaction that has been reconciled is locked and left as it is (false is returned).</summary>
    public async Task<bool> SetClearedAsync(Guid transactionId, bool cleared, CancellationToken ct = default)
    {
        var t = await db.Transactions.SingleAsync(x => x.Id == transactionId, ct);
        if (t.IsReconciled) return false;
        t.IsCleared = cleared;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Gives a transaction the date (and bank date) the bank has for it. A reconciled transaction, or one side of a transfer, is left alone.</summary>
    public async Task<bool> SetBankDateAsync(Guid transactionId, DateOnly date, CancellationToken ct = default)
    {
        var t = await db.Transactions.SingleAsync(x => x.Id == transactionId, ct);
        if (t.IsReconciled || t.TransferPairId is not null) return false;
        t.Date = date;
        t.BankDate = date;
        t.IsUndated = false;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Deletes one transaction (both sides, when it is a transfer). A reconciled transaction is locked and left alone.</summary>
    public async Task<bool> DeleteTransactionAsync(Guid transactionId, CancellationToken ct = default)
    {
        var t = await db.Transactions.SingleAsync(x => x.Id == transactionId, ct);
        if (t.IsReconciled) return false;
        await DeleteTransactionsAsync([t], ct);
        return true;
    }

    public async Task SetOpeningBalanceAsync(Guid accountId, long openingBalance, CancellationToken ct = default)
    {
        var account = await db.Accounts.SingleAsync(a => a.Id == accountId, ct);
        account.OpeningBalance = openingBalance;
        await db.SaveChangesAsync(ct);
    }

    public async Task<long> GetOpeningBalanceAsync(Guid accountId, CancellationToken ct = default) =>
        (await db.Accounts.AsNoTracking().SingleAsync(a => a.Id == accountId, ct)).OpeningBalance;
}
