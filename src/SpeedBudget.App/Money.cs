using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Globalization.NumberFormatting;

namespace SpeedBudget.App;

/// <summary>
/// Shows a NumberBox's amount as money: two decimals, thousands separators, right-aligned. Use
/// <c>app:Money.Format="True"</c> in XAML, or call <see cref="Apply"/> for boxes built in code.
/// </summary>
public static class Money
{
    public static readonly DependencyProperty FormatProperty =
        DependencyProperty.RegisterAttached("Format", typeof(bool), typeof(Money), new PropertyMetadata(false, OnFormatChanged));

    public static bool GetFormat(DependencyObject element) => (bool)element.GetValue(FormatProperty);

    public static void SetFormat(DependencyObject element, bool value) => element.SetValue(FormatProperty, value);

    private static void OnFormatChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is NumberBox box && e.NewValue is true) Apply(box);
    }

    public static void Apply(NumberBox box)
    {
        box.NumberFormatter = new MoneyFormatter();
        box.AcceptsExpression = false;   // the formatter reads sums itself, so amounts such as "1 250 + 350" work too
        box.Loaded -= RightAlign;
        box.Loaded += RightAlign;
    }

    // NumberBox has no alignment of its own, so reach the text box inside its template once it is loaded.
    private static void RightAlign(object sender, RoutedEventArgs e)
    {
        if (sender is NumberBox box && FindTextBox(box) is { } input)
        {
            input.TextAlignment = Microsoft.UI.Xaml.TextAlignment.Right;
            input.GotFocus -= KeepRight;
            input.GotFocus += KeepRight;
            input.Loaded -= HideClear;
            input.Loaded += HideClear;
            HideClear(input, e);
            input.PreviewKeyDown -= DecimalKey;
            input.PreviewKeyDown += DecimalKey;
            input.PointerWheelChanged -= IgnoreWheel;
            input.PointerWheelChanged += IgnoreWheel;
        }
    }

    /// <summary>While the amount is being edited the box is laid out again, and must keep the amount against its right-hand edge.</summary>
    private static void KeepRight(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox input) return;
        input.TextAlignment = Microsoft.UI.Xaml.TextAlignment.Right;
        // The clear button is brought back by the box's focus states, so it is taken out of play again now.
        HideClear(input, e);
        input.DispatcherQueue.TryEnqueue(() => HideClear(input, e));
    }

    /// <summary>The text box's own clear (X) button would sit over the amount while it is edited, so it is taken out of play.</summary>
    private static void HideClear(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox input) return;
        input.ApplyTemplate();
        if (FindNamed(input, "DeleteButton") is Button clear)
        {
            clear.Width = 0; clear.MinWidth = 0; clear.Opacity = 0; clear.IsHitTestVisible = false; clear.IsTabStop = false;
            clear.Margin = new Thickness(0); clear.Padding = new Thickness(0);
        }

        // The box's focus states put the clear button's width back, which pushes the text away from the right-hand edge.
        // The text area is let run under it (the button is invisible and cannot be pressed), so the amount stays at the edge.
        if (FindNamed(input, "ContentElement") is { } content && content.Parent is Grid)
            Grid.SetColumnSpan(content, 4);
    }

    private static FrameworkElement? FindNamed(DependencyObject parent, string name)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement { Name: var n } fe && n == name) return fe;
            if (FindNamed(child, name) is { } found) return found;
        }
        return null;
    }

    /// <summary>
    /// Scrolling the mouse wheel over a money box must never step its amount up or down, so the box does not get to see it.
    /// The page behind it scrolls instead, as it would anywhere else.
    /// </summary>
    private static void IgnoreWheel(object sender, PointerRoutedEventArgs e)
    {
        e.Handled = true;
        var delta = e.GetCurrentPoint(null).Properties.MouseWheelDelta;
        for (var node = VisualTreeHelper.GetParent((DependencyObject)sender); node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is ScrollViewer { ScrollableHeight: > 0, VerticalScrollMode: not ScrollMode.Disabled } viewer)
            {
                viewer.ChangeView(null, viewer.VerticalOffset - delta, null, disableAnimation: true);
                break;
            }
        }
    }

    /// <summary>Typing a point or a comma gives the decimal separator this PC uses, whichever of the two is pressed.</summary>
    private static void DecimalKey(object sender, KeyRoutedEventArgs e)
    {
        if (sender is not TextBox input) return;
        var key = (int)e.Key;
        if (key is not (188 or 190 or 110 or 194)) return;   // comma, full stop, numpad decimal, numpad separator
        var down = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread;
        if (down(Windows.System.VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down)
            || down(Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down)) return;

        var separator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        if (key == 188 && separator == ",") return;   // already what it would type
        if (key == 190 && separator == ".") return;
        e.Handled = true;
        input.SelectedText = separator;
        input.SelectionStart += input.SelectionLength;
        input.SelectionLength = 0;
    }

    internal static TextBox? FindTextBox(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is TextBox box) return box;
            if (FindTextBox(child) is { } found) return found;
        }
        return null;
    }
}

/// <summary>
/// Formats money with two decimals and thousands separators, and reads what was typed with either a point or a comma as the
/// decimal mark. Text such as "1 250,50", "1250.5" or "1,250.50" all give the amount that was meant.
/// </summary>
internal sealed class MoneyFormatter : INumberFormatter2, INumberParser
{
    private readonly DecimalFormatter _inner = new() { FractionDigits = 2, IntegerDigits = 1, IsGrouped = true };

    public string FormatInt(long value) => _inner.FormatInt(value);
    public string FormatUInt(ulong value) => _inner.FormatUInt(value);
    public string FormatDouble(double value) => _inner.FormatDouble(value);

    public long? ParseInt(string text) => ParseDouble(text) is { } d ? (long)Math.Round(d) : null;
    public ulong? ParseUInt(string text) => ParseDouble(text) is { } d and >= 0 ? (ulong)Math.Round(d) : null;

    /// <summary>
    /// An amount, or a sum such as "1 200 + 350", "3000/2" or "(1500-300)*2". Spaces only ever group digits, so they are ignored.
    /// </summary>
    public double? ParseDouble(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = new string(text.Where(c => !char.IsWhiteSpace(c) && c != '\u00A0' && c != '\u202F').ToArray())
            .Replace('\u2212', '-').Replace('\u00D7', '*').Replace('\u00F7', '/');
        if (!t.Skip(1).Any(c => c is '+' or '-' or '*' or '/' or '(' or ')') && !t.StartsWith('(')) return ParseNumber(t);

        var at = 0;
        var result = Sum(t, ref at);
        return result is { } v && at == t.Length && double.IsFinite(v) ? v : null;
    }

    private double? Sum(string t, ref int at)
    {
        var left = Product(t, ref at);
        while (left is not null && at < t.Length && t[at] is '+' or '-')
        {
            var op = t[at++];
            var right = Product(t, ref at);
            if (right is null) return null;
            left = op == '+' ? left + right : left - right;
        }
        return left;
    }

    private double? Product(string t, ref int at)
    {
        var left = Factor(t, ref at);
        while (left is not null && at < t.Length && t[at] is '*' or '/')
        {
            var op = t[at++];
            var right = Factor(t, ref at);
            if (right is null || (op == '/' && right == 0)) return null;
            left = op == '*' ? left * right : left / right;
        }
        return left;
    }

    private double? Factor(string t, ref int at)
    {
        if (at >= t.Length) return null;
        if (t[at] == '-') { at++; return Factor(t, ref at) is { } n ? -n : null; }
        if (t[at] == '+') { at++; return Factor(t, ref at); }
        if (t[at] == '(')
        {
            at++;
            var inner = Sum(t, ref at);
            if (inner is null || at >= t.Length || t[at] != ')') return null;
            at++;
            return inner;
        }
        var start = at;
        while (at < t.Length && (char.IsDigit(t[at]) || t[at] is '.' or ',')) at++;
        return at > start ? ParseNumber(t[start..at]) : null;
    }

    private double? ParseNumber(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = new string(text.Where(c => !char.IsWhiteSpace(c) && c != '\u00A0' && c != '\u202F').ToArray());
        var negative = t.StartsWith('-') || t.StartsWith('\u2212');
        t = t.TrimStart('-', '\u2212', '+');

        var dot = t.LastIndexOf('.');
        var comma = t.LastIndexOf(',');
        int decimalAt;
        if (dot >= 0 && comma >= 0) decimalAt = Math.Max(dot, comma);                       // both marks: the last one is the decimal
        else if (dot < 0 && comma < 0) decimalAt = -1;
        else
        {
            var mark = dot >= 0 ? '.' : ',';
            var count = t.Count(c => c == mark);
            var after = t.Length - Math.Max(dot, comma) - 1;
            var groupsHere = CultureInfo.CurrentCulture.NumberFormat.NumberGroupSeparator.Contains(mark);
            // A mark that appears more than once, or that this PC uses for thousands and has three digits after it, groups digits.
            decimalAt = count > 1 || (groupsHere && after == 3) ? -1 : Math.Max(dot, comma);
        }

        var whole = decimalAt < 0 ? t : t[..decimalAt];
        var fraction = decimalAt < 0 ? "" : t[(decimalAt + 1)..];
        whole = new string(whole.Where(char.IsDigit).ToArray());
        fraction = new string(fraction.Where(char.IsDigit).ToArray());
        if (whole.Length == 0 && fraction.Length == 0) return null;

        var invariant = (whole.Length == 0 ? "0" : whole) + (fraction.Length > 0 ? "." + fraction : "");
        if (!double.TryParse(invariant, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return null;
        return negative ? -value : value;
    }
}
