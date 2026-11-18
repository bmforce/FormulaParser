namespace FormulaParser;

/// <summary>
/// Результат вычисления простой формулы, которая не использует внешние параметры данных.
/// </summary>
public sealed class FormulaEvaluationResult
{
    internal FormulaEvaluationResult(string formula, double? value)
    {
        Formula = formula;
        Value = value;
    }

    public string Formula { get; }

    public double? Value { get; }
}
