namespace SpeedBudget.Core;

public sealed record ReconcileLine(long Amount, bool IsCleared);

/// <summary>
/// An account checked against its bank statement. Cleared transactions are the ones the bank has processed.
/// </summary>
/// <param name="BookBalance">Opening balance plus every transaction, cleared or not.</param>
/// <param name="ClearedBalance">Opening balance plus cleared transactions only. This should equal the statement.</param>
/// <param name="UnclearedTotal">Transactions the bank has not processed yet.</param>
/// <param name="Difference">Statement minus cleared balance; zero when reconciled. Null without a statement balance.</param>
/// <param name="AvailableToSpend">Statement plus uncleared transactions. Null without a statement balance.</param>
public sealed record Reconciliation(long BookBalance, long ClearedBalance, long UnclearedTotal, long? Difference, long? AvailableToSpend)
{
    public static Reconciliation Calculate(long openingBalance, IEnumerable<ReconcileLine> lines, long? statementBalance)
    {
        var all = lines.ToList();
        var uncleared = all.Where(l => !l.IsCleared).Sum(l => l.Amount);
        var cleared = openingBalance + all.Where(l => l.IsCleared).Sum(l => l.Amount);
        return new Reconciliation(
            openingBalance + all.Sum(l => l.Amount),
            cleared,
            uncleared,
            statementBalance is { } s ? s - cleared : null,
            statementBalance is { } t ? t + uncleared : null);
    }

    public bool IsReconciled => Difference == 0;
}
