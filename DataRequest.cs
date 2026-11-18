namespace FormulaParser;

/// <summary>
/// Запрос внешних данных параметра p.* для вычисления формулы.
/// </summary>
public sealed class DataRequest
{
    public required int ObjectId { get; init; }
    public required string Identifier { get; init; }
    public required DateTime DateFrom { get; init; }
    public required DateTime DateTo { get; init; }

    public ParameterDataKey DataKey => new(ObjectId, Identifier);

    public override bool Equals(object? obj) =>
        obj is DataRequest other &&
        ObjectId == other.ObjectId &&
        Identifier == other.Identifier &&
        DateFrom == other.DateFrom &&
        DateTo == other.DateTo;

    public override int GetHashCode() => 
        HashCode.Combine(ObjectId, Identifier, DateFrom, DateTo);

    public override string ToString() => Format($"obj.{ObjectId}.p.{Identifier}");

    private string Format(string name) =>
        DateFrom == DateTo
            ? $"{name} на {DateFrom:dd.MM.yyyy HH:mm:ss}"
            : $"{name} с {DateFrom:dd.MM.yyyy HH:mm:ss} по {DateTo:dd.MM.yyyy HH:mm:ss}";
}
