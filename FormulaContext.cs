namespace FormulaParser;

/// <summary>
/// Контекст с предзагруженными данными
/// </summary>
public sealed class FormulaContext : IFormulaContext
{
    private const int LinearParameterLookupThreshold = 2;

    private readonly Dictionary<ParameterDataKey, ParameterDataSeries> _data;
    private readonly (ParameterDataKey DataKey, ParameterDataSeries Series)[] _paramItems;
    private readonly Dictionary<ParameterDataKey, ParameterDataSeries>? _paramIndex;
    private readonly Dictionary<string, double> _customValues;
    private readonly int _objectId;

    // Внутренний доступ к предзагруженным данным.
    internal Dictionary<ParameterDataKey, ParameterDataSeries> Data => _data;

    public FormulaContext(
        Dictionary<ParameterDataKey, ParameterDataSeries> data,
        Dictionary<string, double> customValues)
    {
        _data = data;
        _customValues = customValues;
        _objectId = 0;

        _paramItems = new (ParameterDataKey DataKey, ParameterDataSeries Series)[data.Count];

        var index = 0;
        foreach (var (dataKey, series) in data)
            _paramItems[index++] = (dataKey, series);

        if (data.Count <= LinearParameterLookupThreshold)
            return;

        _paramIndex = new(data.Count);
        foreach (var (dataKey, series) in _paramItems)
            _paramIndex[dataKey] = series;
    }

    private FormulaContext(
        Dictionary<ParameterDataKey, ParameterDataSeries> data,
        Dictionary<string, double> customValues,
        (ParameterDataKey DataKey, ParameterDataSeries Series)[] paramItems,
        Dictionary<ParameterDataKey, ParameterDataSeries>? paramIndex,
        int objectId)
    {
        _data = data;
        _customValues = customValues;
        _paramItems = paramItems;
        _paramIndex = paramIndex;
        _objectId = objectId;
    }

    public FormulaContext ForObject(int objectId) =>
        objectId == _objectId
            ? this
            : new FormulaContext(_data, _customValues, _paramItems, _paramIndex, objectId);

    public ValueResult GetParameterValue(ParameterInfo parameter, DateTime timestamp)
    {
        if (parameter.Kind == ParameterKind.Custom)
            return ValueResult.Scalar(GetCustomValue(parameter));

        var series = GetSeries(parameter);
        if (parameter.Modifier == null)
            return ValueResult.Scalar(series.GetValue(timestamp));

        var modifier = parameter.Modifier;
        var monthBegin = new DateTime(timestamp.Year, timestamp.Month, 1);
        var yearBegin = new DateTime(timestamp.Year, 1, 1);

        if (modifier.Equals(FormulaKeywords.MONTH_BEGIN, StringComparison.OrdinalIgnoreCase))
            return ValueResult.Scalar(series.GetValue(monthBegin));

        if (modifier.Equals(FormulaKeywords.MONTH_NOW, StringComparison.OrdinalIgnoreCase))
            return CreateArrayValue(series, monthBegin, timestamp);

        if (modifier.Equals(FormulaKeywords.YEAR_NOW, StringComparison.OrdinalIgnoreCase))
            return CreateArrayValue(series, yearBegin, timestamp);

        throw new InvalidOperationException($"Unknown modifier: {modifier}");
    }

    public double? GetParameterScalar(ParameterInfo parameter, DateTime timestamp)
    {
        if (parameter.Kind == ParameterKind.Custom)
            return GetCustomValue(parameter);

        var series = GetSeries(parameter);
        if (parameter.Modifier == null)
            return series.GetValue(timestamp);

        var modifier = parameter.Modifier;
        if (modifier.Equals(FormulaKeywords.MONTH_BEGIN, StringComparison.OrdinalIgnoreCase))
        {
            var monthBegin = new DateTime(timestamp.Year, timestamp.Month, 1);
            return series.GetValue(monthBegin);
        }

        if (modifier.Equals(FormulaKeywords.MONTH_NOW, StringComparison.OrdinalIgnoreCase) ||
            modifier.Equals(FormulaKeywords.YEAR_NOW, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Cannot convert array to scalar. Use aggregation functions.");
        }

        throw new InvalidOperationException($"Unknown modifier: {modifier}");
    }

    public ParameterDataSeries GetParameterDataSeries(ParameterInfo parameter) => GetSeries(parameter);

    public ReadOnlyMemory<DateTime> GetParameterTimestamps(ParameterInfo parameter, DateTime from, DateTime to)
    {
        return GetSeries(parameter).GetTimestamps(from, to);
    }

    public int GetMonthDays(DateTime timestamp)
    {
        var monthBegin = new DateTime(timestamp.Year, timestamp.Month, 1);
        return (timestamp - monthBegin).Days + 1;
    }

    private static ValueResult CreateArrayValue(ParameterDataSeries series, DateTime targetStart, DateTime targetEnd)
    {
        var (values, hasValue) = series.ExtractRange(targetStart, targetEnd);
        return ValueResult.Array(values, hasValue);
    }

    private double? GetCustomValue(ParameterInfo parameter) =>
        _customValues.TryGetValue(parameter.ParameterId, out var value) ? value : null;

    private ParameterDataSeries GetSeries(ParameterInfo parameter)
    {
        var objectId = parameter.ObjectId ?? _objectId;
        var dataKey = new ParameterDataKey(objectId, parameter.ParameterId);

        if (_paramIndex != null)
        {
            if (_paramIndex.TryGetValue(dataKey, out var indexedSeries))
                return indexedSeries;

            throw new InvalidOperationException($"Parameter {parameter} not found in data");
        }

        for (var i = _paramItems.Length - 1; i >= 0; i--)
        {
            var (currentId, series) = _paramItems[i];
            if (currentId == dataKey)
                return series;
        }

        throw new InvalidOperationException($"Parameter {parameter} not found in data");
    }
}
