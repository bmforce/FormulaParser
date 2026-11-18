namespace FormulaParser;

/// <summary>
/// Результат вычисления одной входной строки расчета.
/// </summary>
public sealed class FormulaCalculationResult
{
    internal FormulaCalculationResult(
        int? parameterId,
        int? objectId,
        string formula,
        IReadOnlyList<FormulaCalculationValue> values)
    {
        ParameterId = parameterId;
        ObjectId = objectId;
        Formula = formula;
        Values = values;
    }

    public int? ParameterId { get; }

    public int? ObjectId { get; }

    public string Formula { get; }

    public IReadOnlyList<FormulaCalculationValue> Values { get; }
}

public readonly record struct FormulaCalculationValue(DateTime Date, double? Value);
