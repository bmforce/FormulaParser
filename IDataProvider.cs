using System.Numerics;
namespace FormulaParser;

/// <summary>
/// Интерфейс для загрузки внешних данных и custom-значений одним провайдером.
/// </summary>
public interface IDataProvider<T>
    where T : struct, IFloatingPoint<T>
{
    Dictionary<ObjectSelector, long[]> ResolveObjects(IReadOnlyCollection<ObjectSelector> selectors);

    /// <summary>Получить определения одним пакетом. Отсутствие пары в результате означает обычный ряд данных.</summary>
    Dictionary<ParameterDataKey, string> GetFormulas(IReadOnlyCollection<ParameterDataKey> parameters);

    void SetFormula(long objectId, string identifier, string formula);
    /// <summary>
    /// Установить или обновить custom-значение без привязки к дате.
    /// </summary>
    void SetValue(string name, T value);

    /// <summary>
    /// Получить custom-значения без привязки к дате.
    /// </summary>
    /// <param name="names">Имена custom-значений из формулы</param>
    /// <returns>Словарь: ключ - имя custom-значения, значение - константа</returns>
    Dictionary<string, T> GetCustomValues(IReadOnlyCollection<string> names);

    /// <summary>
    /// Получить внешние данные для всех запрошенных p.* параметров одним вызовом.
    /// </summary>
    /// <param name="requests">Список запросов внешних данных</param>
    /// <returns>Словарь: ключ - объект + параметр, значение - разреженный временной ряд данных</returns>
    Dictionary<ParameterDataKey, ParameterDataSeries<T>> GetData(IReadOnlyCollection<DataRequest> requests);
}
