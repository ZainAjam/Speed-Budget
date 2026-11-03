using SpeedBudget.Core;

namespace SpeedBudget.Core.Tests;

public class EmojiCatalogTests
{
    // A few entries in the shape Emojibase publishes: a normal emoji, a component, one with no group, and one out of order.
    private const string Sample = """
    [
      { "label": "flag: South Africa", "hexcode": "1F1FF-1F1E6", "emoji": "🇿🇦", "tags": ["flag"], "order": 3000, "group": 9 },
      { "label": "automobile", "hexcode": "1F697", "emoji": "🚗", "tags": ["car", "driving", "vehicle"], "order": 2000, "group": 5 },
      { "label": "light skin tone", "hexcode": "1F3FB", "emoji": "🏻", "order": 1500, "group": 2 },
      { "label": "regional indicator A", "hexcode": "1F1E6", "emoji": "🇦" },
      { "label": "grinning face", "hexcode": "1F600", "emoji": "😀", "tags": ["face", "grin"], "order": 1, "group": 0 }
    ]
    """;

    [Fact]
    public void Parsing_keeps_real_pictures_in_the_collections_order_and_skips_the_rest()
    {
        var entries = EmojiCatalogData.ParseEmojibase(Sample);

        Assert.Equal(new[] { "😀", "🚗", "🇿🇦" }, entries.Select(e => e.Emoji));
    }

    [Fact]
    public void Names_are_capitalised_and_search_text_includes_tags_and_group()
    {
        var car = EmojiCatalogData.ParseEmojibase(Sample).Single(e => e.Emoji == "🚗");

        Assert.Equal("Automobile", car.Name);
        Assert.Equal("Travel & Places", car.Group);
        Assert.Contains("car", car.Search);
        Assert.Contains("vehicle", car.Search);
        Assert.Contains("travel", car.Search);
    }

    private static EmojiEntry Entry(string emoji, string name, string tags) => new(emoji, name, "Test", $"{name.ToLowerInvariant()} {tags}");

    [Fact]
    public void Search_matches_the_start_of_words_so_car_finds_cars_and_not_scared_faces()
    {
        var entries = new[]
        {
            Entry("😨", "Fearful face", "scared fear"),
            Entry("🚗", "Automobile", "car driving vehicle"),
            Entry("💳", "Credit card", "card payment"),
            Entry("🏎️", "Racing car", "car racing"),
        };

        var found = EmojiCatalogData.Search(entries, "car").Select(e => e.Emoji).ToArray();

        Assert.DoesNotContain("😨", found);
        // Whole-word hits come first, whether in the name or in the keywords, shorter names first; then word-start hits.
        Assert.Equal(new[] { "🚗", "🏎️", "💳" }, found);
    }

    [Fact]
    public void Search_needs_every_word_and_an_empty_search_lists_everything_in_order()
    {
        var entries = new[] { Entry("🚗", "Automobile", "car vehicle"), Entry("🚌", "Bus", "vehicle public transport") };

        Assert.Equal(new[] { "🚌" }, EmojiCatalogData.Search(entries, "vehicle public").Select(e => e.Emoji));
        Assert.Equal(new[] { "🚗", "🚌" }, EmojiCatalogData.Search(entries, "  ").Select(e => e.Emoji));
        Assert.Empty(EmojiCatalogData.Search(entries, "banana"));
    }

    [Fact]
    public void A_cache_round_trips_and_garbage_reads_as_empty()
    {
        var entries = EmojiCatalogData.ParseEmojibase(Sample);

        var back = EmojiCatalogData.FromCache(EmojiCatalogData.ToCache(entries));

        Assert.Equal(entries, back);
        Assert.Empty(EmojiCatalogData.FromCache("not json at all"));
        Assert.Empty(EmojiCatalogData.FromCache("[[\"only one column\"]]"));
    }
}
