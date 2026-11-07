using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SpeedBudget.App.ViewModels;

namespace SpeedBudget.App.Views;

/// <summary>Excel-style filters on the column headings of the recorded transactions table.</summary>
public sealed partial class TransactionsPage
{
    private const string BlankLabel = "(Blanks)";

    private IEnumerable<ColumnTitle> FilterTitles() =>
        HeadingGrid.Children.OfType<Border>().Select(b => b.Child).OfType<ColumnTitle>().Where(t => t.FilterKey != "");

    private void WireFilters()
    {
        foreach (var title in FilterTitles()) title.FilterClicked += ShowFilter;
        ViewModel.Filters.Changed += ShowFilterState;
    }

    /// <summary>The funnel of every filtered column is lit.</summary>
    private void ShowFilterState()
    {
        foreach (var title in FilterTitles())
            title.IsFiltered = Enum.TryParse<FilterColumn>(title.FilterKey, out var column) && ViewModel.Filters.IsActive(column);
    }

    private void ClearFilters_Click(object sender, RoutedEventArgs e) => ViewModel.ClearFilters();

    /// <summary>
    /// The filter of one column: a search box and the column's values with a tick beside each, as in Excel. Only the ticked values stay in the table.
    /// </summary>
    private void ShowFilter(ColumnTitle title)
    {
        if (!Enum.TryParse<FilterColumn>(title.FilterKey, out var column)) return;
        var all = ViewModel.FilterValues(column).Select(v => v == "" ? BlankLabel : v).ToList();
        var allowed = ViewModel.Filters.Allowed(column);
        var ticked = new HashSet<string>(allowed is null ? all : all.Where(v => allowed.Contains(v == BlankLabel ? "" : v)));
        var visible = all;
        var syncing = false;

        var search = new TextBox { PlaceholderText = "Search" };
        var selectAll = new CheckBox { Content = "(Select all)", MinWidth = 0 };
        var list = new ListView { SelectionMode = ListViewSelectionMode.Multiple, MaxHeight = 260, IsItemClickEnabled = false };
        var ok = new Button { Content = "OK", Style = (Style)Application.Current.Resources["AccentButtonStyle"], MinWidth = 80 };
        var clear = new Button { Content = "Clear filter", IsEnabled = allowed is not null };
        var none = new TextBlock { Text = "Nothing matches.", Opacity = 0.7, Visibility = Visibility.Collapsed, Margin = new Thickness(4, 0, 0, 0) };

        void Sync()
        {
            selectAll.IsChecked = visible.Count > 0 && visible.All(ticked.Contains);
            ok.IsEnabled = ticked.Count > 0;
            none.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        void Refill()
        {
            syncing = true;
            list.ItemsSource = visible;
            if (visible.Count > 0 && visible.All(ticked.Contains)) list.SelectAll();
            else foreach (var v in visible.Where(ticked.Contains)) list.SelectedItems.Add(v);
            syncing = false;
            Sync();
        }

        search.TextChanged += (_, _) =>
        {
            var text = search.Text.Trim();
            visible = text == "" ? all : all.Where(v => v.Contains(text, StringComparison.CurrentCultureIgnoreCase)).ToList();
            Refill();
        };
        list.SelectionChanged += (_, e) =>
        {
            if (syncing) return;
            foreach (var v in e.AddedItems.OfType<string>()) ticked.Add(v);
            foreach (var v in e.RemovedItems.OfType<string>()) ticked.Remove(v);
            Sync();
        };
        selectAll.Click += (_, _) =>
        {
            var on = selectAll.IsChecked == true;
            foreach (var v in visible) { if (on) ticked.Add(v); else ticked.Remove(v); }
            Refill();
        };

        var flyout = new Flyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedLeft };
        ok.Click += (_, _) =>
        {
            // Every value ticked is the same as no filter.
            ViewModel.Filters.Set(column, ticked.Count >= all.Count ? null : ticked.Select(v => v == BlankLabel ? "" : v));
            flyout.Hide();
        };
        clear.Click += (_, _) => { ViewModel.Filters.Set(column, null); flyout.Hide(); };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { clear, ok } };
        flyout.Content = new StackPanel { Width = 280, Spacing = 8, Children = { search, selectAll, list, none, buttons } };
        flyout.Opened += (_, _) => search.Focus(FocusState.Programmatic);
        Refill();
        flyout.ShowAt(title);
    }
}
