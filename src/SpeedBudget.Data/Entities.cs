using SpeedBudget.Core;

namespace SpeedBudget.Data;

/// <summary>
/// Base for every synced row. Rows are never physically deleted: IsDeleted marks a tombstone
/// so the deletion can reach other devices. There are no foreign keys, because pulled rows can
/// arrive in any order.
/// </summary>
public abstract class SyncEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string UpdatedAt { get; set; } = "";
    public string UpdatedBy { get; set; } = "";
    public bool IsDeleted { get; set; }
}

public enum AccountType { Cheque, Savings, Credit, Cash, Investment, Loan }

public class AccountEntity : SyncEntity
{
    public string Name { get; set; } = "";
    public AccountType Type { get; set; }
    public bool IsOnBudget { get; set; }
    public long OpeningBalance { get; set; }
    public bool IsClosed { get; set; }
    public int SortOrder { get; set; }
}

public class GroupEntity : SyncEntity
{
    public string Name { get; set; } = "";
    public ItemKind Kind { get; set; }
    public int SortOrder { get; set; }
}

public enum PaymentMethod { None, Eft, CashOrCard, DebitOrder, Transfer, Savings }

public class ItemEntity : SyncEntity
{
    public PaymentMethod PaymentMethod { get; set; }

    /// <summary>The heading this item is listed under on the Payments page, such as "Utilities".</summary>
    public string? ReportHeading { get; set; }

    public Guid GroupId { get; set; }
    public Guid? ParentItemId { get; set; }
    public string Name { get; set; } = "";
    public ItemKind Kind { get; set; }
    public string? AccountRef { get; set; }
    public string? Notes { get; set; }
    public bool IsPinned { get; set; }
    public bool IsArchived { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>
/// A synced key/value setting. The id is derived from the key, so every device uses the same row
/// and two devices setting the same key edit one row instead of creating duplicates.
/// </summary>
public class SettingEntity : SyncEntity
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";

    public static Guid IdFor(string key) =>
        new(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes("setting:" + key)));
}

public class MonthMarkerEntity : SyncEntity
{
    public DateOnly Date { get; set; }
    public int DayOrder { get; set; }
    public string Label { get; set; } = "";
}

public class AllocationEntity : SyncEntity
{
    public Guid MonthId { get; set; }
    public Guid BudgetItemId { get; set; }
    public long Amount { get; set; }
}

public class IncomeHoldEntity : SyncEntity
{
    public Guid MonthId { get; set; }
    public Guid IncomeItemId { get; set; }
    public long Amount { get; set; }
}

public class TransactionEntity : SyncEntity
{
    public Guid AccountId { get; set; }
    public DateOnly Date { get; set; }
    public int DayOrder { get; set; }
    public DateOnly? BankDate { get; set; }
    public string Payee { get; set; } = "";
    public string? Memo { get; set; }
    public long Amount { get; set; }
    public bool IsCleared { get; set; }

    /// <summary>Checked against a bank balance during a reconcile. Reconciled transactions are locked until unreconciled.</summary>
    public bool IsReconciled { get; set; }
    public Guid? TransferPairId { get; set; }
}

public class SplitLineEntity : SyncEntity
{
    public Guid TransactionId { get; set; }
    public Guid? BudgetItemId { get; set; }
    public long Amount { get; set; }
    public string? Memo { get; set; }
}
