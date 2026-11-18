namespace FormulaParser;

public interface IFormulaContext
{
    /// <summary>
    /// Получить значение параметра (скаляр или массив в зависимости от модификатора)
    /// </summary>
    ValueResult GetParameterValue(ParameterInfo parameter, DateTime timestamp);

    /// <summary>
    /// Получить скалярное значение параметра.
    /// </summary>
    double? GetParameterScalar(ParameterInfo parameter, DateTime timestamp);

    /// <summary>
    /// Получить предзагруженный ряд параметра.
    /// </summary>
    ParameterDataSeries GetParameterDataSeries(ParameterInfo parameter);

    /// <summary>
    /// Получить timestamps параметра в указанном диапазоне.
    /// </summary>
    ReadOnlyMemory<DateTime> GetParameterTimestamps(ParameterInfo parameter, DateTime from, DateTime to);

    /// <summary>
    /// Количество дней с начала месяца до указанного timestamp.
    /// </summary>
    int GetMonthDays(DateTime timestamp);
}
