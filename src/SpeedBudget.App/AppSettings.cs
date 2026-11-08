using System.Text.Json;

namespace SpeedBudget.App;

/// <summary>
/// Choices that belong to this installation rather than to a budget (zoom, how the left panel behaves, where updates come from),
/// kept in app-settings.json in the data folder, so they stay the same whichever budget is open.
/// </summary>
public static class AppSettings
{
    private static string FilePath => Path.Combine(AppServices.DataDirectory, "app-settings.json");
    private static Dictionary<string, string>? s_values;

    private static Dictionary<string, string> Values
    {
        get
        {
            if (s_values is not null) return s_values;
            try { s_values = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FilePath)); }
            catch { /* no file yet, or an unreadable one: start empty */ }
            return s_values ??= [];
        }
    }

    public static string? Get(string key) => Values.GetValueOrDefault(key);

    /// <summary>Saves a choice. If the file cannot be written the choice still applies until the app closes.</summary>
    public static void Set(string key, string value)
    {
        Values[key] = value;
        try
        {
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(Values));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch { /* the choice still applies for this run */ }
    }
}
