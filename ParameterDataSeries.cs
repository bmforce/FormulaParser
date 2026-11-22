using System.Numerics;
namespace FormulaParser;

/// <summary>
/// Разреженный временной ряд значений параметра.
/// Хранит только реальные timestamps; отсутствие точного DateTime означает null.
/// </summary>
public sealed class ParameterDataSeries<T>
    where T : struct, IFloatingPoint<T>
{
    public required Dictionary<DateTime, T> ValuesByTimestamp { get; init; }
    public required ReadOnlyMemory<DateTime> Timestamps { get; init; }
    public required ReadOnlyMemory<T> Values { get; init; }

    public int Length => Values.Length;

    public static ParameterDataSeries<T> Empty() =>
        new()
        {
            ValuesByTimestamp = [],
            Timestamps = ReadOnlyMemory<DateTime>.Empty,
            Values = ReadOnlyMemory<T>.Empty
        };

    public static ParameterDataSeries<T> FromValues(
        DateTime dateFrom,
        DateTime dateTo,
        IReadOnlyDictionary<DateTime, T> valuesByTimestamp)
    {
        var pairs = new List<KeyValuePair<DateTime, T>>(valuesByTimestamp.Count);
        foreach (var (timestamp, value) in valuesByTimestamp)
        {
            if (timestamp < dateFrom || timestamp > dateTo)
                continue;

            pairs.Add(new KeyValuePair<DateTime, T>(timestamp, value));
        }

        pairs.Sort(static (left, right) => left.Key.CompareTo(right.Key));

        var exactMap = new Dictionary<DateTime, T>(pairs.Count);
        var timestamps = new DateTime[pairs.Count];
        var values = new T[pairs.Count];

        for (var i = 0; i < pairs.Count; i++)
        {
            var (timestamp, value) = pairs[i];
            exactMap[timestamp] = value;
            timestamps[i] = timestamp;
            values[i] = value;
        }

        return new ParameterDataSeries<T>
        {
            ValuesByTimestamp = exactMap,
            Timestamps = timestamps,
            Values = values
        };
    }

    public T? GetValue(DateTime timestamp)
    {
        return ValuesByTimestamp.TryGetValue(timestamp, out var value) ? value : null;
    }

    public (T[] Values, bool[] HasValue) ExtractRange(DateTime from, DateTime to)
    {
        var (startIndex, endExclusive) = GetRangeBounds(from, to);
        var count = endExclusive - startIndex;
        var values = new T[count];
        var hasValue = new bool[count];

        if (count == 0)
            return (values, hasValue);

        Values.Span.Slice(startIndex, count).CopyTo(values);
        Array.Fill(hasValue, true);
        return (values, hasValue);
    }

    public ReadOnlyMemory<DateTime> GetTimestamps(DateTime from, DateTime to)
    {
        var (startIndex, endExclusive) = GetRangeBounds(from, to);
        var count = endExclusive - startIndex;
        return count == 0 ? ReadOnlyMemory<DateTime>.Empty : Timestamps.Slice(startIndex, count);
    }

    internal (int StartIndex, int EndExclusive) GetRangeBounds(DateTime from, DateTime to)
    {
        ThrowIfInvalidRange(from, to);

        var timestamps = Timestamps.Span;
        var startIndex = LowerBound(timestamps, from);
        var endExclusive = UpperBound(timestamps, to);
        return (startIndex, endExclusive);
    }

    private static void ThrowIfInvalidRange(DateTime from, DateTime to)
    {
        if (to < from)
            throw new ArgumentException("Range end must be greater than or equal to range start.");
    }

    private static int LowerBound(ReadOnlySpan<DateTime> values, DateTime target)
    {
        var left = 0;
        var right = values.Length;

        while (left < right)
        {
            var middle = left + ((right - left) / 2);
            if (values[middle] < target)
                left = middle + 1;
            else
                right = middle;
        }

        return left;
    }

    private static int UpperBound(ReadOnlySpan<DateTime> values, DateTime target)
    {
        var left = 0;
        var right = values.Length;

        while (left < right)
        {
            var middle = left + ((right - left) / 2);
            if (values[middle] <= target)
                left = middle + 1;
            else
                right = middle;
        }

        return left;
    }
}
