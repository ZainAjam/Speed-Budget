using SpeedBudget.Core;

namespace SpeedBudget.Data;

/// <summary>A bank account to create. Opening balance is in cents.</summary>
public sealed class SetupAccount
{
    public string Name { get; set; } = "";
    public AccountType Type { get; set; } = AccountType.Cheque;
    public bool IsOnBudget { get; set; } = true;
    public long OpeningBalance { get; set; }
}

public sealed class SetupItem(string name, string? emoji = null, bool selected = true)
{
    public string Name { get; set; } = name;
    public string? Emoji { get; set; } = emoji;
    public bool Selected { get; set; } = selected;
}

public sealed class SetupGroup(string name, ItemKind kind, IEnumerable<SetupItem>? items = null)
{
    public string Name { get; set; } = name;
    public ItemKind Kind { get; set; } = kind;
    public List<SetupItem> Items { get; } = items?.ToList() ?? [];
}

public sealed class SetupCounterparty(string name, bool selected = true)
{
    public string Name { get; set; } = name;
    public bool Selected { get; set; } = selected;
}

/// <summary>What the setup wizard will create: accounts, budget groups with items, and common counterparties.</summary>
public sealed class SetupPlan
{
    /// <summary>The name of the first budget month, such as "March 2024". Only applied while nothing has been set up yet.</summary>
    public string? FirstMonthLabel { get; set; }

    public List<SetupAccount> Accounts { get; } = [];
    public List<SetupGroup> Groups { get; } = [];
    public List<SetupCounterparty> Counterparties { get; } = [];
}

/// <summary>What applying a plan actually added. Things that already existed are not counted.</summary>
public sealed record SetupResult(int Accounts, int Groups, int Items, int Counterparties);
