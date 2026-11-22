namespace FormulaParser;

public enum ObjectDirection { Ancestors, Descendants }

/// <summary>One recursive traversal, filtering the result by object type. Numeric identifiers are IDs.</summary>
public readonly record struct ObjectSelectionStep(ObjectDirection Direction, string ObjectType, string? RelationType = null);

public readonly record struct ObjectSelector(long ObjectId, ObjectDirection Direction, string ObjectType, string? RelationType = null);

internal sealed class ObjectGraph
{
    private readonly record struct Edge(long ParentId, long ChildId, long TypeId, string TypeCodeName);
    private readonly Dictionary<long, (long Id, string Code)> _types = new();
    private readonly Dictionary<long, List<Edge>> _outgoing = new();
    private readonly Dictionary<long, List<Edge>> _incoming = new();

    public void SetObjectType(long objectId, long typeId, string typeCodeName)
    {
        ValidateCode(typeCodeName);
        _types[objectId] = (typeId, typeCodeName);
    }

    public void AddRelation(long parentId, long childId, long typeId, string typeCodeName)
    {
        ValidateCode(typeCodeName);
        var edge = new Edge(parentId, childId, typeId, typeCodeName);
        if (!_outgoing.TryGetValue(parentId, out var outgoing)) _outgoing[parentId] = outgoing = new();
        if (!_incoming.TryGetValue(childId, out var incoming)) _incoming[childId] = incoming = new();
        if (!outgoing.Contains(edge)) { outgoing.Add(edge); incoming.Add(edge); }
    }

    public Dictionary<ObjectSelector, long[]> Resolve(IReadOnlyCollection<ObjectSelector> selectors)
    {
        var result = new Dictionary<ObjectSelector, long[]>();
        foreach (var selector in selectors)
        {
            var visited = new HashSet<long> { selector.ObjectId };
            var pending = new Queue<long>();
            var found = new HashSet<long>();
            pending.Enqueue(selector.ObjectId);
            var edges = selector.Direction == ObjectDirection.Descendants ? _outgoing : _incoming;
            while (pending.TryDequeue(out var current))
            {
                if (!edges.TryGetValue(current, out var adjacent)) continue;
                foreach (var edge in adjacent)
                {
                    if (selector.RelationType != null && !Matches(selector.RelationType, edge.TypeId, edge.TypeCodeName)) continue;
                    var next = selector.Direction == ObjectDirection.Descendants ? edge.ChildId : edge.ParentId;
                    if (!visited.Add(next)) continue;
                    pending.Enqueue(next);
                    if (_types.TryGetValue(next, out var type) && Matches(selector.ObjectType, type.Id, type.Code)) found.Add(next);
                }
            }
            result[selector] = found.Order().ToArray();
        }
        return result;
    }

    private static bool Matches(string identifier, long id, string code) => long.TryParse(identifier, out var numeric)
        ? numeric == id : string.Equals(identifier, code, StringComparison.Ordinal);

    private static void ValidateCode(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        if (!FormulaSyntax.IsIdentifier(code))
            throw new ArgumentException($"Invalid type code '{code}'.", nameof(code));
    }
}
