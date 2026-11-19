using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SpeedBudget.Data;
using Windows.Storage.Pickers;

namespace SpeedBudget.App.Views;

/// <summary>The file pickers and dialogs the tables share for importing from, and exporting to, Excel.</summary>
public static class ExcelFiles
{
    private static void StartWithWindow(object picker) =>
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));

    /// <summary>Asks where to save an Excel file. Null when the user backs out.</summary>
    public static async Task<string?> PickSaveAsync(string suggestedName)
    {
        var picker = new FileSavePicker { SuggestedFileName = suggestedName };
        picker.FileTypeChoices.Add("Excel workbook", new List<string> { ".xlsx" });
        StartWithWindow(picker);
        return (await picker.PickSaveFileAsync())?.Path;
    }

    /// <summary>Asks which Excel file to read. Null when the user backs out.</summary>
    public static async Task<string?> PickOpenAsync()
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".xlsx");
        StartWithWindow(picker);
        return (await picker.PickSingleFileAsync())?.Path;
    }

    /// <summary>Asks which file to import: Excel, CSV or text, or a zip of those. Null when the user backs out.</summary>
    public static async Task<string?> PickImportAsync()
    {
        var picker = new FileOpenPicker();
        foreach (var ext in BankFile.Extensions) picker.FileTypeFilter.Add(ext);
        StartWithWindow(picker);
        return (await picker.PickSingleFileAsync())?.Path;
    }

    public static string ProblemText(IReadOnlyList<ImportProblem> problems)
    {
        var lines = problems.Take(8).Select(p => $"Row {p.Line}: {p.Message}").ToList();
        if (problems.Count > 8) lines.Add($"…and {problems.Count - 8} more.");
        return string.Join("\n", lines);
    }

    public static async Task ShowAsync(XamlRoot root, string title, string message) =>
        await new ContentDialog { XamlRoot = root, Title = title, Content = new ScrollViewer { MaxHeight = 360, Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap } }, CloseButtonText = "OK" }.ShowAsync();

    /// <summary>Shows what an import would do and asks whether to go on.</summary>
    public static async Task<bool> ConfirmAsync(XamlRoot root, string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = title,
            Content = new ScrollViewer { MaxHeight = 360, Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap } },
            PrimaryButtonText = "Import",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
