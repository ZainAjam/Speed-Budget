using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using SpeedBudget.App.ViewModels;
using SpeedBudget.Data;

namespace SpeedBudget.App.Views;

/// <summary>A column offered in a mapping drop-down: its index (-1 for "none") and a label with a sample of what is in it.</summary>
public sealed record ColumnChoice(int Index, string Label);

/// <summary>
/// A line of the file in the reconciliation. When the table has a transaction that looks like it, <see cref="Matched"/> says whether the user
/// confirms that it is the same one (it is then cleared). A line with no match, or whose match the user rejected, can be imported as new.
/// </summary>
public sealed class ReconcileRow : INotifyPropertyChanged
{
    private bool _matched, _import;
    public required BankMatch Match { get; init; }

    /// <summary>The transaction that looks the same, or null when the table has none.</summary>
    public TransactionEntity? Existing => Match.Existing;
    public bool HasMatch => Existing is not null;

    /// <summary>Already cleared: confirming the match changes nothing.</summary>
    public bool AlreadyCleared => Existing is { IsCleared: true, IsUndated: false } || Existing is { IsReconciled: true };

    /// <summary>The user says the file's line and the table's transaction are the same one.</summary>
    public bool Matched { get => _matched; set => Set(ref _matched, value); }

    /// <summary>To be added as a new transaction (only for a line that has no confirmed match).</summary>
    public bool Import { get => _import; set => Set(ref _import, value); }

    /// <summary>The counterparty the new transaction gets: the file's description to begin with, or blank, and the user can pick or type another.</summary>
    public string Payee { get; set; } = "";
    public IReadOnlyList<string> Suggestions { get; init; } = [];
    public IReadOnlyDictionary<string, string> Icons { get; init; } = new Dictionary<string, string>();

    public bool CanConfirm => HasMatch && !AlreadyCleared;
    public double MatchOpacity => AlreadyCleared ? 0.55 : 1;

    // The file's side
    public string DateText => Match.Line.Date.ToString("d MMM yyyy");
    public string Description => Match.Line.Description;
    public string AmountText => Format.Money(Match.Line.Amount);
    public string MemoText => Match.Line.Memo ?? "";

    // The table's side
    public string TableDateText => Existing is null ? "" : Existing.IsUndated ? "No date yet" : Existing.Date.ToString("d MMM yyyy");
    public string TablePayee => Existing?.Payee ?? "";
    public string TableAmountText => Existing is null ? "" : Format.Money(Existing.Amount);
    public string TableNote => Existing is null ? "" : AlreadyCleared ? "already cleared" : Existing.IsUndated ? "expected, no date" : Existing.Date > DateOnly.FromDateTime(DateTime.Today) ? "scheduled" : "";

    private void Set(ref bool field, bool value, [CallerMemberName] string? name = null)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// The import wizard. Step 1: which file table, which account, and which column is what (guessed, and remembered for the account).
/// Step 2: the reconciliation: lines the table already has are shown as to be cleared, lines it does not have as to be added. Nothing is
/// changed until the user confirms on the second step.
/// </summary>
public sealed class ImportWizardDialog : ContentDialog
{
    private readonly TransactionsViewModel _vm;
    private readonly IReadOnlyList<BankTable> _tables;
    private readonly IReadOnlyList<AccountOption> _accounts;

    private readonly ComboBox _tableBox = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _accountBox = new() { HorizontalAlignment = HorizontalAlignment.Stretch, DisplayMemberPath = nameof(AccountOption.Name) };
    private readonly NumberBox _headerRows = new() { Minimum = 0, Maximum = 50, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline, Width = 180, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly ComboBox _dateBox = Choice(), _formatBox = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _descriptionBox = Choice(), _memoBox = Choice();
    private readonly RadioButtons _moneyMode = new() { Items = { "One amount column", "Separate money out and money in columns" }, SelectedIndex = 0, MaxColumns = 2 };
    private readonly ComboBox _amountBox = Choice(), _debitBox = Choice(), _creditBox = Choice();
    private readonly CheckBox _negativeOut = new() { Content = "A negative amount is money out", IsChecked = true };
    private readonly CheckBox _rememberBox = new() { Content = "Remember this setup and skip this step next time (you can still change it from the reconciliation)", IsChecked = false };
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _rawPreview = new() { FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"), FontSize = 12, Opacity = 0.8, TextWrapping = TextWrapping.NoWrap };
    private readonly StackPanel _mapStep = new() { Spacing = 10 };
    private readonly Grid _reconcileStep = new() { Visibility = Visibility.Collapsed, RowSpacing = 8 };
    private readonly TextBlock _reconcileSummary = new() { TextWrapping = TextWrapping.Wrap };

    private BankTable _table;
    private BankParse _parse = new([], []);
    private bool _loading;
    private bool _onReconcile;
    private List<ReconcileRow> _rows = [];
    private BankFormat? _format;

    private static ComboBox Choice() => new() { HorizontalAlignment = HorizontalAlignment.Stretch, DisplayMemberPath = nameof(ColumnChoice.Label) };

    public ImportWizardDialog(TransactionsViewModel vm, IReadOnlyList<BankTable> tables, string fileName)
    {
        _vm = vm;
        _tables = tables;
        _table = tables[0];
        _accounts = vm.ImportAccounts();
        Title = "Import transactions";
        PrimaryButtonText = "Next";
        CloseButtonText = "Cancel";
        DefaultButton = ContentDialogButton.Primary;
        IsPrimaryButtonEnabled = false;
        Resources["ContentDialogMaxWidth"] = 980.0;
        Resources["ContentDialogMinWidth"] = 640.0;

        _formatBox.ItemsSource = BankFile.DateFormats;
        foreach (var t in tables) _tableBox.Items.Add(t.Name);
        _tableBox.SelectedIndex = 0;
        _accountBox.ItemsSource = _accounts;
        _accountBox.SelectedItem = vm.AccountId is { } id ? _accounts.FirstOrDefault(a => a.Id == id) : _accounts.FirstOrDefault();

        BuildMapStep(fileName);
        BuildReconcileStep();
        Content = new Grid { Children = { _mapStep, _reconcileStep } };

        PrimaryButtonClick += async (_, e) =>
        {
            var deferral = e.GetDeferral();
            try
            {
                e.Cancel = true;
                if (!_onReconcile) await GoToReconcileAsync();
                else if (await ApplyAsync()) { e.Cancel = false; }
            }
            finally { deferral.Complete(); }
        };
        SecondaryButtonClick += (_, e) => { e.Cancel = true; ShowStep(reconcile: false); };

        _tableBox.SelectionChanged += async (_, _) => { if (_tableBox.SelectedIndex >= 0 && !_loading) { _table = _tables[_tableBox.SelectedIndex]; await SetUpAsync(); } };
        _accountBox.SelectionChanged += async (_, _) => { if (!_loading) await SetUpAsync(); };
        _headerRows.ValueChanged += (_, _) => { if (!_loading) { FillColumnChoices(); Reparse(); } };
        foreach (var box in new[] { _dateBox, _formatBox, _descriptionBox, _memoBox, _amountBox, _debitBox, _creditBox })
            box.SelectionChanged += (_, _) => { if (!_loading) Reparse(); };
        _moneyMode.SelectionChanged += (_, _) => { if (!_loading) { ShowMoneyMode(); Reparse(); } };
        _negativeOut.Click += (_, _) => Reparse();
        Opened += async (_, _) => await SetUpAsync(auto: true);
    }

    // ---- Step 1: the file and its columns ----

    private void BuildMapStep(string fileName)
    {
        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var r = 0;
        void Add(string label, UIElement control, string? tip = null)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.85 };
            Grid.SetRow(text, r); Grid.SetRow((FrameworkElement)control, r); Grid.SetColumn((FrameworkElement)control, 1);
            if (tip is not null) ToolTipService.SetToolTip((DependencyObject)control, tip);
            grid.Children.Add(text);
            grid.Children.Add(control);
            r++;
        }
        if (_tables.Count > 1) Add("Sheet or file", _tableBox, "The file holds several tables: pick the one with the transactions");
        if (_vm.AccountId is null) Add("Into account", _accountBox);
        Add("Heading rows to skip", _headerRows, "The rows at the top that are headings, not transactions");
        Add("Date", _dateBox);
        Add("Date layout", _formatBox, "How the dates are written in the file. dd is the day, MM the month, yyyy the year");
        Add("Counterparty", _descriptionBox, "The column with the description or the name of who was paid. Choose (none) to import with a blank counterparty and pick one on the next step");
        Add("Memo", _memoBox);
        Add("Money", _moneyMode);
        Add("Amount", _amountBox);
        Add("Money out", _debitBox);
        Add("Money in", _creditBox);
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(_negativeOut, r); Grid.SetColumn(_negativeOut, 1);
        grid.Children.Add(_negativeOut);

        _mapStep.Children.Add(new TextBlock { Text = $"{fileName}: say which column holds what. The columns are guessed from the file; correct any that are wrong.", TextWrapping = TextWrapping.Wrap, Opacity = 0.8 });
        _mapStep.Children.Add(grid);
        _mapStep.Children.Add(_rememberBox);
        _mapStep.Children.Add(_summary);
        _mapStep.Children.Add(new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _rawPreview });
    }

    private static string ColumnName(int c) => c < 26 ? ((char)('A' + c)).ToString() : $"{(char)('A' + c / 26 - 1)}{(char)('A' + c % 26)}";

    private void FillColumnChoices()
    {
        var wasLoading = _loading;
        _loading = true;
        var header = (int)(double.IsNaN(_headerRows.Value) ? 0 : _headerRows.Value);
        var sampleRow = Math.Min(header, Math.Max(_table.Rows.Count - 1, 0));
        var columns = Math.Max(_table.Columns, 1);
        List<ColumnChoice> Choices(bool optional)
        {
            var list = new List<ColumnChoice>();
            if (optional) list.Add(new ColumnChoice(-1, "(none)"));
            for (var c = 0; c < columns; c++)
            {
                var heading = header > 0 ? _table.Cell(header - 1, c) : "";
                var sample = _table.Cell(sampleRow, c);
                var label = $"{ColumnName(c)}" + (heading != "" ? $" · {Trim(heading)}" : "") + (sample != "" ? $"   e.g. {Trim(sample)}" : "");
                list.Add(new ColumnChoice(c, label));
            }
            return list;
        }
        static string Trim(string s) => s.Length <= 28 ? s : s[..27] + "…";
        void Fill(ComboBox box, bool optional, int select)
        {
            var items = Choices(optional);
            box.ItemsSource = items;
            box.SelectedItem = items.FirstOrDefault(i => i.Index == select) ?? items.FirstOrDefault();
        }
        var f = _format;
        Fill(_dateBox, false, f?.DateColumn ?? 0);
        Fill(_descriptionBox, true, f?.DescriptionColumn ?? 1);
        Fill(_memoBox, true, f?.MemoColumn ?? -1);
        Fill(_amountBox, false, f is { AmountColumn: >= 0 } ? f.AmountColumn : Math.Min(2, columns - 1));
        Fill(_debitBox, false, f is { DebitColumn: >= 0 } ? f.DebitColumn : Math.Min(2, columns - 1));
        Fill(_creditBox, false, f is { CreditColumn: >= 0 } ? f.CreditColumn : Math.Min(3, columns - 1));
        _formatBox.SelectedItem = BankFile.DateFormats.Contains(f?.DateFormat ?? "") ? f!.DateFormat : BankFile.DateFormats[0];
        _loading = wasLoading;
    }

    private void ShowMoneyMode()
    {
        var split = _moneyMode.SelectedIndex == 1;
        SetRowVisible(_amountBox, !split);
        SetRowVisible(_negativeOut, !split);
        SetRowVisible(_debitBox, split);
        SetRowVisible(_creditBox, split);
    }

    /// <summary>Hides or shows a control and the label beside it.</summary>
    private static void SetRowVisible(FrameworkElement control, bool visible)
    {
        control.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (control.Parent is Grid grid)
            foreach (var child in grid.Children.OfType<TextBlock>().Where(t => Grid.GetRow(t) == Grid.GetRow(control) && Grid.GetColumn(t) == 0))
                child.Visibility = control.Visibility;
    }

    private Guid? AccountId => _vm.AccountId ?? (_accountBox.SelectedItem as AccountOption)?.Id;

    /// <summary>Takes the layout the account used before, or else a guess from the file, and shows it.</summary>
    private bool _usedSaved;

    private async Task SetUpAsync(bool auto = false)
    {
        _loading = true;
        try
        {
            BankFormat? format = null;
            _usedSaved = false;
            if (AccountId is { } id)
            {
                format = await _vm.GetBankFormatAsync(id);
                _rememberBox.IsChecked = await _vm.GetBankSkipSetupAsync(id);
            }
            if (format is not null && format.HeaderRows < _table.Rows.Count && BankFile.Parse(_table, format).Lines.Count > 0) _usedSaved = true;
            else format = BankFile.Detect(_table);
            _format = format;
            _headerRows.Value = format?.HeaderRows ?? 0;
            FillColumnChoices();
            _moneyMode.SelectedIndex = format is { AmountColumn: < 0 } ? 1 : 0;
            _negativeOut.IsChecked = format?.NegativeIsOut ?? true;
            ShowMoneyMode();
        }
        finally { _loading = false; }
        Reparse();

        // The setup was remembered for this account and still reads this file: go straight to the reconciliation.
        if (auto && _rememberBox.IsChecked == true && _usedSaved && _tables.Count == 1 && _vm.AccountId is not null && _parse.Lines.Count > 0)
            await GoToReconcileAsync();
    }

    /// <summary>The layout as it is set in the boxes now.</summary>
    private BankFormat CurrentFormat()
    {
        int Index(ComboBox box) => (box.SelectedItem as ColumnChoice)?.Index ?? -1;
        var split = _moneyMode.SelectedIndex == 1;
        return new BankFormat((int)(double.IsNaN(_headerRows.Value) ? 0 : _headerRows.Value), Index(_dateBox), _formatBox.SelectedItem as string ?? BankFile.DateFormats[0], Index(_descriptionBox),
            split ? -1 : Index(_amountBox), split ? Index(_debitBox) : -1, split ? Index(_creditBox) : -1, _negativeOut.IsChecked == true, Index(_memoBox));
    }

    private void Reparse()
    {
        _format = CurrentFormat();
        _parse = BankFile.Parse(_table, _format);
        var text = $"{_parse.Lines.Count} transaction{(_parse.Lines.Count == 1 ? "" : "s")} read";
        if (_parse.Lines.Count > 0)
        {
            var first = _parse.Lines.Min(l => l.Date);
            var last = _parse.Lines.Max(l => l.Date);
            text += $", from {first:d MMM yyyy} to {last:d MMM yyyy}; money in {Format.Money(_parse.Lines.Where(l => l.Amount > 0).Sum(l => l.Amount))}, money out {Format.Money(-_parse.Lines.Where(l => l.Amount < 0).Sum(l => l.Amount))}.";
        }
        else text += ". Check the columns, the date layout and the number of heading rows.";
        if (_parse.Problems.Count > 0) text += $"\n{_parse.Problems.Count} line{(_parse.Problems.Count == 1 ? "" : "s")} cannot be read and will be left out:\n{ExcelFiles.ProblemText(_parse.Problems.Take(3).ToList())}";
        _summary.Text = text;

        var preview = _table.Rows.Skip(Math.Max(0, _format.HeaderRows - 1)).Take(5).Select(r => string.Join("  |  ", r.Select(c => c.Length > 22 ? c[..21] + "…" : c)));
        _rawPreview.Text = string.Join("\n", preview);
        IsPrimaryButtonEnabled = !_onReconcile && _parse.Lines.Count > 0 && AccountId is not null;
    }

    // ---- Step 2: the reconciliation ----

    private readonly TextBlock _matchHead = new() { Text = "Already in the table", FontSize = 15 }, _newHead = new() { Text = "Not in the table", FontSize = 15 };
    private readonly Pivot _pivot = new();
    private readonly PivotItem _matchTab = new();
    private readonly PivotItem _newTab = new();
    private readonly ListView _matchList = new() { SelectionMode = ListViewSelectionMode.None, MinHeight = 260, MaxHeight = 330 };
    private readonly ListView _newList = new() { SelectionMode = ListViewSelectionMode.None, MinHeight = 260, MaxHeight = 330 };
    private readonly System.Collections.ObjectModel.ObservableCollection<ReconcileRow> _newRows = new();
    private readonly TextBlock _matchHelp = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
    private readonly TextBlock _newHelp = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };

    private static string Template(string columns, string cells, string check) => $$"""
        <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:local="using:SpeedBudget.App.Views">
          <Grid ColumnSpacing="10" Opacity="{Binding MatchOpacity}">
            <Grid.ColumnDefinitions>{{columns}}</Grid.ColumnDefinitions>
            {{check}}
            {{cells}}
          </Grid>
        </DataTemplate>
        """;

    private void BuildReconcileStep()
    {
        _reconcileStep.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _reconcileStep.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _reconcileStep.Children.Add(_reconcileSummary);

        const string itemStyle = """
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ListViewItem">
              <Setter Property="MinHeight" Value="40"/>
              <Setter Property="Padding" Value="12,0,12,0"/>
              <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
            </Style>
            """;

        // Tab 1: the table's transaction on the left, the file's line on the right, and a tick to confirm they are the same one.
        const string matchColumns = """<ColumnDefinition Width="32"/><ColumnDefinition Width="92"/><ColumnDefinition Width="*"/><ColumnDefinition Width="92"/><ColumnDefinition Width="28"/><ColumnDefinition Width="92"/><ColumnDefinition Width="*"/><ColumnDefinition Width="92"/>""";
        _matchList.ItemTemplate = (DataTemplate)XamlReader.Load(Template(matchColumns, """
            <TextBlock Grid.Column="1" Text="{Binding TableDateText}" VerticalAlignment="Center"/>
            <StackPanel Grid.Column="2" VerticalAlignment="Center">
              <TextBlock Text="{Binding TablePayee}" TextTrimming="CharacterEllipsis"/>
              <TextBlock Text="{Binding TableNote}" FontSize="11" Opacity="0.7"/>
            </StackPanel>
            <TextBlock Grid.Column="3" Text="{Binding TableAmountText}" HorizontalAlignment="Right" VerticalAlignment="Center"/>
            <FontIcon Grid.Column="4" Glyph="&#xE76C;" FontSize="12" Opacity="0.6" VerticalAlignment="Center" HorizontalAlignment="Center"/>
            <TextBlock Grid.Column="5" Text="{Binding DateText}" VerticalAlignment="Center"/>
            <TextBlock Grid.Column="6" Text="{Binding Description}" TextTrimming="CharacterEllipsis" VerticalAlignment="Center"/>
            <TextBlock Grid.Column="7" Text="{Binding AmountText}" HorizontalAlignment="Right" VerticalAlignment="Center"/>
            """, """<CheckBox IsChecked="{Binding Matched, Mode=TwoWay}" IsEnabled="{Binding CanConfirm}" MinWidth="0" Padding="0" VerticalAlignment="Center" ToolTipService.ToolTip="Tick if these are the same transaction"/>"""));
        _matchList.ItemContainerStyle = (Style)XamlReader.Load(itemStyle);
        var matchHeading = (Grid)XamlReader.Load($$"""
            <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" ColumnSpacing="10" Padding="12,0,12,0" Opacity="0.75">
              <Grid.ColumnDefinitions>{{matchColumns}}</Grid.ColumnDefinitions>
              <TextBlock Grid.Column="1" Grid.ColumnSpan="3" Text="In the transaction table" FontWeight="SemiBold"/>
              <TextBlock Grid.Column="5" Grid.ColumnSpan="3" Text="In the import file" FontWeight="SemiBold"/>
            </Grid>
            """);
        var matchPanel = new Grid { RowSpacing = 6 };
        for (var i = 0; i < 3; i++) matchPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        matchPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var matchBar = SelectBar(on => Bulk(() => { foreach (var r in _rows.Where(r => r.CanConfirm)) { r.Matched = on; if (!on) r.Import = true; } }));
        Grid.SetRow(matchBar, 1); Grid.SetRow(matchHeading, 2); Grid.SetRow(_matchList, 3);
        matchPanel.Children.Add(_matchHelp); matchPanel.Children.Add(matchBar); matchPanel.Children.Add(matchHeading); matchPanel.Children.Add(_matchList);
        _matchTab.Content = matchPanel;

        // Tab 2: lines of the file that the table does not have, to pick which ones to import.
        const string newColumns = """<ColumnDefinition Width="32"/><ColumnDefinition Width="100"/><ColumnDefinition Width="*"/><ColumnDefinition Width="*"/><ColumnDefinition Width="110"/>""";
        _newList.ItemTemplate = (DataTemplate)XamlReader.Load(Template(newColumns, """
            <TextBlock Grid.Column="1" Text="{Binding DateText}" VerticalAlignment="Center"/>
            <local:PayeePicker Grid.Column="2" ItemsSource="{Binding Suggestions}" Icons="{Binding Icons}" SelectedPayee="{Binding Payee, Mode=TwoWay}" AllowEdit="False" HorizontalAlignment="Stretch" VerticalAlignment="Center" />
            <TextBlock Grid.Column="3" Text="{Binding MemoText}" TextTrimming="CharacterEllipsis" Opacity="0.7" VerticalAlignment="Center"/>
            <TextBlock Grid.Column="4" Text="{Binding AmountText}" HorizontalAlignment="Right" VerticalAlignment="Center"/>
            """, """<CheckBox IsChecked="{Binding Import, Mode=TwoWay}" MinWidth="0" Padding="0" VerticalAlignment="Center" ToolTipService.ToolTip="Tick to add this as a new transaction"/>"""));
        _newList.ItemContainerStyle = (Style)XamlReader.Load(itemStyle);
        _newList.ItemsSource = _newRows;
        var newHeading = (Grid)XamlReader.Load($$"""
            <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" ColumnSpacing="10" Padding="12,0,12,0" Opacity="0.75">
              <Grid.ColumnDefinitions>{{newColumns}}</Grid.ColumnDefinitions>
              <TextBlock Grid.Column="1" Text="Date" FontWeight="SemiBold"/>
              <TextBlock Grid.Column="2" Text="Counterparty" FontWeight="SemiBold"/>
              <TextBlock Grid.Column="3" Text="Memo" FontWeight="SemiBold"/>
              <TextBlock Grid.Column="4" Text="Amount" FontWeight="SemiBold" HorizontalAlignment="Right"/>
            </Grid>
            """);
        var newPanel = new Grid { RowSpacing = 6 };
        for (var i = 0; i < 3; i++) newPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        newPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var newBar = SelectBar(on => Bulk(() => { foreach (var r in _newRows) r.Import = on; }));
        Grid.SetRow(newBar, 1); Grid.SetRow(newHeading, 2); Grid.SetRow(_newList, 3);
        newPanel.Children.Add(_newHelp); newPanel.Children.Add(newBar); newPanel.Children.Add(newHeading); newPanel.Children.Add(_newList);
        _newTab.Content = newPanel;

        _matchTab.Header = _matchHead; _newTab.Header = _newHead;
        var pivot = _pivot;
        pivot.Items.Add(_matchTab); pivot.Items.Add(_newTab);
        Grid.SetRow(pivot, 1);
        _reconcileStep.Children.Add(pivot);
    }

    private void ShowStep(bool reconcile)
    {
        _onReconcile = reconcile;
        _mapStep.Visibility = reconcile ? Visibility.Collapsed : Visibility.Visible;
        _reconcileStep.Visibility = reconcile ? Visibility.Visible : Visibility.Collapsed;
        Title = reconcile ? "Import transactions: reconcile" : "Import transactions";
        PrimaryButtonText = reconcile ? "Apply" : "Next";
        SecondaryButtonText = reconcile ? "Edit import settings" : "";
        if (reconcile) UpdateSummary(); else Reparse();
    }

    private async Task GoToReconcileAsync()
    {
        if (AccountId is not { } account) return;
        var matches = await _vm.MatchBankLinesAsync(account, _parse.Lines);
        var payees = _vm.Payees.ToList();
        _rows = matches.Select(m => new ReconcileRow { Match = m, Payee = m.Line.Description, Suggestions = payees, Icons = _vm.PayeeIconsForImport }).ToList();
        foreach (var row in _rows)
        {
            row.Matched = row.CanConfirm;   // a match is proposed; the user confirms or rejects it
            row.Import = !row.HasMatch;
            row.PropertyChanged += Row_Changed;
        }
        FillLists();
        ShowStep(reconcile: true);
        _pivot.SelectedIndex = _rows.Any(r => r.HasMatch) ? 0 : 1;   // nothing to check against the table: open on the lines to import
    }

    private bool _filling;

    private bool Offered(ReconcileRow r) => !r.HasMatch || (!r.Matched && !r.AlreadyCleared);

    /// <summary>The first tab holds the lines that have a match in the table; the second those with none, and those whose match was rejected.</summary>
    private void FillLists()
    {
        _filling = true;
        _matchList.ItemsSource = _rows.Where(r => r.HasMatch).ToList();
        _newRows.Clear();
        foreach (var row in _rows.Where(Offered)) _newRows.Add(row);
        _filling = false;
    }

    private void Row_Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (_filling) return;
        if (e.PropertyName == nameof(ReconcileRow.Matched) && sender is ReconcileRow row)
        {
            if (!row.Matched && row.HasMatch) row.Import = true;   // not the same transaction after all: offer it as a new one
            SyncNewRows();
        }
        UpdateSummary();
    }

    /// <summary>Makes the second tab list the lines that are offered for import, in the order of the file.</summary>
    private void SyncNewRows()
    {
        _filling = true;
        var wanted = _rows.Where(Offered).ToList();
        for (var i = _newRows.Count - 1; i >= 0; i--) if (!wanted.Contains(_newRows[i])) _newRows.RemoveAt(i);
        foreach (var r in wanted.Where(r => !_newRows.Contains(r))) _newRows.Insert(Math.Min(wanted.IndexOf(r), _newRows.Count), r);
        _filling = false;
    }

    /// <summary>Changes many lines at once (select all, deselect all), then updates the lists and the totals once.</summary>
    private void Bulk(Action change)
    {
        _filling = true;
        change();
        _filling = false;
        SyncNewRows();
        UpdateSummary();
    }

    /// <summary>The Select all and Deselect all buttons of a tab.</summary>
    private static StackPanel SelectBar(Action<bool> set)
    {
        var all = new HyperlinkButton { Content = "Select all", Padding = new Thickness(8, 2, 8, 2) };
        var none = new HyperlinkButton { Content = "Deselect all", Padding = new Thickness(8, 2, 8, 2) };
        all.Click += (_, _) => set(true);
        none.Click += (_, _) => set(false);
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Padding = new Thickness(4, 0, 0, 0), Children = { all, none } };
    }

    private IEnumerable<ReconcileRow> ToClear => _rows.Where(r => r.CanConfirm && r.Matched);
    private IEnumerable<ReconcileRow> ToAdd => _rows.Where(r => Offered(r) && r.Import);

    private void UpdateSummary()
    {
        var withMatch = _rows.Count(r => r.HasMatch);
        var clear = ToClear.Count();
        var add = ToAdd.Count();
        var offered = _newRows.Count;
        _matchHead.Text = $"Already in the table ({withMatch})";
        _newHead.Text = $"Not in the table ({offered})";
        _matchHelp.Text = withMatch == 0
            ? "None of the lines in the file looks like a transaction that is already in the table."
            : "Each line of the file that looks like a transaction already in the table is shown beside it. Tick the pair if it is the same transaction: it is then cleared. Untick it if it is not; the line moves to the next tab.";
        _newHelp.Text = offered == 0 ? "Every line of the file is already in the table." : "These lines of the file are not in the table. Tick the ones to add as new, cleared transactions."
            + (_vm.AccountIsOnBudget(AccountId) ? " They have no budget item yet: give them one in the table." : "");
        _reconcileSummary.Text = $"{clear} transaction{(clear == 1 ? "" : "s")} will be cleared and {add} new transaction{(add == 1 ? "" : "s")} added. Nothing changes until you press Apply.";
        IsPrimaryButtonEnabled = clear + add > 0;
    }

    private async Task<bool> ApplyAsync()
    {
        if (AccountId is not { } account) return false;
        var result = await _vm.ApplyBankMatchesAsync(account, ToClear.Select(r => r.Match).ToList(), ToAdd.Select(r => r.Match.Line with { Description = (r.Payee ?? "").Trim() }).ToList(), CurrentFormat(), _rememberBox.IsChecked == true);
        return result is not null;
    }
}
