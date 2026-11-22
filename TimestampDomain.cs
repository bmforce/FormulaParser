namespace FormulaParser;

internal readonly struct TimestampDomain
{
    private TimestampDomain(bool isAny, ReadOnlyMemory<DateTime> timestamps)
    {
        IsAny = isAny;
        Timestamps = timestamps;
    }

    public bool IsAny { get; }
    public ReadOnlyMemory<DateTime> Timestamps { get; }

    public static TimestampDomain Any { get; } = new(true, ReadOnlyMemory<DateTime>.Empty);
    public static TimestampDomain Empty { get; } = new(false, ReadOnlyMemory<DateTime>.Empty);

    public static TimestampDomain From(ReadOnlyMemory<DateTime> timestamps) =>
        timestamps.Length == 0 ? Empty : new TimestampDomain(false, timestamps);

    public TimestampDomain Intersect(TimestampDomain other)
    {
        if (IsAny)
            return other;
        if (other.IsAny)
            return this;
        if (Timestamps.Length == 0 || other.Timestamps.Length == 0)
            return Empty;

        var left = Timestamps.Span;
        var right = other.Timestamps.Span;
        var result = new DateTime[Math.Min(left.Length, right.Length)];
        var leftIndex = 0;
        var rightIndex = 0;
        var resultIndex = 0;

        while (leftIndex < left.Length && rightIndex < right.Length)
        {
            var leftValue = left[leftIndex];
            var rightValue = right[rightIndex];

            if (leftValue == rightValue)
            {
                result[resultIndex++] = leftValue;
                leftIndex++;
                rightIndex++;
            }
            else if (leftValue < rightValue)
            {
                leftIndex++;
            }
            else
            {
                rightIndex++;
            }
        }

        return resultIndex == 0
            ? Empty
            : new TimestampDomain(false, result.AsMemory(0, resultIndex));
    }

    public TimestampDomain Union(TimestampDomain other)
    {
        if (IsAny)
            return other;
        if (other.IsAny)
            return this;
        if (Timestamps.Length == 0)
            return other;
        if (other.Timestamps.Length == 0)
            return this;

        var left = Timestamps.Span;
        var right = other.Timestamps.Span;
        var result = new DateTime[left.Length + right.Length];
        var leftIndex = 0;
        var rightIndex = 0;
        var resultIndex = 0;

        while (leftIndex < left.Length && rightIndex < right.Length)
        {
            var leftValue = left[leftIndex];
            var rightValue = right[rightIndex];

            if (leftValue == rightValue)
            {
                result[resultIndex++] = leftValue;
                leftIndex++;
                rightIndex++;
            }
            else if (leftValue < rightValue)
            {
                result[resultIndex++] = leftValue;
                leftIndex++;
            }
            else
            {
                result[resultIndex++] = rightValue;
                rightIndex++;
            }
        }

        while (leftIndex < left.Length)
            result[resultIndex++] = left[leftIndex++];

        while (rightIndex < right.Length)
            result[resultIndex++] = right[rightIndex++];

        return new TimestampDomain(false, result.AsMemory(0, resultIndex));
    }
}
