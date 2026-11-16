using System.Globalization;
using ClosedXML.Excel;

namespace SpeedBudget.Data;

/// <summary>One line read from the file. Amounts are in cents; the withdrawal is a positive amount.</summary>
public sealed record ImportRow(int Line, DateOnly Date, long Balance, long Withdrawal, string? Comment);

/// <summary>A line that could not be used, with the spreadsheet row it was on and why.</summary>
public sealed record ImportProblem(int Line, string Message);

public sealed record BalanceImportFile(IReadOnlyList<ImportRow> Rows, IReadOnlyList<ImportProblem> Problems);

/// <summary>What importing would do (or did): lines for dates the tracker does not have yet, and lines that replace the figures of a date it has.</summary>
public sealed record BalanceImportResult(int Added, int Updated);

/// <summary>
/// Reads an Excel file of balances for an account kept by its balances (an export from the app, or a sheet of your own).
/// The headings in the first row are Date, Balance, Withdrawal and Comment.
/// </summary>
public static class BalanceImport
{
    private const string SheetName = "Balances";

    /// <summary>Reads a sheet with Date, Balance, Withdrawal and Comment columns. Lines that cannot be used are reported, not imported.</summary>
    public static BalanceImportFile Read(string path)
    {
        var rows = new List<ImportRow>();
        var problems = new List<ImportProblem>();

        // Opened so that a file that is still open in Excel can be read.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var book = new XLWorkbook(stream);
        var sheet = book.Worksheets.FirstOrDefault(s => s.Name.Equals(SheetName, StringComparison.OrdinalIgnoreCase)) ?? book.Worksheets.First();

        // Find the heading row and which column is which.
        var last = sheet.LastRowUsed()?.RowNumber() ?? 0;
        var headingRow = 0;
        var columns = new Dictionary<string, int>();
        for (var r = 1; r <= Math.Min(last, 20) && headingRow == 0; r++)
        {
            var found = new Dictionary<string, int>();
            foreach (var cell in sheet.Row(r).CellsUsed())
            {
                var name = Normalize(cell.GetString());
                if (name.StartsWith("date")) found.TryAdd("date", cell.Address.ColumnNumber);
                else if (name.StartsWith("balance")) found.TryAdd("balance", cell.Address.ColumnNumber);
                else if (name.StartsWith("withdrawal")) found.TryAdd("withdrawal", cell.Address.ColumnNumber);
                else if (name.StartsWith("comment")) found.TryAdd("comment", cell.Address.ColumnNumber);
            }
            if (found.ContainsKey("date") && found.ContainsKey("balance")) { headingRow = r; columns = found; }
        }
        if (headingRow == 0)
        {
            problems.Add(new ImportProblem(1, "The Date and Balance headings were not found. Put the headings Date, Balance, Withdrawal and Comment in the first row (an export from Speed Budget has them)."));
            return new BalanceImportFile(rows, problems);
        }

        for (var r = headingRow + 1; r <= last; r++)
        {
            var row = sheet.Row(r);
            var dateCell = row.Cell(columns["date"]);
            var balanceCell = row.Cell(columns["balance"]);
            var withdrawalCell = columns.TryGetValue("withdrawal", out var w) ? row.Cell(w) : null;
            var commentText = columns.TryGetValue("comment", out var c) ? row.Cell(c).GetString().Trim() : "";

            // A line with nothing in it is just blank space.
            if (dateCell.IsEmpty() && balanceCell.IsEmpty() && (withdrawalCell?.IsEmpty() ?? true) && commentText == "") continue;

            if (!TryDate(dateCell, out var date)) { problems.Add(new ImportProblem(r, "The date is missing or could not be read.")); continue; }
            if (balanceCell.IsEmpty()) { problems.Add(new ImportProblem(r, $"{date:d MMM yyyy}: there is no balance.")); continue; }
            if (!TryMoney(balanceCell, out var balance)) { problems.Add(new ImportProblem(r, $"{date:d MMM yyyy}: the balance could not be read as an amount.")); continue; }

            long withdrawal = 0;
            if (withdrawalCell is not null && !withdrawalCell.IsEmpty())
            {
                if (!TryMoney(withdrawalCell, out withdrawal)) { problems.Add(new ImportProblem(r, $"{date:d MMM yyyy}: the withdrawal could not be read as an amount.")); continue; }
                withdrawal = Math.Abs(withdrawal);
            }
            rows.Add(new ImportRow(r, date, balance, withdrawal, commentText == "" ? null : commentText));
        }
        return new BalanceImportFile(rows, problems);
    }

    private static string Normalize(string heading) => heading.Trim().ToLowerInvariant();

    private static readonly string[] DateFormats = ["yyyy-MM-dd", "yyyy/MM/dd", "d/M/yyyy", "dd/MM/yyyy", "d MMM yyyy", "d MMMM yyyy", "yyyyMMdd"];

    internal static bool TryDate(IXLCell cell, out DateOnly date)
    {
        date = default;
        if (cell.IsEmpty()) return false;
        if (cell.DataType == XLDataType.DateTime) { date = DateOnly.FromDateTime(cell.GetDateTime()); return true; }
        if (cell.DataType == XLDataType.Number)
        {
            // An Excel date that arrived as a plain number (days since 1900).
            var serial = cell.GetDouble();
            if (serial is < 1 or > 2958465) return false;
            date = DateOnly.FromDateTime(DateTime.FromOADate(serial));
            return true;
        }
        var text = cell.GetString().Trim();
        if (DateTime.TryParseExact(text, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact)) { date = DateOnly.FromDateTime(exact); return true; }
        if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out var parsed)) { date = DateOnly.FromDateTime(parsed); return true; }
        return false;
    }

    internal static bool TryMoney(IXLCell cell, out long cents)
    {
        cents = 0;
        if (cell.DataType == XLDataType.Number) { cents = (long)Math.Round(cell.GetDouble() * 100, MidpointRounding.AwayFromZero); return true; }
        return TryMoney(cell.GetString(), out cents);
    }

    /// <summary>Reads an amount typed as text: with spaces or commas between thousands (1 580 316.21, 1,582,539.55) and a point or comma for the cents.</summary>
    public static bool TryMoney(string text, out long cents)
    {
        cents = 0;
        var s = text.Trim().Replace("R", "", StringComparison.OrdinalIgnoreCase).Replace(" ", "").Replace(" ", "").Replace("'", "");
        if (s == "") return false;

        var comma = s.LastIndexOf(',');
        var point = s.LastIndexOf('.');
        if (comma >= 0 && point >= 0)
            s = comma > point ? s.Replace(".", "").Replace(',', '.') : s.Replace(",", "");
        else if (comma >= 0)
            s = System.Text.RegularExpressions.Regex.IsMatch(s, @"^-?\d+,\d{1,2}$") ? s.Replace(',', '.') : s.Replace(",", "");

        if (!decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)) return false;
        cents = (long)Math.Round(value * 100, MidpointRounding.AwayFromZero);
        return true;
    }
}
