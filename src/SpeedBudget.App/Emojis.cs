using System.Net.Http;
using Microsoft.UI.Dispatching;
using SpeedBudget.Core;

namespace SpeedBudget.App;

/// <summary>One icon a budget item can show in front of its name. An empty Emoji means no icon.</summary>
public sealed class EmojiChoice(EmojiEntry entry)
{
    public EmojiEntry Entry { get; } = entry;
    public string Emoji => Entry.Emoji;
    public string Name => Entry.Name;
    public string Group => Entry.Group;
    public string Display => Emoji == "" ? Name : $"{Emoji}  {Name}";
}

/// <summary>
/// The icons offered for budget items. The full list, about 1,900 emoji with names and search tags, comes from the
/// open-source Emojibase collection (MIT licence) and is kept in a cache file, so it downloads once and then works
/// offline. Until it arrives, or if it never can, a short built-in list is used.
/// </summary>
public static class Emojis
{
    private const string SourceUrl = "https://cdn.jsdelivr.net/npm/emojibase-data@18.0.0/en/data.json";
    private static readonly TimeSpan RefreshAfter = TimeSpan.FromDays(60);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly EmojiChoice None = new(new EmojiEntry("", "No icon", "", "no icon none"));

    private static Dictionary<string, EmojiChoice> _byEmoji = new();

    /// <summary>"No icon", then every emoji in the collection's own order.</summary>
    public static IReadOnlyList<EmojiChoice> Choices { get; private set; } = Array.Empty<EmojiChoice>();

    /// <summary>False while only the short built-in list is available.</summary>
    public static bool IsFullCollection { get; private set; }

    /// <summary>Raised on the UI thread when the full collection has been loaded.</summary>
    public static event Action? Changed;

    /// <summary>Set once at start-up so a load that finishes on another thread can safely tell the UI.</summary>
    public static DispatcherQueue? UiQueue { get; set; }

    static Emojis() => Apply(BuiltIn(), full: false);

    public static EmojiChoice? Find(string? emoji) =>
        string.IsNullOrEmpty(emoji) ? null : _byEmoji.GetValueOrDefault(emoji);

    /// <summary>
    /// Loads the cached collection if there is one, and downloads a fresh copy when there is none or it is old.
    /// A failed download is not an error: the cache, or the built-in list, carries on.
    /// </summary>
    public static async Task LoadAsync(string directory)
    {
        var cache = Path.Combine(directory, "emoji-catalog.json");
        var haveCache = false;

        try
        {
            if (File.Exists(cache) && EmojiCatalogData.FromCache(await File.ReadAllTextAsync(cache)) is { Count: > 500 } cached)
            {
                Apply(cached, full: true);
                haveCache = true;
                if (File.GetLastWriteTimeUtc(cache) > DateTime.UtcNow - RefreshAfter) return;
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Emoji cache unreadable: {ex.Message}"); }

        try
        {
            var entries = EmojiCatalogData.ParseEmojibase(await Http.GetStringAsync(SourceUrl));
            if (entries.Count < 500) return;
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(cache, EmojiCatalogData.ToCache(entries));
            Apply(entries, full: true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Emoji download failed ({(haveCache ? "keeping the cache" : "using the built-in list")}): {ex.Message}");
        }
    }

    private static void Apply(IEnumerable<EmojiEntry> entries, bool full)
    {
        var list = new List<EmojiChoice> { None };
        list.AddRange(entries.Select(e => new EmojiChoice(e)));
        _byEmoji = list.Where(c => c.Emoji != "").GroupBy(c => c.Emoji).ToDictionary(g => g.Key, g => g.First());
        Choices = list;
        IsFullCollection = full;

        if (full)
        {
            if (UiQueue is { } queue) queue.TryEnqueue(() => Changed?.Invoke());
            else Changed?.Invoke();
        }
    }

    private static IEnumerable<EmojiEntry> BuiltIn()
    {
        (string Emoji, string Name)[] common =
        {
            ("🏠", "House"), ("🏡", "Home and garden"), ("🔑", "Rent"), ("🧹", "Cleaning"), ("🛋️", "Furniture"), ("🔌", "Electricity"),
            ("💡", "Lights"), ("💧", "Water"), ("🔥", "Gas and heating"), ("📞", "Telephone"), ("📱", "Cellphone"), ("💻", "Computer"),
            ("🌐", "Internet"), ("📺", "TV"), ("🛒", "Groceries"), ("🍞", "Bread and bakery"), ("🥩", "Meat"), ("🍎", "Fruit and veg"),
            ("🍽️", "Eating out"), ("🍔", "Takeaways"), ("☕", "Coffee"), ("🍕", "Pizza"), ("🥤", "Drinks"), ("👕", "Clothing"),
            ("👟", "Shoes"), ("💇", "Hair"), ("🧼", "Toiletries"), ("🧴", "Cosmetics"), ("🎁", "Gifts"), ("🎂", "Birthdays"),
            ("🎈", "Parties"), ("🚗", "Car"), ("⛽", "Fuel"), ("🔧", "Repairs"), ("🅿️", "Parking"), ("🚌", "Public transport"),
            ("🚕", "Taxi"), ("✈️", "Flights"), ("🏖️", "Holiday"), ("🏕️", "Camping"), ("🏥", "Hospital"), ("💊", "Medicine"),
            ("🩺", "Doctor"), ("🦷", "Dentist"), ("👓", "Eye care"), ("❤️", "Health"), ("🎓", "Education"), ("📚", "Books"),
            ("✏️", "Stationery"), ("🎒", "School"), ("👶", "Baby"), ("🍼", "Baby supplies"), ("🧸", "Toys"), ("🐶", "Dog"),
            ("🐱", "Cat"), ("🎬", "Movies"), ("🎮", "Games"), ("🎵", "Music"), ("⚽", "Sport"), ("🏋️", "Gym"), ("💳", "Credit card"),
            ("🏦", "Bank"), ("💰", "Savings"), ("🪙", "Coins"), ("📈", "Investments"), ("🧾", "Bills"), ("🛡️", "Insurance"),
            ("🔒", "Security"), ("📅", "Subscriptions"), ("🎯", "Goals"), ("🕌", "Mosque and charity"), ("🙏", "Donations"),
            ("🌱", "Garden"), ("⭐", "Favourites"),
        };
        return common.Select(c => new EmojiEntry(c.Emoji, c.Name, "Built-in", $"{c.Name.ToLowerInvariant()} built-in"));
    }
}
