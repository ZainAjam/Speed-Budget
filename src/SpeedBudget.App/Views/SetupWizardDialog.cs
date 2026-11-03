using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SpeedBudget.Core;
using SpeedBudget.Data;
using Windows.System;

namespace SpeedBudget.App.Views;

/// <summary>
/// Guides the first-time setup: pick a starting template, then tick the accounts, budget groups and items, and common
/// counterparties you want. Nothing is saved until the last step, and re-running it only adds what is missing.
/// </summary>
public sealed class SetupWizardDialog : ContentDialog
{
    private static readonly string[] StepTitles = ["Start", "Bank accounts", "Budget groups and items", "Counterparties", "Ready"];

    private readonly ContentControl _body = new();
    private readonly ScrollViewer _scroll;
    private readonly Dictionary<string, TextBox> _focusTargets = [];
    private string _template = "Household";
    private string? _renderedTemplate;
    private SetupPlan _plan = new();
    private int _step;
    private string? _refocus;
    private string _error = "";

    /// <summary>What was added, or null if the wizard was cancelled.</summary>
    public SetupResult? Result { get; private set; }

    public SetupWizardDialog()
    {
        Resources["ContentDialogMaxWidth"] = 820.0;
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;
        PrimaryButtonClick += Primary_Click;
        SecondaryButtonClick += (_, args) =>
        {
            args.Cancel = true;
            Go(_step - 1);
        };

        _scroll = new ScrollViewer { Content = _body, MaxHeight = 460, Width = 760, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Content = _scroll;
        Go(0);
    }

    private void Go(int step)
    {
        // Choosing a different template (or arriving the first time) starts from that template's lists.
        if (step > 0 && _renderedTemplate != _template)
        {
            _plan = SetupTemplates.Create(_template);
            _plan.Counterparties.AddRange(SetupTemplates.CommonCounterparties());
            _renderedTemplate = _template;
        }
        _step = step;
        _error = "";
        Render();
        _scroll.ChangeView(0, 0, null, disableAnimation: true);
    }

    private void Render()
    {
        Title = $"Set up your budget: {StepTitles[_step]}  ({_step + 1} of {StepTitles.Length})";
        SecondaryButtonText = _step == 0 ? "" : "Back";
        PrimaryButtonText = _step switch { 0 => "Next", 4 => "Set up my budget", _ => "Next" };
        _focusTargets.Clear();

        var panel = new StackPanel { Spacing = 12 };
        switch (_step)
        {
            case 0: BuildStart(panel); break;
            case 1: BuildAccounts(panel); break;
            case 2: BuildBudget(panel); break;
            case 3: BuildCounterparties(panel); break;
            default: BuildSummary(panel); break;
        }
        if (_error != "") panel.Children.Add(new TextBlock { Text = _error, TextWrapping = TextWrapping.Wrap, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"] });
        _body.Content = panel;

        if (_refocus is not null && _focusTargets.TryGetValue(_refocus, out var box)) box.Focus(FocusState.Programmatic);
        _refocus = null;
    }

    private async void Primary_Click(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (_step < StepTitles.Length - 1)
        {
            args.Cancel = true;
            Go(_step + 1);
            return;
        }

        var deferral = args.GetDeferral();
        try
        {
            Result = await AppServices.Store.ApplySetupAsync(_plan);
            AppServices.RaiseAccountsChanged();
        }
        catch (Exception ex)
        {
            args.Cancel = true;
            _error = $"Could not save: {ex.Message}";
            Render();
        }
        deferral.Complete();
    }

    // ---- Step 1: the starting point ----

    private void BuildStart(StackPanel panel)
    {
        panel.Children.Add(Text("Welcome to Speed Budget. This takes a minute and sets up the lists you will pick from every day. Nothing is saved until the last step, and you can change all of it later."));
        panel.Children.Add(Text("Where would you like to start?", strong: true));

        var choices = new RadioButtons { Header = null };
        foreach (var (name, description) in SetupTemplates.All)
        {
            choices.Items.Add(new RadioButton
            {
                Tag = name, IsChecked = name == _template,
                Content = new StackPanel { Children = { Text(name, strong: true), Text(description, caption: true) } },
            });
        }
        choices.SelectionChanged += (_, _) =>
        {
            if (choices.SelectedItem is RadioButton { Tag: string name }) _template = name;
        };
        panel.Children.Add(choices);
        panel.Children.Add(Text("Choosing a different starting point later in the wizard replaces the lists you have edited so far.", caption: true));
    }

    // ---- Step 2: accounts ----

    private void BuildAccounts(StackPanel panel)
    {
        panel.Children.Add(Text("Add each bank account you want to use. A budget account is money you budget with, such as a cheque or savings account. A tracked account sits outside your budget, such as a retirement fund or a home loan: its transactions are recorded but need no budget item. Opening balance is what the account holds today."));

        foreach (var account in _plan.Accounts.ToList())
        {
            var name = new TextBox { Header = "Name", Text = account.Name, PlaceholderText = "e.g. Cheque account", Width = 190 };
            name.TextChanged += (_, _) => account.Name = name.Text;

            var type = new ComboBox { Header = "Type", Width = 120, ItemsSource = Enum.GetValues<AccountType>(), SelectedItem = account.Type };
            type.SelectionChanged += (_, _) => { if (type.SelectedItem is AccountType t) account.Type = t; };

            var balance = new NumberBox
            {
                Header = "Opening balance", Width = 130, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden, PlaceholderText = "0.00",
                Value = account.OpeningBalance == 0 ? double.NaN : (double)AppServices.ToRands(account.OpeningBalance),
            };
            Money.SetFormat(balance, true);
            balance.ValueChanged += (_, _) => account.OpeningBalance = AppServices.ToCents(balance.Value);

            var onBudget = new CheckBox { Content = "Budget account", IsChecked = account.IsOnBudget, MinWidth = 0, VerticalAlignment = VerticalAlignment.Bottom };
            onBudget.Click += (_, _) => account.IsOnBudget = onBudget.IsChecked == true;

            var remove = IconButton("Remove this account", "", () => { _plan.Accounts.Remove(account); Render(); });

            panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { name, type, balance, onBudget, remove } });
        }

        var add = new Button { Content = "Add an account" };
        add.Click += (_, _) =>
        {
            _plan.Accounts.Add(new SetupAccount());
            Render();
        };
        panel.Children.Add(add);
    }

    // ---- Step 3: groups and items ----

    private void BuildBudget(StackPanel panel)
    {
        panel.Children.Add(Text("Tick the budget items you want. A budget group collects related items, and every item is something you set money aside for each month. Income groups hold where money comes in. You can add your own, and rename, move or delete anything later under Budget Items."));

        foreach (var group in _plan.Groups.ToList())
        {
            var header = new Grid { ColumnSpacing = 8 };
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(Text(group.Kind == ItemKind.Income ? $"{group.Name} (income)" : group.Name, strong: true));
            var removeGroup = IconButton("Remove this group", "", () => { _plan.Groups.Remove(group); Render(); });
            Grid.SetColumn(removeGroup, 1);
            header.Children.Add(removeGroup);
            panel.Children.Add(header);

            var boxes = group.Items.Select(item =>
            {
                var box = new CheckBox { Content = string.IsNullOrEmpty(item.Emoji) ? item.Name : $"{item.Emoji} {item.Name}", IsChecked = item.Selected, MinWidth = 0 };
                box.Click += (_, _) => item.Selected = box.IsChecked == true;
                return (UIElement)box;
            });
            panel.Children.Add(Columns(boxes, 3));

            var key = "item:" + group.Name;
            var addItem = AddBox("Add an item to " + group.Name, key, name =>
            {
                if (group.Items.Any(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase))) return;
                group.Items.Add(new SetupItem(name));
            });
            panel.Children.Add(addItem);
        }

        var kind = new ComboBox { Width = 120, ItemsSource = new[] { "Expense", "Income" }, SelectedIndex = 0, VerticalAlignment = VerticalAlignment.Bottom };
        var group2 = new TextBox { PlaceholderText = "Add a group, e.g. Pets", Width = 240, VerticalAlignment = VerticalAlignment.Bottom };
        _focusTargets["group"] = group2;
        void AddGroup()
        {
            var name = group2.Text.Trim();
            if (name == "" || _plan.Groups.Any(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase))) return;
            _plan.Groups.Add(new SetupGroup(name, kind.SelectedIndex == 1 ? ItemKind.Income : ItemKind.Expense));
            _refocus = "item:" + name;
            Render();
        }
        group2.KeyDown += (_, e) => { if (e.Key == VirtualKey.Enter) { e.Handled = true; AddGroup(); } };
        var addGroup = new Button { Content = "Add group", VerticalAlignment = VerticalAlignment.Bottom };
        addGroup.Click += (_, _) => AddGroup();
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0), Children = { group2, kind, addGroup } });
    }

    // ---- Step 4: counterparties ----

    private void BuildCounterparties(StackPanel panel)
    {
        panel.Children.Add(Text("A counterparty is who a transaction is with: the shop, person or business you paid, or who paid you. Tick the common ones you use so they are in the list from the first day. New ones are also added automatically as you type them into a transaction."));

        var boxes = _plan.Counterparties.Select(c =>
        {
            var box = new CheckBox { Content = c.Name, IsChecked = c.Selected, MinWidth = 0 };
            box.Click += (_, _) => c.Selected = box.IsChecked == true;
            return (UIElement)box;
        });
        panel.Children.Add(Columns(boxes, 4));

        panel.Children.Add(AddBox("Add your own counterparty", "counterparty", name =>
        {
            if (_plan.Counterparties.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) is { } existing) existing.Selected = true;
            else _plan.Counterparties.Add(new SetupCounterparty(name));
        }));
    }

    // ---- Step 5: summary ----

    private void BuildSummary(StackPanel panel)
    {
        var accounts = _plan.Accounts.Where(a => a.Name.Trim() != "").ToList();
        var groups = _plan.Groups.Where(g => g.Items.Any(i => i.Selected)).ToList();
        var items = groups.Sum(g => g.Items.Count(i => i.Selected));
        var counterparties = _plan.Counterparties.Count(c => c.Selected);

        panel.Children.Add(Text("Here is what will be set up:", strong: true));
        panel.Children.Add(Text($"•  {Plural(accounts.Count, "bank account")}" + (accounts.Count > 0 ? $": {string.Join(", ", accounts.Select(a => a.Name.Trim()))}" : "")));
        panel.Children.Add(Text($"•  {Plural(groups.Count, "budget group")} with {Plural(items, "budget item")}"));
        panel.Children.Add(Text($"•  {Plural(counterparties, "counterparty", "counterparties")}"));
        panel.Children.Add(Text("Anything that already exists under the same name is left as it is, so nothing is duplicated. After this you can set each month's budgeted amounts on the Budget screen.", caption: true));
    }

    // ---- Small UI helpers ----

    private static string Plural(int n, string singular, string? plural = null) => $"{n} {(n == 1 ? singular : plural ?? singular + "s")}";

    private static TextBlock Text(string text, bool strong = false, bool caption = false)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        if (strong) block.Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"];
        if (caption)
        {
            block.Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"];
            block.Opacity = 0.7;
        }
        return block;
    }

    private static Button IconButton(string tip, string glyph, Action click)
    {
        var button = new Button { Content = new FontIcon { Glyph = glyph, FontSize = 14 }, VerticalAlignment = VerticalAlignment.Bottom, Background = null };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, tip);
        ToolTipService.SetToolTip(button, tip);
        button.Click += (_, _) => click();
        return button;
    }

    private static Grid Columns(IEnumerable<UIElement> children, int columns)
    {
        var grid = new Grid();
        for (var c = 0; c < columns; c++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        var n = 0;
        foreach (var child in children)
        {
            if (n % columns == 0) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow((FrameworkElement)child, n / columns);
            Grid.SetColumn((FrameworkElement)child, n % columns);
            grid.Children.Add(child);
            n++;
        }
        return grid;
    }

    /// <summary>A text box where Enter (or the Add button) adds what was typed through <paramref name="add"/>, then keeps typing there.</summary>
    private StackPanel AddBox(string placeholder, string key, Action<string> add)
    {
        var box = new TextBox { PlaceholderText = placeholder, Width = 280 };
        _focusTargets[key] = box;
        void Commit()
        {
            var name = box.Text.Trim();
            if (name == "") return;
            add(name);
            _refocus = key;
            Render();
        }
        box.KeyDown += (_, e) => { if (e.Key == VirtualKey.Enter) { e.Handled = true; Commit(); } };
        var button = new Button { Content = "Add" };
        button.Click += (_, _) => Commit();
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 8), Children = { box, button } };
    }
}
