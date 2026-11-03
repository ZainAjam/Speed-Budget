using SpeedBudget.Core;

namespace SpeedBudget.Data;

/// <summary>The starting points offered by the setup wizard. Each call returns a fresh plan that can be edited freely.</summary>
public static class SetupTemplates
{
    public const string Empty = "Start empty";

    public static IReadOnlyList<(string Name, string Description)> All { get; } =
    [
        ("Household", "A typical household: income, home and utilities, daily spending, education, medical, vehicle, debit orders and savings."),
        ("Minimal", "A short list to start from: salary, home, groceries, transport and savings. Add the rest as you go."),
        (Empty, "Nothing pre-filled. You add your own accounts, groups and items."),
    ];

    public static SetupPlan Create(string name)
    {
        var plan = new SetupPlan();
        if (name == Empty) return plan;

        plan.Accounts.Add(new SetupAccount { Name = "Cheque account", Type = AccountType.Cheque, IsOnBudget = true });
        if (name == "Minimal")
        {
            Add(plan, "Income", ItemKind.Income, ("Salary", "💼"));
            Add(plan, "Home", ItemKind.Expense, ("Rent or home loan", "🏠"), ("Utilities", "💡"));
            Add(plan, "Daily spending", ItemKind.Expense, ("Groceries", "🛒"), ("Transport", "🚗"), ("Eating out", "🍽️"));
            Add(plan, "Savings", ItemKind.Expense, ("Savings", "🐷"));
        }
        else
        {
            Add(plan, "Income", ItemKind.Income, ("Salary", "💼"), ("Other income", "💰"));
            Add(plan, "Home & Utilities", ItemKind.Expense, ("Rates", "🏛️"), ("Electricity", "⚡"), ("Water", "💧"), ("Telephone", "☎️"),
                ("Internet", "🌐"), ("Home loan or rent", "🏠"), ("Security", "🔒"));
            Add(plan, "Daily spending", ItemKind.Expense, ("Groceries", "🛒"), ("Daily cash or card expense", "💳"), ("Gifts", "🎁"),
                ("Clothing", "👕"), ("Emergency fund", "🚨"));
            Add(plan, "Education", ItemKind.Expense, ("School fees", "🎓"), ("School accessories", "🎒"));
            Add(plan, "Medical", ItemKind.Expense, ("Doctor", "🩺"), ("Pharmacy", "💊"));
            Add(plan, "Vehicle", ItemKind.Expense, ("Petrol", "⛽"), ("Car licence", "🪪"), ("Car service", "🔧"), ("Car parts", "🛞"),
                ("Traffic fines", "🚓"), ("Lift club", "🚐"));
            Add(plan, "Debit orders", ItemKind.Expense, ("Bank maintenance fee", "🏦"), ("Cellphone", "📱"), ("Insurance", "🛡️"), ("TV licence", "📺"));
            Add(plan, "Savings", ItemKind.Expense, ("Savings", "🐷"));
        }
        return plan;
    }

    /// <summary>Well-known South African counterparties, offered unticked so you only keep the ones you actually use.</summary>
    public static IReadOnlyList<SetupCounterparty> CommonCounterparties() =>
        new[]
        {
            "Woolworths", "Checkers", "Shoprite", "Pick n Pay", "Spar", "Dis-Chem", "Clicks", "Takealot", "Builders Warehouse",
            "Engen", "Shell", "Sasol", "BP", "Eskom", "Municipality", "Telkom", "Vodacom", "MTN", "Cell C", "Rain",
            "Discovery", "Old Mutual", "Sanlam", "Outsurance", "Netflix", "DStv", "Uber", "SARS", "Employer",
        }.Select(n => new SetupCounterparty(n, selected: false)).ToList();

    private static void Add(SetupPlan plan, string group, ItemKind kind, params (string Name, string Emoji)[] items) =>
        plan.Groups.Add(new SetupGroup(group, kind, items.Select(i => new SetupItem(i.Name, i.Emoji))));
}
