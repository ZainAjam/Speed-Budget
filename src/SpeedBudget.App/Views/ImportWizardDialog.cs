using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using SpeedBudget.App.ViewModels;
using SpeedBudget.Data;

namespace SpeedBudget.App.Views;

/// <summary>A column offered in a mapping drop-down: its index (-1 for "none") and a label with a sample of what is in it.</summary>
public sealed record ColumnChoice(int Index, string Label);

/// <summary>
/// A line of the file in the reconciliation. When the table has a transaction that looks like it, the user can confirm the pair (the
/// transaction is then cleared, taking from the file whichever of date, counterparty, memo and amount were chosen) or reject it, which makes the
/// line one to import as new. A line with no match is only ever offered as new.
/// </summary>
public sealed class ReconcileRow : INotifyPropertyChanged
{
    private static readonly string[] FieldNames = ["Date", "Payee", "Memo", "Amount"];
    private readonly bool[] _fromFile = new bool[4];
    private bool _matched = true, _apply, _import;
    private string _memo = "";

    public required BankMatch Match { get; init; }

    /// <summary>The transaction that looks the same, or null when the table has none.</summary>
    public TransactionEntity? Existing => Match.Existing;
    public bool HasMatch => Existing is not null;

    /// <summary>Already cleared: confirming the match changes nothing.</summary>
    public bool AlreadyCleared => Existing is { IsCleared: true, IsUndated: false } || Existing is { IsReconciled: true };

    /// <summary>The pair is still taken to be the same transaction; false once the user has rejected it.</summary>
    public bool Matched { get => _matched; set => Set(ref _matched, value); }

    /// <summary>For a pair: clear the transaction (and take the chosen values from the file).</summary>
    public bool Apply { get => _apply; set => Set(ref _apply, value); }

    /// <summary>To be added as a new transaction (a line with no match, or whose match was rejected).</summary>
    public bool Import { get => _import; set => Set(ref _import, value); }

    public bool CanConfirm => HasMatch && !AlreadyCleared;
    public double MatchOpacity => AlreadyCleared ? 0.55 : 1;

    // For a new transaction: what to add. The user can pick the counterparty and change the memo.
    public string Payee { get; set; } = "";
    public string Memo { get => _memo; set => _memo = value ?? ""; }
    public IReadOnlyList<string> Suggestions { get; init; } = [];
    public IReadOnlyDictionary<string, string> Icons { get; init; } = new Dictionary<string, string>();

    // The file's side
    public string DateText => Match.Line.Date.ToString("d MMM yyyy");
    public string Description => Match.Line.Description;
    public string AmountText => Format.Money(Match.Line.Amount);
    public string MemoText => Match.Line.Memo ?? "";

    // The table's side
    public string TableDateText => Existing is null ? "" : Existing.IsUndated ? "No date yet" : Existing.Date.ToString("d MMM yyyy");
    public string TablePayee => Existing?.Payee ?? "";
    public string TableMemo => Existing?.Memo ?? "";
    public string TableAmountText => Existing is null ? "" : Format.Money(Existing.Amount);
    public string TableNote => Existing is null ? "" : AlreadyCleared ? "already cleared" : Existing.IsUndated ? "expected, no date" : Existing.Date > DateOnly.FromDateTime(DateTime.Today) ? "scheduled" : "";

    // Which version of each value to keep: the table's or the file's. A radio button on each side, so each value has a pair of properties.
    public bool DateTable { get => !_fromFile[0]; set { if (value) Choose(0, false); } }
    public bool DateFile { get => _fromFile[0]; set { if (value) Choose(0, true); } }
    public bool PayeeTable { get => !_fromFile[1]; set { if (value) Choose(1, false); } }
    public bool PayeeFile { get => _fromFile[1]; set { if (value) Choose(1, true); } }
    public bool MemoTable { get => !_fromFile[2]; set { if (value) Choose(2, false); } }
    public bool MemoFile { get => _fromFile[2]; set { if (value) Choose(2, true); } }
    public bool AmountTable { get => !_fromFile[3]; set { if (value) Choose(3, false); } }
    public bool AmountFile { get => _fromFile[3]; set { if (value) Choose(3, true); } }

    public bool UseFileDate => _fromFile[0];
    public bool UseFilePayee => _fromFile[1];
    public bool UseFileMemo => _fromFile[2];
    public bool UseFileAmount => _fromFile[3];

    /// <summary>Where the table has nothing (no date yet, no counterparty, no memo) and the file has something, the file's value is the one to use.</summary>
    public void ChooseDefaults()
    {
        if (Existing is null) return;
        _fromFile[0] = Existing.IsUndated;
        _fromFile[1] = Existing.Payee == "" && Description != "";
        _fromFile[2] = string.IsNullOrEmpty(Existing.Memo) && MemoText != "";
        _fromFile[3] = false;
    }

    /// <summary>The "Not the same" button: this line is not that transaction, so it is offered as a new one.</summary>
    public ICommand RejectCommand => new RelayCommand(() => { Matched = false; Import = true; });

    /// <summary>On the second tab: the pair was rejected by mistake, so put it back.</summary>
    public ICommand RestoreCommand => new RelayCommand(() => Matched = true);

    public Visibility RestoreVisibility => HasMatch ? Visibility.Visible : Visibility.Collapsed;

    private void Choose(int field, bool file)
    {
        if (_fromFile[field] == file) return;
        _fromFile[field] = file;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(FieldNames[field] + "Table"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(FieldNames[field] + "File"));
    }

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
        Resources["ContentDialogMaxWidth"] = 4000.0;
        Resources["ContentDialogMaxHeight"] = 4000.0;
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

    private readonly Pivot _pivot = new();
    private readonly TextBlock _matchHead = new() { Text = "Already in the table", FontSize = 15 }, _newHead = new() { Text = "Not in the table", FontSize = 15 };
    private readonly PivotItem _matchTab = new();
    private readonly PivotItem _newTab = new();
    private readonly ListView _matchList = new() { SelectionMode = ListViewSelectionMode.None };
    private readonly ListView _newList = new() { SelectionMode = ListViewSelectionMode.None };
    private readonly System.Collections.ObjectModel.ObservableCollection<ReconcileRow> _matchRows = new();
    private readonly System.Collections.ObjectModel.ObservableCollection<ReconcileRow> _newRows = new();
    private readonly TextBlock _matchHelp = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
    private readonly TextBlock _newHelp = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };

    private static string Template(string columns, string cells) => $$"""
        <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:local="using:SpeedBudget.App.Views">
          <Grid ColumnSpacing="6" Opacity="{Binding MatchOpacity}">
            <Grid.ColumnDefinitions>{{columns}}</Grid.ColumnDefinitions>
            {{cells}}
          </Grid>
        </DataTemplate>
        """;

    /// <summary>One value of a pair, with a radio button to choose it. Each sits in its own Border so the radio buttons do not group with one another.</summary>
    private static string Radio(int column, string property, string text) => $$"""
        <Border Grid.Column="{{column}}"><RadioButton IsChecked="{Binding {{property}}, Mode=TwoWay}" MinWidth="0" Padding="4,0,0,0" HorizontalAlignment="Stretch" VerticalAlignment="Center" MinHeight="32"><TextBlock Text="{Binding {{text}}}" TextTrimming="CharacterEllipsis" ToolTipService.ToolTip="{Binding {{text}}}"/></RadioButton></Border>
        """;

    private void BuildReconcileStep()
    {
        _reconcileStep.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _reconcileStep.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _reconcileStep.Children.Add(_reconcileSummary);

        const string itemStyle = """
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ListViewItem">
              <Setter Property="MinHeight" Value="44"/>
              <Setter Property="Padding" Value="12,0,12,0"/>
              <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
            </Style>
            """;

        // Tab 1: the table's transaction on the left, the file's line on the right, a radio button on each value to say which to keep.
        const string matchColumns = """
            <ColumnDefinition Width="30"/><ColumnDefinition Width="116"/><ColumnDefinition Width="*"/><ColumnDefinition Width="*"/><ColumnDefinition Width="92"/>
            <ColumnDefinition Width="16"/>
            <ColumnDefinition Width="116"/><ColumnDefinition Width="*"/><ColumnDefinition Width="*"/><ColumnDefinition Width="92"/><ColumnDefinition Width="112"/>
            """;
        _matchList.ItemTemplate = (DataTemplate)XamlReader.Load(Template(matchColumns, $$"""
            <CheckBox IsChecked="{Binding Apply, Mode=TwoWay}" IsEnabled="{Binding CanConfirm}" MinWidth="0" Padding="0" VerticalAlignment="Center" ToolTipService.ToolTip="Clear this transaction"/>
            {{Radio(1, "DateTable", "TableDateText")}}
            {{Radio(2, "PayeeTable", "TablePayee")}}
            {{Radio(3, "MemoTable", "TableMemo")}}
            {{Radio(4, "AmountTable", "TableAmountText")}}
            <FontIcon Grid.Column="5" Glyph="&#xE76C;" FontSize="12" Opacity="0.6" VerticalAlignment="Center" HorizontalAlignment="Center"/>
            {{Radio(6, "DateFile", "DateText")}}
            {{Radio(7, "PayeeFile", "Description")}}
            {{Radio(8, "MemoFile", "MemoText")}}
            {{Radio(9, "AmountFile", "AmountText")}}
            <Button Grid.Column="10" Command="{Binding RejectCommand}" Content="Not the same" Padding="8,4" VerticalAlignment="Center" HorizontalAlignment="Right" ToolTipService.ToolTip="This is not the same transaction: move the line to the Not in the table tab"/>
            """));
        _matchList.ItemContainerStyle = (Style)XamlReader.Load(itemStyle);
        _matchList.ItemsSource = _matchRows;
        var matchHeading = (Grid)XamlReader.Load($$"""
            <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" ColumnSpacing="6" Padding="12,0,12,0" Opacity="0.75">
              <Grid.ColumnDefinitions>{{matchColumns}}</Grid.ColumnDefinitions>
              <TextBlock Grid.Column="0" Text="Clear" FontSize="11" VerticalAlignment="Bottom"/>
              <TextBlock Grid.Column="1" Grid.ColumnSpan="4" Text="In the transaction table (choose what to keep)" FontWeight="SemiBold"/>
              <TextBlock Grid.Column="6" Grid.ColumnSpan="4" Text="In the import file" FontWeight="SemiBold"/>
              <TextBlock Grid.Row="1" Grid.Column="1" Text="Date" FontSize="11" Margin="30,0,0,0"/><TextBlock Grid.Row="1" Grid.Column="2" Text="Counterparty" FontSize="11" Margin="30,0,0,0"/>
              <TextBlock Grid.Row="1" Grid.Column="3" Text="Memo" FontSize="11" Margin="30,0,0,0"/><TextBlock Grid.Row="1" Grid.Column="4" Text="Amount" FontSize="11" Margin="30,0,0,0"/>
              <TextBlock Grid.Row="1" Grid.Column="6" Text="Date" FontSize="11" Margin="30,0,0,0"/><TextBlock Grid.Row="1" Grid.Column="7" Text="Counterparty" FontSize="11" Margin="30,0,0,0"/>
              <TextBlock Grid.Row="1" Grid.Column="8" Text="Memo" FontSize="11" Margin="30,0,0,0"/><TextBlock Grid.Row="1" Grid.Column="9" Text="Amount" FontSize="11" Margin="30,0,0,0"/>
            </Grid>
            """);
        matchHeading.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        matchHeading.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var matchPanel = new Grid { RowSpacing = 6 };
        for (var i = 0; i < 3; i++) matchPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        matchPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var matchBar = SelectBar(on => Bulk(() => { foreach (var r in _matchRows.Where(r => r.CanConfirm)) r.Apply = on; }));
        Grid.SetRow(matchBar, 1); Grid.SetRow(matchHeading, 2); Grid.SetRow(_matchList, 3);
        matchPanel.Children.Add(_matchHelp); matchPanel.Children.Add(matchBar); matchPanel.Children.Add(matchHeading); matchPanel.Children.Add(_matchList);
        _matchTab.Content = matchPanel;

        // Tab 2: lines of the file that the table does not have, to pick which ones to import.
        const string newColumns = """<ColumnDefinition Width="34"/><ColumnDefinition Width="112"/><ColumnDefinition Width="*"/><ColumnDefinition Width="*"/><ColumnDefinition Width="110"/><ColumnDefinition Width="116"/>""";
        _newList.ItemTemplate = (DataTemplate)XamlReader.Load(Template(newColumns, """
            <CheckBox IsChecked="{Binding Import, Mode=TwoWay}" MinWidth="0" Padding="0" VerticalAlignment="Center" ToolTipService.ToolTip="Tick to add this as a new transaction"/>
            <TextBlock Grid.Column="1" Text="{Binding DateText}" VerticalAlignment="Center"/>
            <local:PayeePicker Grid.Column="2" ItemsSource="{Binding Suggestions}" Icons="{Binding Icons}" SelectedPayee="{Binding Payee, Mode=TwoWay}" AllowEdit="False" HorizontalAlignment="Stretch" VerticalAlignment="Center"/>
            <TextBox Grid.Column="3" Text="{Binding Memo, Mode=TwoWay}" PlaceholderText="Memo" VerticalAlignment="Center" MinWidth="0"/>
            <TextBlock Grid.Column="4" Text="{Binding AmountText}" HorizontalAlignment="Right" VerticalAlignment="Center"/>
            <Button Grid.Column="5" Command="{Binding RestoreCommand}" Visibility="{Binding RestoreVisibility}" Content="Back to match" Padding="8,4" VerticalAlignment="Center" HorizontalAlignment="Right" ToolTipService.ToolTip="Put this line back beside the transaction it matched"/>
            """));
        _newList.ItemContainerStyle = (Style)XamlReader.Load(itemStyle);
        _newList.ItemsSource = _newRows;
        var newHeading = (Grid)XamlReader.Load($$"""
            <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" ColumnSpacing="6" Padding="12,0,12,0" Opacity="0.75">
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
        _pivot.Items.Add(_matchTab); _pivot.Items.Add(_newTab);
        Grid.SetRow(_pivot, 1);
        _reconcileStep.Children.Add(_pivot);
    }

    private void ShowStep(bool reconcile)
    {
        _onReconcile = reconcile;
        _mapStep.Visibility = reconcile ? Visibility.Collapsed : Visibility.Visible;
        _reconcileStep.Visibility = reconcile ? Visibility.Visible : Visibility.Collapsed;
        Title = reconcile ? "Import transactions: reconcile" : "Import transactions";
        PrimaryButtonText = reconcile ? "Apply" : "Next";
        SecondaryButtonText = reconcile ? "Edit import settings" : "";
        if (reconcile)
        {
            // The reconciliation uses the whole window, as it holds a lot of lines with a lot to compare.
            if (XamlRoot is { } root)
            {
                _reconcileStep.Width = Math.Max(640, root.Size.Width - 140);
                _reconcileStep.Height = Math.Max(300, root.Size.Height - 250);
            }
            UpdateSummary();
        }
        else
        {
            _reconcileStep.Width = double.NaN;
            _reconcileStep.Height = double.NaN;
            Reparse();
        }
    }

    private async Task GoToReconcileAsync()
    {
        if (AccountId is not { } account) return;
        var matches = await _vm.MatchBankLinesAsync(account, _parse.Lines);
        var payees = _vm.Payees.ToList();
        _rows = matches.Select(m => new ReconcileRow { Match = m, Payee = m.Line.Description, Memo = m.Line.Memo ?? "", Suggestions = payees, Icons = _vm.PayeeIconsForImport }).ToList();
        foreach (var row in _rows)
        {
            row.ChooseDefaults();
            row.Apply = row.CanConfirm;   // a match is proposed; the user confirms or rejects it
            row.Import = !row.HasMatch;
            row.PropertyChanged += Row_Changed;
        }
        _filling = true;
        SyncLists();
        _filling = false;
        ShowStep(reconcile: true);
        _pivot.SelectedIndex = _rows.Any(r => r.HasMatch) ? 0 : 1;   // nothing to check against the table: open on the lines to import
    }

    private bool _filling;

    private static bool Offered(ReconcileRow r) => !r.HasMatch || !r.Matched;

    /// <summary>The first tab holds the pairs the user has not rejected; the second the lines with no match and the rejected pairs, in the order of the file.</summary>
    private void SyncLists()
    {
        Sync(_matchRows, _rows.Where(r => r.HasMatch && r.Matched).ToList());
        Sync(_newRows, _rows.Where(Offered).ToList());
    }

    private static void Sync(System.Collections.ObjectModel.ObservableCollection<ReconcileRow> target, List<ReconcileRow> wanted)
    {
        for (var i = target.Count - 1; i >= 0; i--) if (!wanted.Contains(target[i])) target.RemoveAt(i);
        for (var i = 0; i < wanted.Count; i++)
        {
            if (i < target.Count && ReferenceEquals(target[i], wanted[i])) continue;
            var at = target.IndexOf(wanted[i]);
            if (at >= 0) target.Move(at, i); else target.Insert(Math.Min(i, target.Count), wanted[i]);
        }
    }

    private void Row_Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (_filling) return;
        if (e.PropertyName == nameof(ReconcileRow.Matched))
        {
            _filling = true;
            SyncLists();
            _filling = false;
        }
        UpdateSummary();
    }

    /// <summary>Changes many lines at once (select all, deselect all), then updates the lists and the totals once.</summary>
    private void Bulk(Action change)
    {
        _filling = true;
        change();
        SyncLists();
        _filling = false;
        UpdateSummary();
    }

    private IEnumerable<ReconcileRow> ToClear => _rows.Where(r => r.CanConfirm && r.Matched && r.Apply);
    private IEnumerable<ReconcileRow> ToAdd => _rows.Where(r => Offered(r) && r.Import);

    private void UpdateSummary()
    {
        var withMatch = _matchRows.Count;
        var clear = ToClear.Count();
        var add = ToAdd.Count();
        var offered = _newRows.Count;
        _matchHead.Text = $"Already in the table ({withMatch})";
        _newHead.Text = $"Not in the table ({offered})";
        _matchHelp.Text = withMatch == 0
            ? "None of the lines in the file looks like a transaction that is already in the table."
            : "Each line of the file that looks like a transaction already in the table is shown beside it. For each value, choose with the radio buttons whether to keep the table's or take the file's. Tick Clear to clear the transaction with those choices. If it is not the same transaction, press Not the same: the line moves to the next tab.";
        _newHelp.Text = offered == 0 ? "Every line of the file is already in the table." : "These lines of the file are not in the table. Tick the ones to add as new, cleared transactions, and choose the counterparty and edit the memo if you like."
            + (_vm.AccountIsOnBudget(AccountId) ? " They have no budget item yet: give them one in the table." : "");
        _reconcileSummary.Text = $"{clear} transaction{(clear == 1 ? "" : "s")} will be cleared and {add} new transaction{(add == 1 ? "" : "s")} added. Nothing changes until you press Apply.";
        IsPrimaryButtonEnabled = clear + add > 0;
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

    private async Task<bool> ApplyAsync()
    {
        if (AccountId is not { } account) return false;
        var clear = ToClear.Select(r => new BankClear(r.Match, r.UseFileDate, r.UseFilePayee, r.UseFileMemo, r.UseFileAmount)).ToList();
        var add = ToAdd.Select(r => r.Match.Line with { Description = (r.Payee ?? "").Trim(), Memo = string.IsNullOrWhiteSpace(r.Memo) ? null : r.Memo.Trim() }).ToList();
        var result = await _vm.ApplyBankMatchesAsync(account, clear, add, CurrentFormat(), _rememberBox.IsChecked == true);
        return result is not null;
    }
}
