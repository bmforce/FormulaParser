namespace FormulaParser;

/// <summary>
/// Описание одной формулы, которую нужно посчитать для указанного объекта.
/// </summary>
public sealed class FormulaCalculation
{
    public int? ParameterId { get; init; }

    public required int ObjectId { get; init; }

    public required string Formula { get; init; }
}
