using System.Numerics;
using System.Runtime.CompilerServices;

namespace FormulaParser;

/// <summary>Accumulation state; component counts remain independent for composite numbers.</summary>
public struct Aggregate<T> : IEquatable<Aggregate<T>> where T : struct, IFloatingPoint<T>
{
    private T _sum;
    public int FirstCount { get; private set; }
    public int SecondCount { get; private set; }
    public bool HasValue => FirstCount != 0 || SecondCount != 0;

    public Aggregate() => _sum = T.Zero;

    internal static Aggregate<T> FromScalarSum(T sum, int count) => new() { _sum = sum, FirstCount = count };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(T value)
    {
        if (NumericAggregation<T>.Composite is { } composite)
        {
            _sum = composite.Add(_sum, value);
            var (first, second) = composite.GetCounts(value);
            FirstCount += first;
            SecondCount += second;
        }
        else
        {
            _sum += value;
            FirstCount++;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Merge(Aggregate<T> other)
    {
        if (!other.HasValue) return;
        _sum = NumericAggregation<T>.Composite is { } composite
            ? composite.Add(_sum, other._sum) : _sum + other._sum;
        FirstCount += other.FirstCount;
        SecondCount += other.SecondCount;
    }

    public T? GetSum() => !HasValue ? null : NumericAggregation<T>.Composite is { } composite
        ? composite.GetSum(_sum, FirstCount, SecondCount) : _sum;

    public T? GetAverage() => !HasValue ? null : NumericAggregation<T>.Composite is { } composite
        ? composite.GetAverage(_sum, FirstCount, SecondCount) : _sum / T.CreateChecked(FirstCount);

    public Aggregate<T> Scale(T factor)
    {
        if (!HasValue) return this;
        if (NumericAggregation<T>.Composite is { } composite)
        {
            var (first, second) = composite.GetCounts(factor);
            FirstCount *= first;
            SecondCount *= second;
        }
        _sum *= factor;
        return this;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Aggregate<T> FromValue(T? value)
    {
        var aggregate = new Aggregate<T>();
        if (value.HasValue) aggregate.Add(value.Value);
        return aggregate;
    }

    public bool Equals(Aggregate<T> other) => FirstCount == other.FirstCount &&
        SecondCount == other.SecondCount && EqualityComparer<T>.Default.Equals(_sum, other._sum);
    public override bool Equals(object? obj) => obj is Aggregate<T> other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(_sum, FirstCount, SecondCount);
    public static bool operator ==(Aggregate<T> left, Aggregate<T> right) => left.Equals(right);
    public static bool operator !=(Aggregate<T> left, Aggregate<T> right) => !left.Equals(right);
}

internal interface ICompositeAggregation<T> where T : struct
{
    T Add(T left, T right);
    (int First, int Second) GetCounts(T value);
    T GetSum(T sum, int firstCount, int secondCount);
    T GetAverage(T sum, int firstCount, int secondCount);
    T FromStorage(decimal? firstValue, decimal? secondValue);
}

internal static class NumericAggregation<T> where T : struct, IFloatingPoint<T>
{
    // Resolved once per numeric type. Ordinary double/decimal do not box values.
    internal static readonly ICompositeAggregation<T>? Composite = CreateComposite();

    private static ICompositeAggregation<T>? CreateComposite() =>
        typeof(T).IsGenericType && typeof(T).GetGenericTypeDefinition() == typeof(Dual<>)
            ? (ICompositeAggregation<T>)Activator.CreateInstance(
                typeof(DualAggregation<>).MakeGenericType(typeof(T).GetGenericArguments()[0]))!
            : null;

    internal static T? FromStorage(decimal? firstValue, decimal? secondValue) =>
        Composite is { } composite
            ? firstValue.HasValue || secondValue.HasValue ? composite.FromStorage(firstValue, secondValue) : null
            : firstValue.HasValue ? T.CreateChecked(firstValue.Value) : null;
}

internal sealed class DualAggregation<T> : ICompositeAggregation<Dual<T>> where T : struct, IFloatingPoint<T>
{
    public Dual<T> Add(Dual<T> left, Dual<T> right) => new(
        left.FirstValue.GetValueOrDefault() + right.FirstValue.GetValueOrDefault(),
        left.SecondValue.GetValueOrDefault() + right.SecondValue.GetValueOrDefault());

    public (int First, int Second) GetCounts(Dual<T> value) =>
        (value.FirstValue.HasValue ? 1 : 0, value.SecondValue.HasValue ? 1 : 0);

    public Dual<T> GetSum(Dual<T> sum, int firstCount, int secondCount) => new(
        firstCount == 0 ? null : sum.FirstValue, secondCount == 0 ? null : sum.SecondValue);

    public Dual<T> GetAverage(Dual<T> sum, int firstCount, int secondCount) => new(
        firstCount == 0 ? null : sum.FirstValue / T.CreateChecked(firstCount),
        secondCount == 0 ? null : sum.SecondValue / T.CreateChecked(secondCount));

    public Dual<T> FromStorage(decimal? firstValue, decimal? secondValue) => new(
        firstValue.HasValue ? T.CreateChecked(firstValue.Value) : null,
        (secondValue ?? firstValue) is { } second ? T.CreateChecked(second) : null);
}
