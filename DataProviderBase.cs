using System.Numerics;
namespace FormulaParser;

/// <summary>
/// Общая база для провайдеров данных.
/// Хранит custom-значения, а внешние p.* параметры оставляет конкретному источнику данных.
/// </summary>
public abstract class DataProviderBase<T> : IDataProvider<T>
    where T : struct, IFloatingPoint<T>
{
    private readonly Dictionary<string, T> _customValues = new(StringComparer.Ordinal);
    private readonly Dictionary<ParameterDataKey, string> _formulas = new();
    public virtual Dictionary<ObjectSelector, long[]> ResolveObjects(IReadOnlyCollection<ObjectSelector> selectors) =>
        selectors.Distinct().ToDictionary(selector => selector, _ => Array.Empty<long>());

    public virtual Dictionary<ParameterDataKey, string> GetFormulas(IReadOnlyCollection<ParameterDataKey> parameters)
    {
        var result = new Dictionary<ParameterDataKey, string>();
        foreach (var key in parameters)
            if (_formulas.TryGetValue(key, out var formula))
                result[key] = formula;
        return result;
    }

    public void SetFormula(long objectId, string identifier, string formula)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(formula);
        _formulas[new ParameterDataKey(objectId, identifier)] = formula;
    }

    /// <summary>
    /// Установить или обновить custom-значение без привязки к дате.
    /// </summary>
    public void SetValue(string name, T value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        _customValues[name] = value;
    }

    public Dictionary<string, T> GetCustomValues(IReadOnlyCollection<string> names)
    {
        var result = new Dictionary<string, T>(names.Count, StringComparer.Ordinal);
        foreach (var name in names)
        {
            if (_customValues.TryGetValue(name, out var value))
                result[name] = value;
        }

        return result;
    }

    public abstract Dictionary<ParameterDataKey, ParameterDataSeries<T>> GetData(
        IReadOnlyCollection<DataRequest> requests);
}
