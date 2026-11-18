namespace FormulaParser;

public readonly struct ValueResult
{
    private readonly double _scalarValue;
    private readonly bool _scalarHasValue;
    private readonly double[]? _arrayValue;
    private readonly bool[]? _arrayHasValue;

    public bool IsArray { get; }

    private ValueResult(double? scalarValue)
    {
        _scalarValue = scalarValue.GetValueOrDefault();
        _scalarHasValue = scalarValue.HasValue;
        _arrayValue = null;
        _arrayHasValue = null;
        IsArray = false;
    }

    private ValueResult(double[] arrayValue, bool[] arrayHasValue)
    {
        if (arrayValue.Length != arrayHasValue.Length)
            throw new ArgumentException("Value and presence arrays must have the same length.");

        _scalarValue = 0;
        _scalarHasValue = false;
        _arrayValue = arrayValue;
        _arrayHasValue = arrayHasValue;
        IsArray = true;
    }

    public static ValueResult Scalar(double? value) => new(value);
    public static ValueResult Array(double[] values, bool[] hasValue) => new(values, hasValue);

    public double? AsScalar()
    {
        if (IsArray)
            throw new InvalidOperationException("Cannot convert array to scalar. Use aggregation functions.");
        return _scalarHasValue ? _scalarValue : null;
    }

    public double?[] AsArray()
    {
        if (!IsArray)
            return [_scalarHasValue ? _scalarValue : null];

        var values = _arrayValue!;
        var hasValue = _arrayHasValue!;
        var result = new double?[values.Length];

        for (var i = 0; i < values.Length; i++)
            result[i] = hasValue[i] ? values[i] : null;

        return result;
    }

    /// <summary>
    /// Суммирует все значения без промежуточных аллокаций.
    /// </summary>
    public double? Sum()
    {
        if (!IsArray)
            return AsScalar();

        var arr = _arrayValue!;
        var hasValue = _arrayHasValue!;
        var sum = 0.0;
        var count = 0;
        for (var i = 0; i < arr.Length; i++)
        {
            if (!hasValue[i])
                continue;

            sum += arr[i];
            count++;
        }

        return count == 0 ? null : sum;
    }

    /// <summary>
    /// Возвращает сумму и количество элементов без промежуточных аллокаций.
    /// </summary>
    public (double Sum, int Count) SumAndCount()
    {
        if (!IsArray)
            return _scalarHasValue ? (_scalarValue, 1) : (0, 0);

        var arr = _arrayValue!;
        var hasValue = _arrayHasValue!;
        var sum = 0.0;
        var count = 0;
        for (var i = 0; i < arr.Length; i++)
        {
            if (!hasValue[i])
                continue;

            sum += arr[i];
            count++;
        }

        return (sum, count);
    }

    public override string ToString()
    {
        if (!IsArray)
            return _scalarHasValue ? _scalarValue.ToString() : "null";

        var values = _arrayValue!;
        var hasValue = _arrayHasValue!;
        var parts = new string[values.Length];
        for (var i = 0; i < values.Length; i++)
            parts[i] = hasValue[i] ? values[i].ToString() : "null";

        return $"[{string.Join(", ", parts)}]";
    }
}
