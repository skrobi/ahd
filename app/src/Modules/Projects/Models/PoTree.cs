namespace PzlEv.Modules.Projects.Models;

/// <summary>
/// Drzewo nakładki Performance Objectives w edycji (docs/performance-objectives.md, rozdz. 3): dodawanie elementów
/// i węzłów wirtualnych, przenoszenie, zmiana kolejności, usuwanie. Kolejność rodzeństwa – SortOrder; Level
/// przeliczany z głębokości po każdej zmianie struktury.
/// </summary>
public sealed class PoTree
{
    private readonly List<PoNode> _nodes;
    private long _nextKey = -1;

    public PoTree(IEnumerable<PoNode>? nodes = null)
    {
        _nodes = nodes?.ToList() ?? [];
        if (_nodes.Count > 0)
            _nextKey = Math.Min(-1, _nodes.Min(n => n.Key) - 1);
    }

    public IReadOnlyList<PoNode> Nodes => _nodes;

    public int ElementCount => _nodes.Count(n => !n.IsVirtual);

    public int VirtualCount => _nodes.Count(n => n.IsVirtual);

    public PoNode? Find(long key) => _nodes.FirstOrDefault(n => n.Key == key);

    public IReadOnlyList<PoNode> Children(long? parentKey) =>
        _nodes.Where(n => n.ParentKey == parentKey).OrderBy(n => n.SortOrder).ToList();

    /// <summary>Węzły w kolejności drzewa (w głąb) z głębokością (0 = korzeń).</summary>
    public IReadOnlyList<(PoNode Node, int Depth)> Flatten()
    {
        var result = new List<(PoNode, int)>(_nodes.Count);
        void Walk(long? parent, int depth)
        {
            foreach (var child in Children(parent))
            {
                result.Add((child, depth));
                Walk(child.Key, depth + 1);
            }
        }
        Walk(null, 0);
        return result;
    }

    /// <summary>Węzeł i wszystkie jego potomki.</summary>
    public IReadOnlyList<PoNode> Subtree(long key)
    {
        var result = new List<PoNode>();
        void Walk(PoNode node)
        {
            result.Add(node);
            foreach (var child in Children(node.Key))
                Walk(child);
        }
        if (Find(key) is { } root)
            Walk(root);
        return result;
    }

    public long NewKey() => _nextKey--;

    /// <summary>Dodaje węzeł na końcu dzieci rodzica (null – korzeń).</summary>
    public PoNode Add(PoNode node)
    {
        if (node.ParentKey is { } parent && Find(parent) is null)
            throw new InvalidOperationException("Nie ma węzła nadrzędnego");
        node.SortOrder = Children(node.ParentKey).Select(n => n.SortOrder + 1).DefaultIfEmpty(0).Max();
        _nodes.Add(node);
        Renumber();
        return node;
    }

    public PoNode AddVirtual(long? parentKey, string name) =>
        Add(new PoNode { Key = NewKey(), ParentKey = parentKey, IsVirtual = true, Name = name.Trim() });

    public PoNode AddElement(long? parentKey, string wbsElement, string name, string? legacyWbs) =>
        Add(new PoNode
        {
            Key = NewKey(), ParentKey = parentKey, IsVirtual = false, WbsElement = string.IsNullOrWhiteSpace(wbsElement) ? null : wbsElement.Trim(), Name = name.Trim(),
            LegacyWbs = string.IsNullOrWhiteSpace(legacyWbs) ? null : legacyWbs.Trim(),
        });

    /// <summary>Czy węzeł można przenieść pod nowego rodzica (nie pod siebie ani pod swojego potomka).</summary>
    public bool CanMove(long key, long? newParentKey) =>
        Find(key) is not null && (newParentKey is null || (Find(newParentKey.Value) is not null && Subtree(key).All(n => n.Key != newParentKey)));

    /// <summary>Przenosi węzeł (z poddrzewem) na koniec dzieci nowego rodzica (null – korzeń).</summary>
    public bool Move(long key, long? newParentKey)
    {
        if (!CanMove(key, newParentKey))
            return false;
        var node = Find(key)!;
        if (node.ParentKey == newParentKey)
            return false;
        node.ParentKey = newParentKey;
        node.SortOrder = Children(newParentKey).Where(n => n.Key != key).Select(n => n.SortOrder + 1).DefaultIfEmpty(0).Max();
        Renumber();
        return true;
    }

    /// <summary>Zmienia kolejność wśród rodzeństwa: -1 – wyżej, +1 – niżej.</summary>
    public bool Shift(long key, int direction)
    {
        if (Find(key) is not { } node)
            return false;
        var siblings = Children(node.ParentKey).ToList();
        var index = siblings.IndexOf(node);
        var target = index + Math.Sign(direction);
        if (target < 0 || target >= siblings.Count)
            return false;
        (siblings[index], siblings[target]) = (siblings[target], siblings[index]);
        for (var i = 0; i < siblings.Count; i++)
            siblings[i].SortOrder = i;
        return true;
    }

    /// <summary>Przenosi węzeł poziom wyżej – za swojego rodzica.</summary>
    public bool Outdent(long key)
    {
        if (Find(key) is not { ParentKey: { } parentKey } node)
            return false;
        var parent = Find(parentKey)!;
        var siblings = Children(parent.ParentKey).ToList();
        node.ParentKey = parent.ParentKey;
        siblings.Insert(siblings.IndexOf(parent) + 1, node);
        for (var i = 0; i < siblings.Count; i++)
            siblings[i].SortOrder = i;
        Renumber();
        return true;
    }

    /// <summary>Usuwa węzeł; jego dzieci przechodzą do rodzica usuniętego węzła (w jego miejscu).</summary>
    public bool Remove(long key)
    {
        if (Find(key) is not { } node)
            return false;
        var siblings = Children(node.ParentKey).ToList();
        var index = siblings.IndexOf(node);
        siblings.RemoveAt(index);
        siblings.InsertRange(index, Children(node.Key));
        foreach (var child in Children(node.Key))
            child.ParentKey = node.ParentKey;
        _nodes.Remove(node);
        for (var i = 0; i < siblings.Count; i++)
            siblings[i].SortOrder = i;
        Renumber();
        return true;
    }

    public void Clear() => _nodes.Clear();

    public PoTree Copy() => new(_nodes.Select(n => n.Copy())) { _nextKey = _nextKey };

    /// <summary>Level = głębokość + 1; SortOrder rodzeństwa od 0 bez przerw.</summary>
    private void Renumber()
    {
        foreach (var group in _nodes.GroupBy(n => n.ParentKey))
        {
            var i = 0;
            foreach (var node in group.OrderBy(n => n.SortOrder))
                node.SortOrder = i++;
        }
        foreach (var (node, depth) in Flatten())
            node.Level = depth + 1;
    }
}
