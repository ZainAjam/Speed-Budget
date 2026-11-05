namespace SpeedBudget.Data;

/// <summary>One row changed by a save: what it was before (or that it did not exist), so the save can be reversed.</summary>
public sealed record UndoRow(Type EntityType, Guid Id, bool WasAdded, bool NowDeleted, IReadOnlyDictionary<string, object?> Old);

/// <summary>Everything one user action changed. Undoing it puts every row back as it was.</summary>
public sealed class UndoUnit(DateTime at, List<UndoRow> rows)
{
    public DateTime At { get; set; } = at;
    public List<UndoRow> Rows { get; } = rows;

    /// <summary>What the action did, in words: "adding a transaction", "changing a budgeted amount".</summary>
    public string Describe()
    {
        static string Noun(Type t) => t.Name switch
        {
            nameof(TransactionEntity) or nameof(SplitLineEntity) => "a transaction",
            nameof(AllocationEntity) => "a budgeted amount",
            nameof(IncomeHoldEntity) => "held income",
            nameof(PayeeEntity) => "a counterparty",
            nameof(ItemEntity) => "a budget item",
            nameof(GroupEntity) => "a budget group",
            nameof(AccountEntity) => "an account",
            nameof(ExpectedTransactionEntity) => "an expected transaction",
            nameof(MonthMarkerEntity) => "a month",
            _ => "your data",
        };

        var verb = Rows.All(r => r.WasAdded) ? "adding"
            : Rows.Any(r => r.NowDeleted && r.Old.TryGetValue(nameof(SyncEntity.IsDeleted), out var d) && d is false) ? "deleting"
            : Rows.Any(r => !r.NowDeleted && r.Old.TryGetValue(nameof(SyncEntity.IsDeleted), out var d) && d is true) ? "restoring"
            : "changing";
        var nouns = Rows.Select(r => Noun(r.EntityType)).Distinct().Take(2).ToList();
        return $"{verb} {string.Join(" and ", nouns)}";
    }
}

/// <summary>
/// The history behind Ctrl+Z. Every save of a synced row records what the rows were before, so any change on any screen can
/// be reversed, and reversing it can itself be reversed (Ctrl+Y). Saves that follow each other within a moment belong to
/// one action, such as a transaction and its lines, and are undone together.
/// </summary>
public sealed class UndoLog
{
    private const int Limit = 200;
    private static readonly TimeSpan SameAction = TimeSpan.FromMilliseconds(700);

    private readonly List<UndoUnit> _undo = new();
    private readonly List<UndoUnit> _redo = new();

    /// <summary>Off while the app starts, so housekeeping is not something a user can undo.</summary>
    public bool Recording { get; set; }

    /// <summary>Set while an undo or redo is being applied, so its own save goes to the opposite list.</summary>
    internal bool? Reversing { get; set; }   // false = undoing, true = redoing

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Clear() { _undo.Clear(); _redo.Clear(); }

    internal UndoUnit? Pop(bool redo)
    {
        var list = redo ? _redo : _undo;
        if (list.Count == 0) return null;
        var unit = list[^1];
        list.RemoveAt(list.Count - 1);
        return unit;
    }

    internal void Put(bool redo, UndoUnit unit) => (redo ? _redo : _undo).Add(unit);

    internal void Record(List<UndoRow> rows)
    {
        if (!Recording || rows.Count == 0) return;
        var now = DateTime.UtcNow;

        switch (Reversing)
        {
            case false: _redo.Add(new UndoUnit(now, rows)); return;    // an undo's inverse can be redone
            case true: _undo.Add(new UndoUnit(now, rows)); return;     // and a redo's can be undone again
        }

        _redo.Clear();
        if (_undo.Count > 0 && now - _undo[^1].At < SameAction)
        {
            // One action saved in several steps: keep the earliest "before" of each row.
            var last = _undo[^1];
            foreach (var row in rows)
            {
                var i = last.Rows.FindIndex(r => r.Id == row.Id && r.EntityType == row.EntityType);
                if (i < 0) last.Rows.Add(row);
                else last.Rows[i] = last.Rows[i] with { NowDeleted = row.NowDeleted };
            }
            last.At = now;
            return;
        }

        _undo.Add(new UndoUnit(now, rows));
        if (_undo.Count > Limit) _undo.RemoveAt(0);
    }
}
