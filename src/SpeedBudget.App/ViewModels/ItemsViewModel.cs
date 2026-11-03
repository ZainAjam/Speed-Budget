using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Controls;
using SpeedBudget.Core;
using SpeedBudget.Data;

namespace SpeedBudget.App.ViewModels;

/// <summary>A group or an item in the tree. Edits go straight to the entity and are saved by the owner.</summary>
public sealed partial class NodeVm(GroupEntity? group, ItemEntity? item, Func<Task> save) : ObservableObject
{
    public GroupEntity? Group { get; } = group;
    public ItemEntity? Item { get; } = item;
    public bool IsGroup => Group is not null;
    public Guid Id => Group?.Id ?? Item!.Id;
    public ItemKind Kind => Group?.Kind ?? Item!.Kind;
    public ObservableCollection<NodeVm> Children { get; } = new();

    /// <summary>Remembered by the view model so the tree keeps its shape when it is rebuilt.</summary>
    public Action<Guid, bool>? ExpandedChanged { get; set; }

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (SetProperty(ref _isExpanded, value)) ExpandedChanged?.Invoke(Id, value); }
    }

    public string Name
    {
        get => Group?.Name ?? Item!.Name;
        set
        {
            if (Name == value) return;
            if (Group is not null) Group.Name = value; else Item!.Name = value;
            Changed(nameof(Name), nameof(Display));
        }
    }

    public string Notes
    {
        get => Item?.Notes ?? "";
        set { if (Item is null || Notes == value) return; Item.Notes = value; Changed(nameof(Notes)); }
    }

    public string AccountRef
    {
        get => Item?.AccountRef ?? "";
        set { if (Item is null || AccountRef == value) return; Item.AccountRef = value; Changed(nameof(AccountRef)); }
    }

    /// <summary>The icon shown in front of the item's name, or an empty string for none.</summary>
    public string Emoji
    {
        get => Item?.Emoji ?? "";
        set
        {
            if (Item is null) return;
            var chosen = string.IsNullOrEmpty(value) ? null : value;
            if (Item.Emoji == chosen) return;
            Item.Emoji = chosen;
            Changed(nameof(Emoji), nameof(Display));
        }
    }

    public bool IsPinned
    {
        get => Item?.IsPinned ?? false;
        set { if (Item is null || Item.IsPinned == value) return; Item.IsPinned = value; Changed(nameof(IsPinned)); }
    }

    public bool IsArchived
    {
        get => Item?.IsArchived ?? false;
        set { if (Item is null || Item.IsArchived == value) return; Item.IsArchived = value; Changed(nameof(IsArchived), nameof(Display)); }
    }

    public string Display => IsArchived ? $"{Item?.DisplayName ?? Name} (archived)" : Item?.DisplayName ?? Name;

    public string KindText => Kind == ItemKind.Income ? "Income" : "Expense";

    public void RaiseArchived() { OnPropertyChanged(nameof(IsArchived)); OnPropertyChanged(nameof(Display)); }

    private void Changed(params string[] names)
    {
        foreach (var n in names) OnPropertyChanged(n);
        _ = save();
    }
}

public sealed partial class ItemsViewModel : ObservableObject
{
    private readonly BudgetStore _store = AppServices.Store;
    private readonly HashSet<Guid> _expanded = new();

    public ObservableCollection<NodeVm> Roots { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(IsGroupSelected), nameof(IsItemSelected))]
    public partial NodeVm? Selected { get; set; }

    [ObservableProperty]
    public partial string Message { get; set; } = "";

    public bool HasSelection => Selected is not null;
    public bool IsGroupSelected => Selected?.IsGroup == true;
    public bool IsItemSelected => Selected is { IsGroup: false };
    public bool HasMessage => Message != "";

    partial void OnMessageChanged(string value) => OnPropertyChanged(nameof(HasMessage));

    public async Task LoadAsync(Guid? select = null)
    {
        var groups = await _store.GetGroupsAsync();
        var items = await _store.GetItemsAsync();

        Roots.Clear();
        NodeVm? toSelect = null;
        foreach (var g in groups)
        {
            var groupNode = Make(g, null);
            Roots.Add(groupNode);
            if (g.Id == select) toSelect = groupNode;
            var byParent = items.Where(i => i.GroupId == g.Id).ToLookup(i => i.ParentItemId);
            toSelect = AddChildren(groupNode, byParent, null, select) ?? toSelect;
        }
        Selected = toSelect;
    }

    private NodeVm? AddChildren(NodeVm parent, ILookup<Guid?, ItemEntity> byParent, Guid? parentId, Guid? select)
    {
        NodeVm? found = null;
        foreach (var item in byParent[parentId])
        {
            var node = Make(null, item);
            parent.Children.Add(node);
            if (item.Id == select) found = node;
            found = AddChildren(node, byParent, item.Id, select) ?? found;
        }
        return found;
    }

    private NodeVm Make(GroupEntity? group, ItemEntity? item)
    {
        var node = new NodeVm(group, item, SaveAsync);
        node.IsExpanded = _expanded.Contains(node.Id);
        node.ExpandedChanged = (id, open) => { if (open) _expanded.Add(id); else _expanded.Remove(id); };
        return node;
    }

    public async Task AddGroupAsync(ItemKind kind)
    {
        var group = await _store.AddGroupAsync(kind == ItemKind.Income ? "New income group" : "New expense group", kind);
        await LoadAsync(group.Id);
    }

    /// <summary>Adds an item to the selected group, or a sub-item under the selected item.</summary>
    public async Task AddItemAsync()
    {
        if (Selected is not { } node) return;
        var groupId = node.Group?.Id ?? node.Item!.GroupId;
        var parentId = node.Item?.Id;
        _expanded.Add(node.Id);
        var item = await _store.AddItemAsync(groupId, parentId, "New item", node.Kind);
        await LoadAsync(item.Id);
    }

    public async Task MoveAsync(bool up)
    {
        if (Selected is not { } node) return;
        var moved = node.Group is not null
            ? await _store.MoveGroupAsync(node.Group, up)
            : await _store.MoveItemAsync(node.Item!, up);
        if (moved) await LoadAsync(node.Id);
    }

    /// <summary>
    /// Deletes the selection. Returns a sentence describing what happened when it was not a plain delete,
    /// otherwise null.
    /// </summary>
    public async Task<string?> DeleteSelectedAsync()
    {
        if (Selected is not { } node) return null;
        if (node.Group is not null)
        {
            if (!await _store.DeleteGroupAsync(node.Group))
                return "This group still has items. Delete or move its items first.";
            await LoadAsync();
            return null;
        }

        if (await _store.DeleteOrArchiveItemAsync(node.Item!))
        {
            await LoadAsync();
            return null;
        }
        node.RaiseArchived();
        return "This item is in use, so it was archived instead of deleted.";
    }

    /// <summary>
    /// Saves where the user dragged groups and items to: their order, which group they are in, and which item they
    /// sit under. The tree itself does not update its data, so the new layout is read from its nodes. A drop that
    /// makes no sense is undone with a message.
    /// </summary>
    public async Task SaveStructureAsync(IList<TreeViewNode> roots)
    {
        var changes = new List<Action>();
        string? error = null;

        string? Visit(NodeVm group, IList<TreeViewNode> children, ItemEntity? parent)
        {
            for (var i = 0; i < children.Count; i++)
            {
                if (children[i].Content is not NodeVm { IsGroup: false } child) return "Groups can't go inside other groups.";
                if (child.Kind != group.Kind) return "Income items can only be in income groups, and expense items in expense groups.";
                var index = i;
                changes.Add(() => { child.Item!.GroupId = group.Group!.Id; child.Item.ParentItemId = parent?.Id; child.Item.SortOrder = index; });
                if (Visit(group, children[i].Children, child.Item) is { } nested) return nested;
            }
            return null;
        }

        for (var g = 0; g < roots.Count && error is null; g++)
        {
            if (roots[g].Content is not NodeVm { IsGroup: true } group) { error = "Items must stay inside a group."; break; }
            var index = g;
            changes.Add(() => group.Group!.SortOrder = index);
            error = Visit(group, roots[g].Children, null);
        }

        var keep = Selected?.Id;
        if (error is null)
        {
            try
            {
                foreach (var apply in changes) apply();
                await _store.SaveAsync();
                Message = "";
            }
            catch (Exception ex) { error = $"Could not save: {ex.Message}"; }
        }
        else Message = error;

        // Rebuild from the saved data, which also puts back anything that was rejected.
        await LoadAsync(keep);
    }

    private async Task SaveAsync()
    {
        try { await _store.SaveAsync(); Message = ""; }
        catch (Exception ex) { Message = $"Could not save: {ex.Message}"; }
    }
}
