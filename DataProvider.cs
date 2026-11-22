using System.Numerics;
namespace FormulaParser;

/// <summary>
/// Провайдер данных для ручного наполнения значениями "на лету".
/// Пример: <c>provider.SetParameterValue(objectId, 6, date, 300)</c> или <c>provider.SetParameterValue(objectId, "Oil", date, 300)</c>.
/// Custom-значения задаются через <c>provider.SetValue("customparam", 300)</c>.
/// В формуле обычно несколько параметров — их выбираем скопом одним вызовом.
/// </summary>
public sealed class DataProvider<T> : DataProviderBase<T>
    where T : struct, IFloatingPoint<T>
{
    private readonly Dictionary<ParameterDataKey, Dictionary<DateTime, T>> _values = new();
    private readonly Dictionary<ParameterDataKey, ParameterDataSeries<T>> _seriesCache = new();
    private readonly ObjectGraph _graph = new();

    public void SetObjectType(long objectId, long typeId, string typeCodeName) => _graph.SetObjectType(objectId, typeId, typeCodeName);
    public void AddRelation(long parentId, long childId, long typeId, string typeCodeName) => _graph.AddRelation(parentId, childId, typeId, typeCodeName);
    public override Dictionary<ObjectSelector, long[]> ResolveObjects(IReadOnlyCollection<ObjectSelector> selectors) => _graph.Resolve(selectors);

    /// <summary>
    /// Установить или обновить значение параметра по числовому id для указанного объекта и даты.
    /// </summary>
    public void SetParameterValue(long objectId, int parameterId, DateTime date, T value) =>
        SetParameterValue(
            objectId,
            parameterId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            date,
            value);

    /// <summary>
    /// Установить или обновить значение параметра по code name для указанного объекта и даты.
    /// </summary>
    public void SetParameterValue(long objectId, string codeName, DateTime date, T value)
    {
        ArgumentException.ThrowIfNullOrEmpty(codeName);

        var dataKey = new ParameterDataKey(objectId, codeName);

        if (!_values.TryGetValue(dataKey, out var byDate))
        {
            byDate = new Dictionary<DateTime, T>();
            _values[dataKey] = byDate;
        }

        byDate[date] = value;
        _seriesCache.Remove(dataKey);
    }

    public override Dictionary<ParameterDataKey, ParameterDataSeries<T>> GetData(
        IReadOnlyCollection<DataRequest> requests)
    {
        var result = new Dictionary<ParameterDataKey, ParameterDataSeries<T>>(requests.Count);
        foreach (var request in requests)
        {
            var dataKey = request.DataKey;

            if (!_values.TryGetValue(dataKey, out var paramValues))
            {
                result[dataKey] = ParameterDataSeries<T>.Empty();
                continue;
            }

            result[dataKey] = GetOrBuildSeries(dataKey, paramValues);
        }

        return result;
    }

    private ParameterDataSeries<T> GetOrBuildSeries(
        ParameterDataKey parameterId,
        IReadOnlyDictionary<DateTime, T> valuesByTimestamp)
    {
        if (_seriesCache.TryGetValue(parameterId, out var series))
            return series;

        series = ParameterDataSeries<T>.FromValues(
            DateTime.MinValue,
            DateTime.MaxValue,
            valuesByTimestamp);

        _seriesCache[parameterId] = series;
        return series;
    }
}
