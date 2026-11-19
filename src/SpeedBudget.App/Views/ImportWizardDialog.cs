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

/// <summary>A line of the reconciliation: what the file says, the transaction it matches, and whether the user wants it applied.</summary>
public sealed class ReconcileRow : INotifyPropertyChanged
{
    private bool _apply;
    public required BankMatch Match { get; init; }
    public required string Status { get; init; }
    public required bool CanApply { get; init; }
    public required bool IsNew { get; init; }
    public bool Apply { get => _apply; set { if (_apply == value) return; _apply = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Apply))); } }

    public string DateText => Match.Line.Date.ToString("d MMM yyyy");
    public string Description => Match.Line.Description;
    public string AmountText => Format.Money(Match.Line.Amount);
    public double TextOpacity => CanApply ? 1 : 0.55;
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
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _rawPreview = new() { FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"), FontSize = 12, Opacity = 0.8, TextWrapping = TextWrapping.NoWrap };
    private readonly StackPanel _mapStep = new() { Spacing = 10 };
    private readonly Grid _reconcileStep = new() { Visibility = Visibility.Collapsed, RowSpacing = 8 };
    private readonly TextBlock _reconcileSummary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ListView _list = new() { SelectionMode = ListViewSelectionMode.None, MaxHeight = 380 };

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
        Opened += async (_, _) => await SetUpAsync();
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
        Add("Counterparty", _descriptionBox, "The column with the description or the name of who was paid");
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
        Fill(_descriptionBox, false, f?.DescriptionColumn ?? 1);
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
    private async Task SetUpAsync()
    {
        _loading = true;
        try
        {
            BankFormat? format = null;
            if (AccountId is { } id) format = await _vm.GetBankFormatAsync(id);
            if (format is null || format.HeaderRows >= _table.Rows.Count || BankFile.Parse(_table, format).Lines.Count == 0) format = BankFile.Detect(_table);
            _format = format;
            _headerRows.Value = format?.HeaderRows ?? 0;
            FillColumnChoices();
            _moneyMode.SelectedIndex = format is { AmountColumn: < 0 } ? 1 : 0;
            _negativeOut.IsChecked = format?.NegativeIsOut ?? true;
            ShowMoneyMode();
        }
        finally { _loading = false; }
        Reparse();
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

    private void BuildReconcileStep()
    {
        _reconcileStep.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _reconcileStep.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _reconcileStep.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _reconcileStep.Children.Add(_reconcileSummary);

        var heading = (Grid)XamlReader.Load("""
            <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" ColumnSpacing="10" Padding="12,0,0,0" Opacity="0.7">
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width="32"/><ColumnDefinition Width="100"/><ColumnDefinition Width="*"/><ColumnDefinition Width="110"/><ColumnDefinition Width="240"/>
              </Grid.ColumnDefinitions>
              <TextBlock Grid.Column="1" Text="Date" FontWeight="SemiBold"/>
              <TextBlock Grid.Column="2" Text="In the file" FontWeight="SemiBold"/>
              <TextBlock Grid.Column="3" Text="Amount" FontWeight="SemiBold" HorizontalAlignment="Right"/>
              <TextBlock Grid.Column="4" Text="What happens" FontWeight="SemiBold"/>
            </Grid>
            """);
        Grid.SetRow(heading, 1);
        _reconcileStep.Children.Add(heading);

        _list.ItemTemplate = (DataTemplate)XamlReader.Load("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <Grid ColumnSpacing="10" Opacity="{Binding TextOpacity}">
                <Grid.ColumnDefinitions>
                  <ColumnDefinition Width="32"/><ColumnDefinition Width="100"/><ColumnDefinition Width="*"/><ColumnDefinition Width="110"/><ColumnDefinition Width="240"/>
                </Grid.ColumnDefinitions>
                <CheckBox IsChecked="{Binding Apply, Mode=TwoWay}" IsEnabled="{Binding CanApply}" MinWidth="0" Padding="0" VerticalAlignment="Center"/>
                <TextBlock Grid.Column="1" Text="{Binding DateText}" VerticalAlignment="Center"/>
                <TextBlock Grid.Column="2" Text="{Binding Description}" TextTrimming="CharacterEllipsis" VerticalAlignment="Center"/>
                <TextBlock Grid.Column="3" Text="{Binding AmountText}" HorizontalAlignment="Right" VerticalAlignment="Center"/>
                <TextBlock Grid.Column="4" Text="{Binding Status}" TextTrimming="CharacterEllipsis" VerticalAlignment="Center"/>
              </Grid>
            </DataTemplate>
            """);
        _list.ItemContainerStyle = (Style)XamlReader.Load("""
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ListViewItem">
              <Setter Property="MinHeight" Value="34"/>
              <Setter Property="Padding" Value="12,0,12,0"/>
              <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
            </Style>
            """);
        Grid.SetRow(_list, 2);
        _reconcileStep.Children.Add(_list);
    }

    private void ShowStep(bool reconcile)
    {
        _onReconcile = reconcile;
        _mapStep.Visibility = reconcile ? Visibility.Collapsed : Visibility.Visible;
        _reconcileStep.Visibility = reconcile ? Visibility.Visible : Visibility.Collapsed;
        Title = reconcile ? "Import transactions: reconcile" : "Import transactions";
        PrimaryButtonText = reconcile ? "Apply" : "Next";
        SecondaryButtonText = reconcile ? "Back" : "";
        if (reconcile) UpdateSummary(); else Reparse();
    }

    private async Task GoToReconcileAsync()
    {
        if (AccountId is not { } account) return;
        var matches = await _vm.MatchBankLinesAsync(account, _parse.Lines);
        _rows = matches.Select(m =>
        {
            if (m.Existing is null) return new ReconcileRow { Match = m, IsNew = true, CanApply = true, Apply = true, Status = "Not in the table: will be added" };
            var existing = m.Existing;
            var what = $"{existing.Payee}" + (existing.IsUndated ? "" : $", {existing.Date:d MMM}");
            var done = existing.IsCleared && !existing.IsUndated;
            return new ReconcileRow { Match = m, IsNew = false, CanApply = !done && !existing.IsReconciled, Apply = !done && !existing.IsReconciled, Status = done ? $"Already cleared: {what}" : $"In the table: clear {what}" };
        }).ToList();
        foreach (var row in _rows) row.PropertyChanged += (_, _) => UpdateSummary();
        _list.ItemsSource = _rows;
        ShowStep(reconcile: true);
    }

    private void UpdateSummary()
    {
        var clear = _rows.Count(r => r.Apply && !r.IsNew);
        var add = _rows.Count(r => r.Apply && r.IsNew);
        var already = _rows.Count(r => !r.CanApply);
        var text = $"{clear} transaction{(clear == 1 ? "" : "s")} already in the table will be cleared, and {add} not in the table will be added as new cleared transactions.";
        if (already > 0) text += $" {already} {(already == 1 ? "is" : "are")} already cleared.";
        if (add > 0 && _vm.AccountIsOnBudget(AccountId)) text += " New transactions have no budget item yet: give them one in the table.";
        text += " Untick any line to leave it alone.";
        _reconcileSummary.Text = text;
        IsPrimaryButtonEnabled = clear + add > 0;
    }

    private async Task<bool> ApplyAsync()
    {
        if (AccountId is not { } account) return false;
        var result = await _vm.ApplyBankMatchesAsync(account, _rows.Where(r => r.Apply && !r.IsNew).Select(r => r.Match).ToList(), _rows.Where(r => r.Apply && r.IsNew).Select(r => r.Match.Line).ToList(), CurrentFormat());
        return result is not null;
    }
}
