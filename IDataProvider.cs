namespace FormulaParser;

/// <summary>
/// Интерфейс для загрузки внешних данных и custom-значений одним провайдером.
/// </summary>
public interface IDataProvider
{
    /// <summary>
    /// Установить или обновить custom-значение без привязки к дате.
    /// </summary>
    void SetValue(string name, double value);

    /// <summary>
    /// Получить custom-значения без привязки к дате.
    /// </summary>
    /// <param name="names">Имена custom-значений из формулы</param>
    /// <returns>Словарь: ключ - имя custom-значения, значение - константа</returns>
    Dictionary<string, double> GetCustomValues(IReadOnlyCollection<string> names);

    /// <summary>
    /// Получить внешние данные для всех запрошенных p.* параметров одним вызовом.
    /// </summary>
    /// <param name="requests">Список запросов внешних данных</param>
    /// <returns>Словарь: ключ - объект + параметр, значение - разреженный временной ряд данных</returns>
    Dictionary<ParameterDataKey, ParameterDataSeries> GetData(IReadOnlyCollection<DataRequest> requests);
}
