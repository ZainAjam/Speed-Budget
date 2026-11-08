using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SpeedBudget.Data;

namespace SpeedBudget.App.Views;

/// <summary>
/// Lists the budgets on this computer. Choosing another one restarts Speed Budget on it; a new budget starts empty, with the
/// setup wizard. A removed budget is moved aside inside the data folder, not deleted.
/// </summary>
public static class BudgetsDialog
{
    private enum Choice { None, Open, New, Rename, Remove }

    /// <summary>Shows the list and carries out what is chosen. Returns when the dialog is closed (or the app has restarted).</summary>
    public static async Task ShowAsync(XamlRoot root, Action budgetsChanged)
    {
        while (true)
        {
            var (choice, selected) = await ListAsync(root);
            try
            {
                switch (choice)
                {
                    case Choice.Open when selected is { } open:
                        if (open.Id != AppServices.CurrentBudget.Id) AppServices.SwitchBudget(open.Id);
                        return;

                    case Choice.New:
                        if (await CreateAndOpenAsync(root)) return;
                        continue;

                    case Choice.Rename when selected is { } rename:
                        if (await AskNameAsync(root, "Rename budget", rename.Name, "Rename") is { } newName)
                        {
                            AppServices.Registry.Rename(rename.Id, newName);
                            if (rename.Id == AppServices.CurrentBudget.Id) AppServices.RefreshCurrentBudget();
                            budgetsChanged();
                        }
                        continue;

                    case Choice.Remove when selected is { } remove:
                        await RemoveAsync(root, remove);
                        continue;

                    default:
                        return;
                }
            }
            catch (Exception ex)
            {
                await new ContentDialog { XamlRoot = root, Title = "That didn't work", Content = ex.Message, CloseButtonText = "OK" }.ShowAsync();
            }
        }
    }

    /// <summary>Asks for a name, makes the budget and opens it (the app restarts). Does nothing if the name is not given.</summary>
    public static async Task NewAsync(XamlRoot root)
    {
        try { await CreateAndOpenAsync(root); }
        catch (Exception ex) { await new ContentDialog { XamlRoot = root, Title = "That didn't work", Content = ex.Message, CloseButtonText = "OK" }.ShowAsync(); }
    }

    private static async Task<bool> CreateAndOpenAsync(XamlRoot root)
    {
        if (await AskNameAsync(root, "New budget", "", "Create and open") is not { } name) return false;
        AppServices.SwitchBudget(AppServices.Registry.Create(name).Id);
        return true;
    }

    private static async Task<(Choice, BudgetEntry?)> ListAsync(XamlRoot root)
    {
        var current = AppServices.CurrentBudget;
        var list = new ListView { SelectionMode = ListViewSelectionMode.Single, MaxHeight = 280 };
        foreach (var budget in AppServices.Registry.Budgets)
        {
            var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Padding = new Thickness(0, 6, 0, 6) };
            label.Children.Add(new TextBlock { Text = budget.Name, VerticalAlignment = VerticalAlignment.Center });
            if (budget.Id == current.Id)
                label.Children.Add(new TextBlock { Text = "open now", Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center });
            list.Items.Add(new ListViewItem { Content = label, Tag = budget });
        }
        list.SelectedIndex = Math.Max(0, AppServices.Registry.Budgets.ToList().FindIndex(b => b.Id == current.Id));

        var choice = Choice.None;
        var dialog = new ContentDialog
        {
            XamlRoot = root, Title = "Your budgets",
            PrimaryButtonText = "Open", SecondaryButtonText = "New budget…", CloseButtonText = "Close", DefaultButton = ContentDialogButton.Primary,
        };
        var rename = new Button { Content = "Rename…" };
        var remove = new Button { Content = "Remove…" };
        rename.Click += (_, _) => { choice = Choice.Rename; dialog.Hide(); };
        remove.Click += (_, _) => { choice = Choice.Remove; dialog.Hide(); };

        BudgetEntry? Selected() => (list.SelectedItem as ListViewItem)?.Tag as BudgetEntry;
        void Refresh()
        {
            var s = Selected();
            dialog.IsPrimaryButtonEnabled = s is not null && s.Id != current.Id;
            rename.IsEnabled = s is not null;
            remove.IsEnabled = s is not null && s.Id != current.Id && AppServices.Registry.Budgets.Count > 1 && s.Folder != ".";
        }
        list.SelectionChanged += (_, _) => Refresh();
        Refresh();

        dialog.Content = new StackPanel
        {
            Spacing = 12, MinWidth = 380,
            Children =
            {
                new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap, Opacity = 0.8,
                    Text = "Each budget has its own accounts, items and transactions. Opening another budget restarts Speed Budget.",
                },
                list,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { rename, remove } },
            },
        };

        var result = await dialog.ShowAsync();
        if (choice != Choice.None) return (choice, Selected());
        return result switch
        {
            ContentDialogResult.Primary => (Choice.Open, Selected()),
            ContentDialogResult.Secondary => (Choice.New, null),
            _ => (Choice.None, null),
        };
    }

    private static async Task RemoveAsync(XamlRoot root, BudgetEntry budget)
    {
        var confirm = new ContentDialog
        {
            XamlRoot = root, Title = $"Remove {budget.Name}?",
            Content = "It disappears from the list. Nothing is deleted: its data is moved to the removed-budgets folder inside your Speed Budget data folder, " +
                      "where it can still be found.",
            PrimaryButtonText = "Remove", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close,
        };
        if (await confirm.ShowAsync() == ContentDialogResult.Primary) AppServices.Registry.Remove(budget.Id);
    }

    private static async Task<string?> AskNameAsync(XamlRoot root, string title, string initial, string primary)
    {
        var box = new TextBox { Header = "Name", Text = initial, MinWidth = 320, MaxLength = 60 };
        var dialog = new ContentDialog
        {
            XamlRoot = root, Title = title, Content = box,
            PrimaryButtonText = primary, CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = initial.Trim().Length > 0,
        };
        box.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = box.Text.Trim().Length > 0;
        dialog.Opened += (_, _) => { box.Focus(FocusState.Programmatic); box.SelectAll(); };
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? box.Text.Trim() : null;
    }
}
