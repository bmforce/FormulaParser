using System.Numerics;
namespace FormulaParser;

public interface IFormulaContext<T>
    where T : struct, IFloatingPoint<T>
{
    /// <summary>
    /// Получить значение параметра (скаляр или массив в зависимости от модификатора)
    /// </summary>
    ValueResult<T> GetParameterValue(ParameterInfo parameter, DateTime timestamp);

    /// <summary>
    /// Получить скалярное значение параметра.
    /// </summary>
    T? GetParameterScalar(ParameterInfo parameter, DateTime timestamp);

    /// <summary>
    /// Получить предзагруженный ряд параметра.
    /// </summary>
    ParameterDataSeries<T> GetParameterDataSeries(ParameterInfo parameter);

    /// <summary>
    /// Получить timestamps параметра в указанном диапазоне.
    /// </summary>
    ReadOnlyMemory<DateTime> GetParameterTimestamps(ParameterInfo parameter, DateTime from, DateTime to);

    /// <summary>
    /// Количество дней с начала месяца до указанного timestamp.
    /// </summary>
    int GetMonthDays(DateTime timestamp);
}
