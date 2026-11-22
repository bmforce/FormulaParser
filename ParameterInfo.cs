namespace FormulaParser;

public enum ParameterKind
{
    Parameter,
    Custom
}

public sealed class ParameterInfo
{
    public ParameterKind Kind { get; init; } = ParameterKind.Parameter;
    public long? ObjectId { get; init; }
    public required string ParameterId { get; init; }
    public string? Modifier { get; init; }
    public ObjectSelectionStep? Ancestor { get; init; }
    public ObjectSelectionStep? Descendant { get; init; }
    public bool HasSelection => Ancestor.HasValue || Descendant.HasValue;

    public override bool Equals(object? obj) =>
        obj is ParameterInfo other &&
        Kind == other.Kind &&
        ObjectId == other.ObjectId &&
        ParameterId == other.ParameterId &&
        Modifier == other.Modifier && Ancestor == other.Ancestor && Descendant == other.Descendant;

    public override int GetHashCode() => HashCode.Combine(Kind, ObjectId, ParameterId, Modifier, Ancestor, Descendant);

    public override string ToString() =>
        Kind == ParameterKind.Parameter
            ? FormatParameter()
            : Modifier != null ? $"[{ParameterId}]:{Modifier}" : $"[{ParameterId}]";

    private string FormatParameter()
    {
        var prefix = ObjectId.HasValue ? $"{FormulaKeywords.OBJ_PREFIX}{ObjectId.Value}." : string.Empty;
        foreach (var step in new[] { Ancestor, Descendant })
            if (step is { } selection)
            {
                prefix += selection.Direction == ObjectDirection.Ancestors ? FormulaKeywords.ANCESTORS : FormulaKeywords.DESCENDANTS;
                prefix += "." + (selection.RelationType == null ? string.Empty : selection.RelationType + ".") + selection.ObjectType + ".";
            }
        var name = prefix + FormulaKeywords.PARAMETER_PREFIX + ParameterId;

        return Modifier != null ? $"[{name}]:{Modifier}" : $"[{name}]";
    }
}
