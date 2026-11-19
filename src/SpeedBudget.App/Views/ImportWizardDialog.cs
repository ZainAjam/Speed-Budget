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
/// One value of a matched pair (the date, the counterparty, the memo or the amount) as it is in the table and as it is in the file, and which of
/// the two the user wants to keep. Where the two are the same there is nothing to choose.
/// </summary>
public sealed class FieldChoice : INotifyPropertyChanged
{
    private bool _fromFile;

    public FieldChoice(string label, string table, string file, bool enabled)
    {
        Label = label;
        TableText = table;
        FileText = file;
        Enabled = enabled;
        ChooseTable = new RelayCommand(() => FromFile = false);
        ChooseFile = new RelayCommand(() => FromFile = true);
    }

    public string Label { get; }
    public string TableText { get; }
    public string FileText { get; }
    public bool Enabled { get; }

    /// <summary>Both versions are the same, so there is nothing to choose.</summary>
    public bool Differs => TableText != FileText;

    public string TableShown => TableText == "" ? "(blank)" : TableText;
    public string FileShown => FileText == "" ? "(blank)" : FileText;
    public double TableTextOpacity => TableText == "" ? 0.5 : 1;
    public double FileTextOpacity => FileText == "" ? 0.5 : 1;

    public bool FromFile
    {
        get => _fromFile;
        set
        {
            if (_fromFile == value) return;
            _fromFile = value;
            foreach (var name in new[] { nameof(FromFile), nameof(TableBorder), nameof(FileBorder), nameof(TableOpacity), nameof(FileOpacity), nameof(TableCheck), nameof(FileCheck) })
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public Visibility DiffVisibility => Differs ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SameVisibility => Differs ? Visibility.Collapsed : Visibility.Visible;

    // The version that will be kept has an accent outline and a tick; the other is dimmed.
    private static Microsoft.UI.Xaml.Media.Brush Accent => Format.Theme("AccentFillColorDefaultBrush");
    private static readonly Microsoft.UI.Xaml.Media.Brush None = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
    public Microsoft.UI.Xaml.Media.Brush TableBorder => Differs && !_fromFile ? Accent : None;
    public Microsoft.UI.Xaml.Media.Brush FileBorder => Differs && _fromFile ? Accent : None;
    public double TableOpacity => Differs && _fromFile ? 0.5 : 1;
    public double FileOpacity => Differs && !_fromFile ? 0.5 : 1;
    public Visibility TableCheck => Differs && !_fromFile ? Visibility.Visible : Visibility.Collapsed;
    public Visibility FileCheck => Differs && _fromFile ? Visibility.Visible : Visibility.Collapsed;

    public ICommand ChooseTable { get; }
    public ICommand ChooseFile { get; }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// A line of the file in the reconciliation. When the table has a transaction that looks like it, the user can confirm the pair (the
/// transaction is then cleared, taking from the file whichever of date, counterparty, memo and amount were chosen) or reject it, which makes the
/// line one to import as new. A line with no match is only ever offered as new.
/// </summary>
public sealed class ReconcileRow : INotifyPropertyChanged
{
    private FieldChoice[]? _fields;
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
    public double MatchOpacity => AlreadyCleared ? 0.6 : 1;
    public Visibility ClearedNoteVisibility => AlreadyCleared ? Visibility.Visible : Visibility.Collapsed;

    // For a new transaction: what to add. The user can pick the counterparty and change the memo.
    public string Payee { get; set; } = "";
    public string Memo { get => _memo; set => _memo = value ?? ""; }
    public IReadOnlyList<string> Suggestions { get; init; } = [];
    public IReadOnlyDictionary<string, string> Icons { get; init; } = new Dictionary<string, string>();

    // The file's line
    public string DateText => Match.Line.Date.ToString("d MMM yyyy");
    public string Description => Match.Line.Description;
    public string AmountText => Format.Money(Match.Line.Amount);
    public string MemoText => Match.Line.Memo ?? "";

    /// <summary>The date, counterparty, memo and amount of the pair, each as it is in the table and in the file.</summary>
    public FieldChoice[] Fields => _fields ??= Existing is null ? [] :
    [
        new("Date", Existing.IsUndated ? "No date yet" : Existing.Date.ToString("d MMM yyyy"), DateText, CanConfirm),
        new("Counterparty", Existing.Payee, Description, CanConfirm),
        new("Memo", Existing.Memo ?? "", MemoText, CanConfirm),
        new("Amount", Format.Money(Existing.Amount), AmountText, CanConfirm),
    ];

    public bool UseFileDate => Fields.Length > 0 && Fields[0].FromFile;
    public bool UseFilePayee => Fields.Length > 0 && Fields[1].FromFile;
    public bool UseFileMemo => Fields.Length > 0 && Fields[2].FromFile;
    public bool UseFileAmount => Fields.Length > 0 && Fields[3].FromFile;

    /// <summary>Where the table has nothing (no date yet, no counterparty, no memo) and the file has something, the file's value is the one to use.</summary>
    public void ChooseDefaults()
    {
        if (Existing is null) return;
        Fields[0].FromFile = Existing.IsUndated;
        Fields[1].FromFile = Existing.Payee == "" && Description != "";
        Fields[2].FromFile = string.IsNullOrEmpty(Existing.Memo) && MemoText != "";
    }

    /// <summary>The "Not the same" button: this line is not that transaction, so it is offered as a new one.</summary>
    public ICommand RejectCommand => new RelayCommand(() => { Matched = false; Import = true; });

    /// <summary>On the second tab: the pair was rejected by mistake, so put it back.</summary>
    public ICommand RestoreCommand => new RelayCommand(() => Matched = true);

    public Visibility RestoreVisibility => HasMatch ? Visibility.Visible : Visibility.Collapsed;

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
    private readonly ComboBox _descriptionBox = Choice(), _memoBox = Choice(), _balanceBox = Choice();
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
        foreach (var box in new[] { _dateBox, _formatBox, _descriptionBox, _memoBox, _balanceBox, _amountBox, _debitBox, _creditBox })
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
        Add("Balance", _balanceBox, "The column with the running balance after each line, if the file has one. It is used at the end to check that the account agrees with the bank");
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
        Fill(_balanceBox, true, f?.BalanceColumn ?? -1);
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
            if (format is not null && format.HeaderRows < _table.Rows.Count && BankFile.Parse(_table, format).Lines.Count > 0)
            {
                _usedSaved = true;
                if (format.BalanceColumn < 0 && BankFile.Detect(_table) is { BalanceColumn: >= 0 } guess) format = format with { BalanceColumn = guess.BalanceColumn };
            }
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
            split ? -1 : Index(_amountBox), split ? Index(_debitBox) : -1, split ? Index(_creditBox) : -1, _negativeOut.IsChecked == true, Index(_memoBox), Index(_balanceBox));
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

    /// <summary>One value of a pair, as a button that keeps this version when pressed (outlined and ticked when kept), or as plain text where both versions are the same.</summary>
    private static string Side(string field, bool table)
    {
        var side = table ? "Table" : "File";
        return $$"""
            <Grid Height="32">
              <Button Visibility="{Binding {{field}}.DiffVisibility}" Command="{Binding {{field}}.Choose{{side}}}" IsEnabled="{Binding {{field}}.Enabled}"
                      BorderThickness="2" BorderBrush="{Binding {{field}}.{{side}}Border}" Opacity="{Binding {{field}}.{{side}}Opacity}" Background="Transparent"
                      HorizontalAlignment="Stretch" VerticalAlignment="Stretch" HorizontalContentAlignment="Stretch" Padding="10,0">
                <Grid>
                  <TextBlock Text="{Binding {{field}}.{{side}}Shown}" Opacity="{Binding {{field}}.{{side}}TextOpacity}" TextTrimming="CharacterEllipsis" VerticalAlignment="Center" Margin="0,0,22,0"/>
                  <FontIcon Glyph="&#xE73E;" FontSize="13" HorizontalAlignment="Right" VerticalAlignment="Center" Visibility="{Binding {{field}}.{{side}}Check}" Foreground="{ThemeResource AccentTextFillColorPrimaryBrush}"/>
                </Grid>
              </Button>
              <TextBlock Visibility="{Binding {{field}}.SameVisibility}" Text="{Binding {{field}}.{{side}}Shown}" Opacity="0.85" Padding="12,0" VerticalAlignment="Center" TextTrimming="CharacterEllipsis"/>
            </Grid>
            """;
    }

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

        // Tab 1: one card for each pair. The table's version is on the left and the file's on the right, each in its own panel with its own heading
        // band, and the name of the value between them. Where the two differ, press the version to keep: it gets an accent outline and a tick.
        _matchList.ItemTemplate = (DataTemplate)XamlReader.Load($$"""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <Border CornerRadius="8" Padding="14" Margin="0,5" BorderThickness="1" Opacity="{Binding MatchOpacity}"
                      Background="{ThemeResource CardBackgroundFillColorDefaultBrush}" BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}">
                <Grid RowSpacing="12">
                  <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/></Grid.RowDefinitions>
                  <Grid ColumnSpacing="12">
                    <Grid.ColumnDefinitions><ColumnDefinition Width="Auto"/><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
                    <CheckBox Content="Clear this transaction" IsChecked="{Binding Apply, Mode=TwoWay}" IsEnabled="{Binding CanConfirm}" MinWidth="0"/>
                    <TextBlock Grid.Column="1" Text="Already cleared: nothing to change" Opacity="0.7" VerticalAlignment="Center" Visibility="{Binding ClearedNoteVisibility}"/>
                    <Button Grid.Column="2" Command="{Binding RejectCommand}" Content="Not the same transaction" Padding="10,4" ToolTipService.ToolTip="This line is not that transaction: move it to the Not in the table tab"/>
                  </Grid>
                  <Grid Grid.Row="1" ColumnSpacing="10">
                    <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="96"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>

                    <Border CornerRadius="6" BorderThickness="1" BorderBrush="{ThemeResource ControlStrongStrokeColorDefaultBrush}" Background="{ThemeResource ControlFillColorSecondaryBrush}">
                      <StackPanel>
                        <Border Height="32" CornerRadius="5,5,0,0" Background="{ThemeResource ControlFillColorTertiaryBrush}">
                          <StackPanel Orientation="Horizontal" Spacing="8" Padding="12,0" VerticalAlignment="Center">
                            <FontIcon Glyph="&#xE8A1;" FontSize="14"/><TextBlock Text="In the transaction table" FontWeight="SemiBold"/>
                          </StackPanel>
                        </Border>
                        <StackPanel Spacing="4" Padding="8,6,8,8">
                          {{Side("Fields[0]", true)}}
                          {{Side("Fields[1]", true)}}
                          {{Side("Fields[2]", true)}}
                          {{Side("Fields[3]", true)}}
                        </StackPanel>
                      </StackPanel>
                    </Border>

                    <StackPanel Grid.Column="1" Spacing="4" Margin="0,39,0,0">
                      <TextBlock Text="{Binding Fields[0].Label}" Opacity="0.75" Height="32" TextAlignment="Center" Padding="0,6,0,0"/>
                      <TextBlock Text="{Binding Fields[1].Label}" Opacity="0.75" Height="32" TextAlignment="Center" Padding="0,6,0,0"/>
                      <TextBlock Text="{Binding Fields[2].Label}" Opacity="0.75" Height="32" TextAlignment="Center" Padding="0,6,0,0"/>
                      <TextBlock Text="{Binding Fields[3].Label}" Opacity="0.75" Height="32" TextAlignment="Center" Padding="0,6,0,0"/>
                    </StackPanel>

                    <Border Grid.Column="2" CornerRadius="6" BorderThickness="1" BorderBrush="{ThemeResource AccentFillColorDefaultBrush}" Background="{ThemeResource ControlFillColorSecondaryBrush}">
                      <StackPanel>
                        <Border Height="32" CornerRadius="5,5,0,0" Background="{ThemeResource AccentFillColorDefaultBrush}">
                          <StackPanel Orientation="Horizontal" Spacing="8" Padding="12,0" VerticalAlignment="Center">
                            <FontIcon Glyph="&#xE8E5;" FontSize="14" Foreground="{ThemeResource TextOnAccentFillColorPrimaryBrush}"/>
                            <TextBlock Text="In the import file" FontWeight="SemiBold" Foreground="{ThemeResource TextOnAccentFillColorPrimaryBrush}"/>
                          </StackPanel>
                        </Border>
                        <StackPanel Spacing="4" Padding="8,6,8,8">
                          {{Side("Fields[0]", false)}}
                          {{Side("Fields[1]", false)}}
                          {{Side("Fields[2]", false)}}
                          {{Side("Fields[3]", false)}}
                        </StackPanel>
                      </StackPanel>
                    </Border>
                  </Grid>
                </Grid>
              </Border>
            </DataTemplate>
            """);
        _matchList.ItemContainerStyle = (Style)XamlReader.Load("""
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ListViewItem">
              <Setter Property="Padding" Value="0"/>
              <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
            </Style>
            """);
        _matchList.ItemsSource = _matchRows;
        var matchPanel = new Grid { RowSpacing = 6 };
        for (var i = 0; i < 2; i++) matchPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        matchPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var matchBar = SelectBar(on => Bulk(() => { foreach (var r in _matchRows.Where(r => r.CanConfirm)) r.Apply = on; }));
        Grid.SetRow(matchBar, 1); Grid.SetRow(_matchList, 2);
        matchPanel.Children.Add(_matchHelp); matchPanel.Children.Add(matchBar); matchPanel.Children.Add(_matchList);
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
        _pivot.SelectedIndex = _rows.Any(r => r.CanConfirm) ? 0 : 1;   // nothing to check against the table: open on the lines to import
    }

    private bool _filling;

    private static bool Offered(ReconcileRow r) => !r.HasMatch || !r.Matched;

    /// <summary>The first tab holds the pairs the user has not rejected; the second the lines with no match and the rejected pairs, in the order of the file.</summary>
    private void SyncLists()
    {
        Sync(_matchRows, _rows.Where(r => r.CanConfirm && r.Matched).ToList());   // a transaction that is already cleared needs nothing, so it is not shown
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
    /// <summary>Matched transactions that are already cleared: nothing to change, but they agree with the file, so they become reconciled.</summary>
    private IEnumerable<ReconcileRow> ToReconcileOnly => _rows.Where(r => r.AlreadyCleared && r.HasMatch && r.Matched && r.Existing is { IsReconciled: false });
    private IEnumerable<ReconcileRow> ToAdd => _rows.Where(r => Offered(r) && r.Import);

    /// <summary>Says how many lines matched transactions that are already cleared, as these are not listed.</summary>
    private string ClearedNote()
    {
        var done = _rows.Count(r => r.AlreadyCleared);
        return done == 0 ? "" : $" {done} line{(done == 1 ? "" : "s")} matched transactions that are already cleared, so {(done == 1 ? "it is" : "they are")} not shown.";
    }

    private void UpdateSummary()
    {
        var withMatch = _matchRows.Count;
        var clear = ToClear.Count();
        var add = ToAdd.Count();
        var offered = _newRows.Count;
        _matchHead.Text = $"Already in the table ({withMatch})";
        _newHead.Text = $"Not in the table ({offered})";
        _matchHelp.Text = withMatch == 0
            ? "No line of the file matches a transaction in the table that is not yet cleared." + ClearedNote()
            : "Each line of the file that looks like a transaction already in the table is shown as a card, with the table's version on the left and the file's on the right. Where a value differs, press the version you want to keep (it is outlined and ticked). Tick Clear to clear the transaction with those choices, or press Not the same transaction if it is a different one.";
        _newHelp.Text = offered == 0 ? "Every line of the file is already in the table." : "These lines of the file are not in the table. Tick the ones to add as new, cleared transactions, and choose the counterparty and edit the memo if you like."
            + (_vm.AccountIsOnBudget(AccountId) ? " They have no budget item yet: give them one in the table." : "");
        _reconcileSummary.Text = $"{clear} transaction{(clear == 1 ? "" : "s")} will be cleared and {add} new transaction{(add == 1 ? "" : "s")} added. Nothing changes until you press Apply.";
        var agree = ToReconcileOnly.Count();
        if (agree > 0) _reconcileSummary.Text += $" {agree} already-cleared transaction{(agree == 1 ? "" : "s")} agree{(agree == 1 ? "s" : "")} with the file and will be marked reconciled.";
        IsPrimaryButtonEnabled = clear + add > 0 || agree > 0 || BankBalance.Closing(_parse.Lines) is not null;   // with balances in the file, Apply still checks the account against them
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

    /// <summary>After Apply, when the file had balances: its closing balance against the account's cleared balance in the app. Null when there was nothing to compare.</summary>
    public BalanceCheck? Outcome { get; private set; }

    private async Task<bool> ApplyAsync()
    {
        if (AccountId is not { } account) return false;
        var clear = ToClear.Select(r => new BankClear(r.Match, r.UseFileDate, r.UseFilePayee, r.UseFileMemo, r.UseFileAmount)).Concat(ToReconcileOnly.Select(r => new BankClear(r.Match))).ToList();
        var add = ToAdd.Select(r => r.Match.Line with { Description = (r.Payee ?? "").Trim(), Memo = string.IsNullOrWhiteSpace(r.Memo) ? null : r.Memo.Trim() }).ToList();
        var result = await _vm.ApplyBankMatchesAsync(account, clear, add, CurrentFormat(), _rememberBox.IsChecked == true);
        if (result is not null && BankBalance.Closing(_parse.Lines) is { } closing)
            Outcome = new BalanceCheck(closing.Date, closing.Balance, await _vm.ClearedBalanceAsOfAsync(account, closing.Date), await _vm.ClearedBalanceAsOfAsync(account, DateOnly.FromDateTime(DateTime.Today)), account, _parse.Lines);
        return result is not null;
    }
}
