using System.Numerics;
namespace FormulaParser;

/// <summary>
/// Результат вычисления простой формулы, которая не использует внешние параметры данных.
/// </summary>
public sealed class FormulaEvaluationResult<T>
    where T : struct, IFloatingPoint<T>
{
    internal FormulaEvaluationResult(string formula, T? value)
    {
        Formula = formula;
        Value = value;
    }

    public string Formula { get; }

    public T? Value { get; }
}
