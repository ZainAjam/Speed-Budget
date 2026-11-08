using System.Globalization;
using System.Text.Json;

namespace SpeedBudget.Data;

/// <summary>
/// One budget on this computer. <see cref="Folder"/> is where its database and backups are, relative to the data folder
/// ("." for a budget that could not be moved out of the data folder itself).
/// </summary>
public sealed record BudgetEntry(Guid Id, string Name, string Folder, DateTime Created);

/// <summary>
/// The list of budgets on this computer, kept in budgets.json in the data folder. Each budget has a folder of its own holding
/// its database (budget.db), its backups and any restore waiting to be applied. Things that belong to the installation rather
/// than to a budget (the device id, window position, icon cache) stay in the data folder itself.
/// </summary>
public sealed class BudgetRegistry
{
    public const string FileName = "budgets.json";
    public const string BudgetsFolderName = "budgets";
    public const string RemovedFolderName = "removed-budgets";
    public const string DefaultFirstName = "My budget";

    private sealed class Stored
    {
        public Guid? LastOpened { get; set; }
        public List<BudgetEntry> Budgets { get; set; } = [];
    }

    private static readonly JsonSerializerOptions s_json = new() { WriteIndented = true };

    private readonly Stored _data;

    private BudgetRegistry(string root, Stored data)
    {
        Root = root;
        _data = data;
    }

    /// <summary>The data folder the list and the budget folders live in.</summary>
    public string Root { get; }

    public IReadOnlyList<BudgetEntry> Budgets => _data.Budgets;

    /// <summary>The budget that was open last, or the first one.</summary>
    public BudgetEntry Current => _data.Budgets.FirstOrDefault(b => b.Id == _data.LastOpened) ?? _data.Budgets[0];

    public string DirectoryOf(BudgetEntry budget) => Path.GetFullPath(Path.Combine(Root, budget.Folder));

    /// <summary>
    /// Reads the list. The first time (no list yet) the budget from before there were several is moved into a folder of its own
    /// and becomes "My budget", or, on a new installation, an empty "My budget" is made. If the files cannot be moved they stay where
    /// they are and the budget is simply listed there, so nothing is ever lost.
    /// </summary>
    public static BudgetRegistry Load(string root)
    {
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, FileName);

        Stored? stored = null;
        if (File.Exists(file))
        {
            try { stored = JsonSerializer.Deserialize<Stored>(File.ReadAllText(file), s_json); }
            catch (JsonException) { /* an unreadable list is rebuilt below from the folders that are there */ }
        }

        var registry = new BudgetRegistry(root, stored ?? new Stored());
        var changed = stored is null;

        if (stored is null) registry.AdoptExisting();
        if (registry._data.Budgets.Count == 0) { registry.AddNew(DefaultFirstName); changed = true; }
        if (changed) registry.Save();
        return registry;
    }

    /// <summary>Lists the budget from before there were several, and any budget folders found without an entry.</summary>
    private void AdoptExisting()
    {
        var legacy = Path.Combine(Root, "budget.db");
        if (File.Exists(legacy))
        {
            var id = Guid.NewGuid();
            var folder = Path.Combine(BudgetsFolderName, id.ToString("N"));
            var entry = new BudgetEntry(id, DefaultFirstName, MoveLegacyInto(folder) ? folder : ".", DateTime.Now);
            _data.Budgets.Add(entry);
            _data.LastOpened = id;
        }

        // A list that was lost or damaged: budgets that have a database in their folder are listed again.
        var budgets = Path.Combine(Root, BudgetsFolderName);
        if (!Directory.Exists(budgets)) return;
        foreach (var dir in Directory.EnumerateDirectories(budgets).OrderBy(d => d, StringComparer.Ordinal))
        {
            if (!Guid.TryParseExact(Path.GetFileName(dir), "N", out var id) || !File.Exists(Path.Combine(dir, "budget.db"))) continue;
            if (_data.Budgets.Any(b => b.Id == id)) continue;
            _data.Budgets.Add(new BudgetEntry(id, $"Budget {_data.Budgets.Count + 1}", Path.Combine(BudgetsFolderName, id.ToString("N")), Directory.GetCreationTime(dir)));
        }
    }

    /// <summary>
    /// Moves the old single budget (database, backups, a restore waiting to be applied) into its own folder. When any part
    /// cannot be moved, what was moved is put back and false is returned.
    /// </summary>
    private bool MoveLegacyInto(string relativeFolder)
    {
        var target = Path.Combine(Root, relativeFolder);
        var moved = new List<(string From, string To)>();
        try
        {
            Directory.CreateDirectory(target);
            foreach (var name in new[] { "budget.db", "budget.db-wal", "budget.db-shm", "restore-pending.db" })
            {
                var from = Path.Combine(Root, name);
                if (!File.Exists(from)) continue;
                var to = Path.Combine(target, name);
                File.Move(from, to);
                moved.Add((from, to));
            }

            var backups = Path.Combine(Root, "backups");
            if (Directory.Exists(backups))
            {
                var to = Path.Combine(target, "backups");
                Directory.Move(backups, to);
                moved.Add((backups, to));
            }
            return true;
        }
        catch
        {
            foreach (var (from, to) in Enumerable.Reverse(moved))
            {
                try
                {
                    if (Directory.Exists(to)) Directory.Move(to, from);
                    else if (File.Exists(to)) File.Move(to, from);
                }
                catch { /* it stays where it is; the budget is listed there */ }
            }
            try { if (Directory.Exists(target) && !Directory.EnumerateFileSystemEntries(target).Any()) Directory.Delete(target); } catch { }
            return false;
        }
    }

    private BudgetEntry AddNew(string name)
    {
        var id = Guid.NewGuid();
        var folder = Path.Combine(BudgetsFolderName, id.ToString("N"));
        Directory.CreateDirectory(Path.Combine(Root, folder));
        var entry = new BudgetEntry(id, name, folder, DateTime.Now);
        _data.Budgets.Add(entry);
        return entry;
    }

    /// <summary>Makes a new, empty budget. Its database is created the first time it is opened.</summary>
    public BudgetEntry Create(string name)
    {
        name = CheckName(name);
        var entry = AddNew(name);
        Save();
        return entry;
    }

    public void Rename(Guid id, string name)
    {
        name = CheckName(name);
        var index = _data.Budgets.FindIndex(b => b.Id == id);
        if (index < 0) throw new InvalidOperationException("That budget is not in the list.");
        _data.Budgets[index] = _data.Budgets[index] with { Name = name };
        Save();
    }

    /// <summary>Remembers which budget to open next time.</summary>
    public void SetCurrent(Guid id)
    {
        if (_data.Budgets.All(b => b.Id != id)) throw new InvalidOperationException("That budget is not in the list.");
        if (_data.LastOpened == id) return;
        _data.LastOpened = id;
        Save();
    }

    /// <summary>
    /// Takes a budget off the list. Its folder is not deleted: it is moved into removed-budgets, so it can still be got back by hand.
    /// The last budget, and one that lives in the data folder itself, cannot be removed.
    /// </summary>
    public void Remove(Guid id)
    {
        var entry = _data.Budgets.FirstOrDefault(b => b.Id == id) ?? throw new InvalidOperationException("That budget is not in the list.");
        if (_data.Budgets.Count == 1) throw new InvalidOperationException("The only budget cannot be removed.");
        if (entry.Folder == ".") throw new InvalidOperationException("This budget is kept in the main data folder and cannot be removed here.");

        var removed = Path.Combine(Root, RemovedFolderName);
        Directory.CreateDirectory(removed);
        var safeName = string.Concat(entry.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var destination = Path.Combine(removed, $"{safeName}-{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}");
        var source = DirectoryOf(entry);
        if (Directory.Exists(source)) Directory.Move(source, destination);

        _data.Budgets.Remove(entry);
        if (_data.LastOpened == id) _data.LastOpened = _data.Budgets[0].Id;
        Save();
    }

    private static string CheckName(string name)
    {
        name = name.Trim();
        if (name.Length == 0) throw new ArgumentException("A budget needs a name.");
        return name;
    }

    private void Save()
    {
        // Written to a temporary file first, so a crash part-way through never leaves a half-written list.
        var file = Path.Combine(Root, FileName);
        var temp = file + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_data, s_json));
        File.Move(temp, file, overwrite: true);
    }
}
