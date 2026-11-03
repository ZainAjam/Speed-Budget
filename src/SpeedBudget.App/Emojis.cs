namespace SpeedBudget.App;

/// <summary>The icons a budget item can show in front of its name. The first choice means no icon.</summary>
public static class Emojis
{
    public static readonly IReadOnlyList<string> Choices = new[]
    {
        "No icon",
        "🏠", "🏡", "🔑", "🧹", "🛋️", "🔌", "💡", "💧", "🔥", "📞", "📱", "💻", "🌐", "📺",
        "🛒", "🍞", "🥩", "🍎", "🍽️", "🍔", "☕", "🍕", "🥤",
        "👕", "👟", "💇", "🧼", "🧴", "🎁", "🎂", "🎈",
        "🚗", "⛽", "🔧", "🅿️", "🚌", "🚕", "✈️", "🏖️", "🏕️",
        "🏥", "💊", "🩺", "🦷", "👓", "❤️",
        "🎓", "📚", "✏️", "🎒", "👶", "🍼", "🧸",
        "🐶", "🐱", "🎬", "🎮", "🎵", "⚽", "🏋️",
        "💳", "🏦", "💰", "🪙", "📈", "🧾", "🛡️", "🔒", "📅", "🎯", "🕌", "🙏", "🌱", "⭐",
    };

    /// <summary>The emoji for a choice index, or null for "No icon".</summary>
    public static string? At(int index) => index <= 0 || index >= Choices.Count ? null : Choices[index];

    public static int IndexOf(string? emoji)
    {
        if (string.IsNullOrEmpty(emoji)) return 0;
        for (var i = 1; i < Choices.Count; i++)
            if (Choices[i] == emoji) return i;
        return 0;
    }
}
