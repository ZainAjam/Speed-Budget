using SpeedBudget.Core;

namespace SpeedBudget.Data;

/// <summary>An optional starting list of groups and items, adapted from the Action Budget sheet.</summary>
public static class StarterItems
{
    public static readonly IReadOnlyList<(string Group, ItemKind Kind, string[] Items)> Groups = new (string, ItemKind, string[])[]
    {
        ("Income", ItemKind.Income, new[] { "Salary", "Other income" }),
        ("Home & Utilities", ItemKind.Expense, new[] { "Rates", "Electricity", "Water", "Telephone", "Internet", "Home loan or rent", "Security" }),
        ("Daily spending", ItemKind.Expense, new[] { "Groceries", "Daily cash or card expense", "Gifts", "Clothing", "Emergency fund" }),
        ("Education", ItemKind.Expense, new[] { "School fees", "School accessories" }),
        ("Medical", ItemKind.Expense, new[] { "Doctor", "Pharmacy" }),
        ("Vehicle", ItemKind.Expense, new[] { "Petrol", "Car licence", "Car service", "Car parts", "Traffic fines", "Lift club" }),
        ("Debit orders", ItemKind.Expense, new[] { "Bank maintenance fee", "Cellphone", "Insurance", "TV licence" }),
        ("Savings", ItemKind.Expense, new[] { "Savings" }),
    };
}
