namespace FormulaParser;

public enum ParameterKind
{
    Parameter,
    Custom
}

public sealed class ParameterInfo
{
    public ParameterKind Kind { get; init; } = ParameterKind.Parameter;
    public int? ObjectId { get; init; }
    public required string ParameterId { get; init; }
    public string? Modifier { get; init; }

    public override bool Equals(object? obj) =>
        obj is ParameterInfo other &&
        Kind == other.Kind &&
        ObjectId == other.ObjectId &&
        ParameterId == other.ParameterId &&
        Modifier == other.Modifier;

    public override int GetHashCode() => HashCode.Combine(Kind, ObjectId, ParameterId, Modifier);

    public override string ToString() =>
        Kind == ParameterKind.Parameter
            ? FormatParameter()
            : Modifier != null ? $"[{ParameterId}:{Modifier}]" : $"[{ParameterId}]";

    private string FormatParameter()
    {
        var name = ObjectId.HasValue
            ? $"{FormulaKeywords.OBJ_PREFIX}{ObjectId.Value}{FormulaKeywords.OBJECT_PARAMETER_SEPARATOR}{ParameterId}"
            : $"{FormulaKeywords.PARAMETER_PREFIX}{ParameterId}";

        return Modifier != null ? $"[{name}:{Modifier}]" : $"[{name}]";
    }
}
