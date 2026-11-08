using SpeedBudget.Core;
using SpeedBudget.Data;

namespace SpeedBudget.Data.Tests;

public sealed class BudgetRegistryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "speedbudget-registry-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* a leftover temp folder is harmless */ }
    }

    [Fact]
    public void A_new_installation_gets_one_empty_budget_in_a_folder_of_its_own()
    {
        var registry = BudgetRegistry.Load(_root);

        var budget = Assert.Single(registry.Budgets);
        Assert.Equal("My budget", budget.Name);
        Assert.Equal(registry.Current, budget);
        Assert.True(Directory.Exists(registry.DirectoryOf(budget)));
        Assert.StartsWith(Path.Combine(_root, "budgets"), registry.DirectoryOf(budget));
    }

    [Fact]
    public async Task The_budget_from_before_there_were_several_moves_into_its_own_folder_with_its_backups_and_data_intact()
    {
        Directory.CreateDirectory(_root);
        using (var old = LocalDatabase.Open(_root))
        {
            var store = new BudgetStore(old);
            var group = await store.AddGroupAsync("Daily", ItemKind.Expense);
            await store.AddItemAsync(group.Id, null, "Food", ItemKind.Expense);
            BackupService.CreateBackup(old, _root);
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var deviceId = LocalDatabase.GetOrCreateDeviceId(_root);

        var registry = BudgetRegistry.Load(_root);

        var budget = Assert.Single(registry.Budgets);
        var folder = registry.DirectoryOf(budget);
        Assert.False(File.Exists(Path.Combine(_root, "budget.db")));
        Assert.False(Directory.Exists(Path.Combine(_root, "backups")));
        Assert.Single(BackupService.List(folder));

        using var moved = LocalDatabase.Open(folder, _root);
        Assert.Equal("Food", Assert.Single(await new BudgetStore(moved).GetItemsAsync()).Name);
        Assert.Equal(deviceId, LocalDatabase.GetOrCreateDeviceId(_root));   // the device id stays with the installation
    }

    [Fact]
    public void The_list_is_remembered_and_opens_on_the_budget_used_last()
    {
        var registry = BudgetRegistry.Load(_root);
        var second = registry.Create("Dad's budget");
        registry.SetCurrent(second.Id);

        var again = BudgetRegistry.Load(_root);
        Assert.Equal(new[] { "My budget", "Dad's budget" }, again.Budgets.Select(b => b.Name));
        Assert.Equal(second.Id, again.Current.Id);
        Assert.NotEqual(again.DirectoryOf(again.Budgets[0]), again.DirectoryOf(again.Budgets[1]));
    }

    [Fact]
    public void Budgets_keep_separate_data()
    {
        var registry = BudgetRegistry.Load(_root);
        var second = registry.Create("Second");

        using (var a = LocalDatabase.Open(registry.DirectoryOf(registry.Budgets[0]), _root))
            new BudgetStore(a).AddGroupAsync("Only in first", ItemKind.Expense).GetAwaiter().GetResult();
        using var b = LocalDatabase.Open(registry.DirectoryOf(second), _root);

        Assert.Empty(new BudgetStore(b).GetGroupsAsync().GetAwaiter().GetResult());
    }

    [Fact]
    public void A_budget_can_be_renamed_but_not_given_an_empty_name()
    {
        var registry = BudgetRegistry.Load(_root);
        registry.Rename(registry.Current.Id, "  Household  ");

        Assert.Equal("Household", BudgetRegistry.Load(_root).Current.Name);
        Assert.Throws<ArgumentException>(() => registry.Rename(registry.Current.Id, "   "));
        Assert.Throws<ArgumentException>(() => registry.Create(""));
    }

    [Fact]
    public void A_removed_budget_is_moved_aside_not_deleted_and_the_last_one_cannot_be_removed()
    {
        var registry = BudgetRegistry.Load(_root);
        var first = registry.Current;
        var second = registry.Create("Second");
        File.WriteAllText(Path.Combine(registry.DirectoryOf(second), "budget.db"), "x");

        Assert.Throws<InvalidOperationException>(() => BudgetRegistry.Load(Path.Combine(_root, "other")).Remove(Guid.NewGuid()));
        registry.SetCurrent(second.Id);
        registry.Remove(second.Id);

        Assert.Equal(first.Id, registry.Current.Id);   // falls back to the remaining budget
        Assert.Single(registry.Budgets);
        Assert.False(Directory.Exists(Path.Combine(_root, second.Folder)));
        Assert.True(File.Exists(Directory.EnumerateFiles(Path.Combine(_root, "removed-budgets"), "budget.db", SearchOption.AllDirectories).Single()));
        Assert.Throws<InvalidOperationException>(() => registry.Remove(first.Id));
    }

    [Fact]
    public void A_lost_list_is_rebuilt_from_the_budget_folders()
    {
        var registry = BudgetRegistry.Load(_root);
        var second = registry.Create("Second");
        using (LocalDatabase.Open(registry.DirectoryOf(registry.Budgets[0]), _root)) { }
        using (LocalDatabase.Open(registry.DirectoryOf(second), _root)) { }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(Path.Combine(_root, "budgets.json"));

        var rebuilt = BudgetRegistry.Load(_root);

        Assert.Equal(2, rebuilt.Budgets.Count);
        Assert.Contains(rebuilt.Budgets, b => b.Id == second.Id);
    }
}
