namespace FormulaParser;

/// <summary>
/// Провайдер данных для ручного наполнения значениями "на лету".
/// Пример: <c>provider.SetParameterValue(objectId, 6, date, 300)</c> или <c>provider.SetParameterValue(objectId, "Oil", date, 300)</c>.
/// Custom-значения задаются через <c>provider.SetValue("customparam", 300)</c>.
/// В формуле обычно несколько параметров — их выбираем скопом одним вызовом.
/// </summary>
public sealed class DataProvider : DataProviderBase
{
    private readonly Dictionary<ParameterDataKey, Dictionary<DateTime, double>> _values = new();
    private readonly Dictionary<ParameterDataKey, ParameterDataSeries> _seriesCache = new();

    /// <summary>
    /// Установить или обновить значение параметра по числовому id для указанного объекта и даты.
    /// </summary>
    public void SetParameterValue(int objectId, int parameterId, DateTime date, double value) =>
        SetParameterValue(
            objectId,
            parameterId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            date,
            value);

    /// <summary>
    /// Установить или обновить значение параметра по code name для указанного объекта и даты.
    /// </summary>
    public void SetParameterValue(int objectId, string codeName, DateTime date, double value)
    {
        ArgumentException.ThrowIfNullOrEmpty(codeName);

        var dataKey = new ParameterDataKey(objectId, codeName);

        if (!_values.TryGetValue(dataKey, out var byDate))
        {
            byDate = new Dictionary<DateTime, double>();
            _values[dataKey] = byDate;
        }

        byDate[date] = value;
        _seriesCache.Remove(dataKey);
    }

    public override Dictionary<ParameterDataKey, ParameterDataSeries> GetData(
        IReadOnlyCollection<DataRequest> requests)
    {
        var result = new Dictionary<ParameterDataKey, ParameterDataSeries>(requests.Count);
        foreach (var request in requests)
        {
            var dataKey = request.DataKey;

            if (!_values.TryGetValue(dataKey, out var paramValues))
            {
                result[dataKey] = ParameterDataSeries.Empty();
                continue;
            }

            result[dataKey] = GetOrBuildSeries(dataKey, paramValues);
        }

        return result;
    }

    private ParameterDataSeries GetOrBuildSeries(
        ParameterDataKey parameterId,
        IReadOnlyDictionary<DateTime, double> valuesByTimestamp)
    {
        if (_seriesCache.TryGetValue(parameterId, out var series))
            return series;

        series = ParameterDataSeries.FromValues(
            DateTime.MinValue,
            DateTime.MaxValue,
            valuesByTimestamp);

        _seriesCache[parameterId] = series;
        return series;
    }
}
