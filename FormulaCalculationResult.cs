using System.Numerics;
namespace FormulaParser;

/// <summary>
/// Результат вычисления одной входной строки расчета.
/// </summary>
public sealed class FormulaCalculationResult<T>
    where T : struct, IFloatingPoint<T>
{
    internal FormulaCalculationResult(
        int? parameterId,
        long? objectId,
        string formula,
        IReadOnlyList<FormulaCalculationValue<T>> values)
    {
        ParameterId = parameterId;
        ObjectId = objectId;
        Formula = formula;
        Values = values;
    }

    public int? ParameterId { get; }

    public long? ObjectId { get; }

    public string Formula { get; }

    public IReadOnlyList<FormulaCalculationValue<T>> Values { get; }
}

public readonly record struct FormulaCalculationValue<T>(DateTime Date, T? Value)
    where T : struct, IFloatingPoint<T>;
