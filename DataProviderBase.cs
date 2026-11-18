namespace FormulaParser;

/// <summary>
/// Общая база для провайдеров данных.
/// Хранит custom-значения, а внешние p.* параметры оставляет конкретному источнику данных.
/// </summary>
public abstract class DataProviderBase : IDataProvider
{
    private readonly Dictionary<string, double> _customValues = new(StringComparer.Ordinal);

    /// <summary>
    /// Установить или обновить custom-значение без привязки к дате.
    /// </summary>
    public void SetValue(string name, double value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        _customValues[name] = value;
    }

    public Dictionary<string, double> GetCustomValues(IReadOnlyCollection<string> names)
    {
        var result = new Dictionary<string, double>(names.Count, StringComparer.Ordinal);
        foreach (var name in names)
        {
            if (_customValues.TryGetValue(name, out var value))
                result[name] = value;
        }

        return result;
    }

    public abstract Dictionary<ParameterDataKey, ParameterDataSeries> GetData(
        IReadOnlyCollection<DataRequest> requests);
}
