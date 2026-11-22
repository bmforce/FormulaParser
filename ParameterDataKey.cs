namespace FormulaParser;

/// <summary>
/// Внутренний ключ внешнего значения: объект + параметр.
/// </summary>
public readonly record struct ParameterDataKey(long ObjectId, string Identifier)
{
    public override string ToString() => $"obj.{ObjectId}.p.{Identifier}";
}
