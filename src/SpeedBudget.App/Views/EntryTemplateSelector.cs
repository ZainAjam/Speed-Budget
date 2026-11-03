using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SpeedBudget.App.ViewModels;

namespace SpeedBudget.App.Views;

/// <summary>Picks the month-line template for lines and the transaction template for everything else.</summary>
public sealed partial class EntryTemplateSelector : DataTemplateSelector
{
    public DataTemplate? TransactionTemplate { get; set; }
    public DataTemplate? MarkerTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) => item is MarkerRow ? MarkerTemplate : TransactionTemplate;

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) => SelectTemplateCore(item);
}
