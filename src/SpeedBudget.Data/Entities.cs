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
    /// <summary>An optional emoji shown in front of the item's name, such as 🛒 for groceries.</summary>
    public string? Emoji { get; set; }

    /// <summary>The name as shown to the user: the emoji, if any, then the name. Not stored.</summary>
    public string DisplayName => string.IsNullOrEmpty(Emoji) ? Name : $"{Emoji} {Name}";

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
    /// <summary>Legacy column from a version that hid items. Nothing uses it any more; every item is shown.</summary>
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

/// <summary>
/// A person or business that money is paid to or received from. Transactions keep the payee's name as text, so a
/// rename here updates the transactions that use it.
/// </summary>
public class PayeeEntity : SyncEntity
{
    public string Name { get; set; } = "";

    /// <summary>An optional emoji shown in front of the counterparty's name.</summary>
    public string? Emoji { get; set; }
}

/// <summary>
/// A transaction that is expected but has not happened yet. It does not touch any balance or budget. When it happens it is
/// recorded as a real transaction (with the date it happened) and removed from the expected list.
/// </summary>
public class ExpectedTransactionEntity : SyncEntity
{
    public Guid AccountId { get; set; }
    public string Payee { get; set; } = "";
    public string? Memo { get; set; }

    /// <summary>Money out is negative, money in positive, in cents.</summary>
    public long Amount { get; set; }
    public Guid? BudgetItemId { get; set; }

    /// <summary>When it is expected, if known. Only a guide: the real date is given when it is recorded.</summary>
    public DateOnly? ExpectedDate { get; set; }
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
