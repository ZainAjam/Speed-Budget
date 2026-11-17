using Microsoft.UI.Xaml.Media;
using SpeedBudget.App.ViewModels;

namespace SpeedBudget.App.Views;

/// <summary>One row of the budget item drop-down: an item, a group's small title, a divider line, or the "split" / "add" rows.</summary>
public sealed class PickerRow
{
    public ItemOption? Option { get; init; }
    public bool IsDivider { get; init; }
    public string GroupTitle { get; init; } = "";
    public bool IsHeader => GroupTitle != "";
    public bool ShowStar { get; init; }
    public bool IsContent => !IsDivider && !IsHeader;

    /// <summary>Whether pressing the row does anything (dividers and group titles do not).</summary>
    public bool CanChoose => IsContent && Option is not null;

    public string Label => Option?.Label ?? "";
    public string BalanceText => Option?.BalanceText ?? "";
    public Brush BalanceBrush => Option?.BalanceBrush ?? Format.Theme("TextFillColorPrimaryBrush");
    public bool CanEdit => Option?.CanEdit ?? false;
}
