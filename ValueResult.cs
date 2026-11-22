using System.Numerics;
namespace FormulaParser;

public readonly struct ValueResult<T>
    where T : struct, IFloatingPoint<T>
{
    private readonly T _scalarValue;
    private readonly bool _scalarHasValue;
    private readonly T[]? _arrayValue;
    private readonly bool[]? _arrayHasValue;

    public bool IsArray { get; }

    private ValueResult(T? scalarValue)
    {
        _scalarValue = scalarValue.GetValueOrDefault();
        _scalarHasValue = scalarValue.HasValue;
        _arrayValue = null;
        _arrayHasValue = null;
        IsArray = false;
    }

    private ValueResult(T[] arrayValue, bool[] arrayHasValue)
    {
        if (arrayValue.Length != arrayHasValue.Length)
            throw new ArgumentException("Value and presence arrays must have the same length.");

        _scalarValue = T.Zero;
        _scalarHasValue = false;
        _arrayValue = arrayValue;
        _arrayHasValue = arrayHasValue;
        IsArray = true;
    }

    public static ValueResult<T> Scalar(T? value) => new(value);
    public static ValueResult<T> Array(T[] values, bool[] hasValue) => new(values, hasValue);

    public T? AsScalar()
    {
        if (IsArray)
            throw new InvalidOperationException("Cannot convert array to scalar. Use aggregation functions.");
        return _scalarHasValue ? _scalarValue : null;
    }

    public T?[] AsArray()
    {
        if (!IsArray)
            return [_scalarHasValue ? _scalarValue : null];

        var values = _arrayValue!;
        var hasValue = _arrayHasValue!;
        var result = new T?[values.Length];

        for (var i = 0; i < values.Length; i++)
            result[i] = hasValue[i] ? values[i] : null;

        return result;
    }

    /// <summary>
    /// Суммирует все значения без промежуточных аллокаций.
    /// </summary>
    public T? Sum() => GetAggregate().GetSum();

    /// <summary>Accumulate values with independent counts for composite components.</summary>
    public Aggregate<T> GetAggregate()
    {
        if (!IsArray)
            return Aggregate<T>.FromValue(AsScalar());

        var aggregate = new Aggregate<T>();
        var values = _arrayValue!;
        var hasValue = _arrayHasValue!;
        if (NumericAggregation<T>.Composite == null)
        {
            var sum = T.Zero;
            var count = 0;
            for (var i = 0; i < values.Length; i++)
            {
                if (!hasValue[i]) continue;
                sum += values[i];
                count++;
            }
            return Aggregate<T>.FromScalarSum(sum, count);
        }
        for (var i = 0; i < values.Length; i++)
            if (hasValue[i]) aggregate.Add(values[i]);
        return aggregate;
    }

    public override string ToString()
    {
        if (!IsArray)
            return _scalarHasValue ? _scalarValue.ToString() ?? string.Empty : "null";

        var values = _arrayValue!;
        var hasValue = _arrayHasValue!;
        var parts = new string[values.Length];
        for (var i = 0; i < values.Length; i++)
            parts[i] = hasValue[i] ? values[i].ToString() ?? string.Empty : "null";

        return $"[{string.Join(", ", parts)}]";
    }
}
