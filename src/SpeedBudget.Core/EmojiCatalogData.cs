using System.Text.Json;

namespace SpeedBudget.Core;

/// <summary>One emoji with its name, its group, and the lower-case text that searches match against.</summary>
public sealed record EmojiEntry(string Emoji, string Name, string Group, string Search);

/// <summary>
/// Reads the open-source Emojibase collection (https://emojibase.dev, MIT licence): about 1,900 emoji, each with a name
/// and search tags. The same entries are kept in a small cache file so the app works offline after the first download.
/// </summary>
public static class EmojiCatalogData
{
    // Emojibase numbers its groups; 2 is "component" (skin tone and hair swatches), which are not pictures on their own.
    private static readonly string?[] GroupNames =
    {
        "Smileys & Emotion", "People & Body", null, "Animals & Nature", "Food & Drink",
        "Travel & Places", "Activities", "Objects", "Symbols", "Flags",
    };

    /// <summary>Reads Emojibase's <c>en/data.json</c>, in the collection's own order. Entries that are not pictures are skipped.</summary>
    public static IReadOnlyList<EmojiEntry> ParseEmojibase(string json)
    {
        using var document = JsonDocument.Parse(json);
        var entries = new List<(int Order, EmojiEntry Entry)>();

        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (!item.TryGetProperty("emoji", out var emoji) || emoji.GetString() is not { Length: > 0 } glyph) continue;
            if (!item.TryGetProperty("group", out var group) || !group.TryGetInt32(out var groupId)) continue;
            if (groupId < 0 || groupId >= GroupNames.Length || GroupNames[groupId] is not { } groupName) continue;

            var label = item.TryGetProperty("label", out var l) ? l.GetString() ?? "" : "";
            if (label == "") continue;

            var tags = item.TryGetProperty("tags", out var t) && t.ValueKind == JsonValueKind.Array
                ? t.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x != "")
                : Enumerable.Empty<string>();
            var order = item.TryGetProperty("order", out var o) && o.TryGetInt32(out var n) ? n : int.MaxValue;

            var name = char.ToUpperInvariant(label[0]) + label[1..];
            var search = $"{label} {string.Join(' ', tags)} {groupName}".ToLowerInvariant();
            entries.Add((order, new EmojiEntry(glyph, name, groupName, search)));
        }

        return entries.OrderBy(e => e.Order).Select(e => e.Entry).ToList();
    }

    /// <summary>
    /// How well an emoji matches what was typed, lower being better, or null when it does not match. Every typed word must
    /// match the start of a word in the emoji's name or keywords, so "car" finds automobile and card but not "scared".
    /// A whole-word match beats a prefix. Keywords count as much as the name, because the main emoji for a word (the car
    /// for "car") often has it only as a keyword; ties go to the shorter name, then to the collection's own order.
    /// </summary>
    public static int? Score(EmojiEntry entry, IReadOnlyList<string> words)
    {
        var searchTokens = entry.Search.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var total = 0;

        foreach (var word in words)
        {
            int? best = null;
            foreach (var token in searchTokens)
            {
                if (!token.StartsWith(word, StringComparison.Ordinal)) continue;
                var score = token == word ? 0 : 1;
                best = best is null ? score : Math.Min(best.Value, score);
            }
            if (best is null) return null;
            total += best.Value;
        }
        return total;
    }

    /// <summary>The entries that match the typed text, best matches first and otherwise in the collection's order.</summary>
    public static IReadOnlyList<EmojiEntry> Search(IEnumerable<EmojiEntry> entries, string typed, int limit = 400) =>
        Search(entries, e => e, typed, limit);

    /// <summary>The same search over any items that carry an emoji entry, returning the items themselves.</summary>
    public static IReadOnlyList<T> Search<T>(IEnumerable<T> items, Func<T, EmojiEntry> entryOf, string typed, int limit = 400)
    {
        var words = typed.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return items.Take(limit).ToList();

        return items
            .Select((item, index) => (Item: item, Index: index, Score: Score(entryOf(item), words), Length: entryOf(item).Name.Length))
            .Where(x => x.Score is not null)
            .OrderBy(x => x.Score).ThenBy(x => x.Length).ThenBy(x => x.Index)
            .Take(limit)
            .Select(x => x.Item)
            .ToList();
    }
    public static string ToCache(IEnumerable<EmojiEntry> entries) =>
        JsonSerializer.Serialize(entries.Select(e => new[] { e.Emoji, e.Name, e.Group, e.Search }));

    /// <summary>Reads a cache written by <see cref="ToCache"/>. Returns an empty list for anything unreadable.</summary>
    public static IReadOnlyList<EmojiEntry> FromCache(string json)
    {
        try
        {
            var rows = JsonSerializer.Deserialize<string[][]>(json) ?? Array.Empty<string[]>();
            return rows.Where(r => r.Length == 4 && r[0] != "").Select(r => new EmojiEntry(r[0], r[1], r[2], r[3])).ToList();
        }
        catch (JsonException)
        {
            return Array.Empty<EmojiEntry>();
        }
    }
}
