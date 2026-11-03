namespace SpeedBudget.App;

/// <summary>One icon a budget item can show in front of its name. Index 0 means no icon.</summary>
public sealed record EmojiChoice(int Index, string Emoji, string Name)
{
    public string Display => Index == 0 ? Name : $"{Emoji}  {Name}";
}

public static class Emojis
{
    public static readonly IReadOnlyList<EmojiChoice> Choices = Build(
        ("🏠", "House"), ("🏡", "Home and garden"), ("🔑", "Rent"), ("🧹", "Cleaning"), ("🛋️", "Furniture"), ("🔌", "Electricity"),
        ("💡", "Lights"), ("💧", "Water"), ("🔥", "Gas and heating"), ("📞", "Telephone"), ("📱", "Cellphone"), ("💻", "Computer"),
        ("🌐", "Internet"), ("📺", "TV"),
        ("🛒", "Groceries"), ("🍞", "Bread and bakery"), ("🥩", "Meat"), ("🍎", "Fruit and veg"), ("🍽️", "Eating out"),
        ("🍔", "Takeaways"), ("☕", "Coffee"), ("🍕", "Pizza"), ("🥤", "Drinks"),
        ("👕", "Clothing"), ("👟", "Shoes"), ("💇", "Hair"), ("🧼", "Toiletries"), ("🧴", "Cosmetics"), ("🎁", "Gifts"),
        ("🎂", "Birthdays"), ("🎈", "Parties"),
        ("🚗", "Car"), ("⛽", "Fuel"), ("🔧", "Repairs"), ("🅿️", "Parking"), ("🚌", "Public transport"), ("🚕", "Taxi"),
        ("✈️", "Flights"), ("🏖️", "Holiday"), ("🏕️", "Camping"),
        ("🏥", "Hospital"), ("💊", "Medicine"), ("🩺", "Doctor"), ("🦷", "Dentist"), ("👓", "Eye care"), ("❤️", "Health"),
        ("🎓", "Education"), ("📚", "Books"), ("✏️", "Stationery"), ("🎒", "School"), ("👶", "Baby"), ("🍼", "Baby supplies"),
        ("🧸", "Toys"),
        ("🐶", "Dog"), ("🐱", "Cat"), ("🎬", "Movies"), ("🎮", "Games"), ("🎵", "Music"), ("⚽", "Sport"), ("🏋️", "Gym"),
        ("💳", "Credit card"), ("🏦", "Bank"), ("💰", "Savings"), ("🪙", "Coins"), ("📈", "Investments"), ("🧾", "Bills"),
        ("🛡️", "Insurance"), ("🔒", "Security"), ("📅", "Subscriptions"), ("🎯", "Goals"), ("🕌", "Mosque and charity"),
        ("🙏", "Donations"), ("🌱", "Garden"), ("⭐", "Favourites"));

    private static List<EmojiChoice> Build(params (string Emoji, string Name)[] choices)
    {
        var list = new List<EmojiChoice> { new(0, "", "No icon") };
        for (var i = 0; i < choices.Length; i++) list.Add(new EmojiChoice(i + 1, choices[i].Emoji, choices[i].Name));
        return list;
    }

    /// <summary>The emoji for a choice index, or null for "No icon".</summary>
    public static string? At(int index) => index <= 0 || index >= Choices.Count ? null : Choices[index].Emoji;

    public static int IndexOf(string? emoji)
    {
        if (string.IsNullOrEmpty(emoji)) return 0;
        return Choices.FirstOrDefault(c => c.Index > 0 && c.Emoji == emoji)?.Index ?? 0;
    }
}
