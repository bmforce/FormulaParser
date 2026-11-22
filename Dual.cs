using System.Globalization;
using System.Numerics;
using System.Text;

namespace FormulaParser;

/// <summary>
/// Two independent numeric components. Arithmetic propagates missing components;
/// aggregates skip them independently. Scalar constants populate both components.
/// </summary>
public readonly struct Dual<T>(T? firstValue, T? secondValue) : IFloatingPoint<Dual<T>>
    where T : struct, IFloatingPoint<T>
{
    public T? FirstValue { get; } = firstValue;
    public T? SecondValue { get; } = secondValue;

    public static Dual<T> Zero => new(T.Zero, T.Zero);
    public static Dual<T> One => new(T.One, T.One);
    public static Dual<T> NegativeOne => new(T.NegativeOne, T.NegativeOne);
    public static Dual<T> AdditiveIdentity => Zero;
    public static Dual<T> MultiplicativeIdentity => One;
    public static Dual<T> E => new(T.E, T.E);
    public static Dual<T> Pi => new(T.Pi, T.Pi);
    public static Dual<T> Tau => new(T.Tau, T.Tau);
    public static int Radix => T.Radix;

    public static Dual<T> operator +(Dual<T> a, Dual<T> b) => new(a.FirstValue + b.FirstValue, a.SecondValue + b.SecondValue);
    public static Dual<T> operator -(Dual<T> a, Dual<T> b) => new(a.FirstValue - b.FirstValue, a.SecondValue - b.SecondValue);
    public static Dual<T> operator *(Dual<T> a, Dual<T> b) => new(a.FirstValue * b.FirstValue, a.SecondValue * b.SecondValue);
    public static Dual<T> operator /(Dual<T> a, Dual<T> b) => new(a.FirstValue / b.FirstValue, a.SecondValue / b.SecondValue);
    public static Dual<T> operator %(Dual<T> a, Dual<T> b) => new(a.FirstValue % b.FirstValue, a.SecondValue % b.SecondValue);
    public static Dual<T> operator -(Dual<T> value) => new(-value.FirstValue, -value.SecondValue);
    public static Dual<T> operator +(Dual<T> value) => value;
    public static Dual<T> operator ++(Dual<T> value) => value + One;
    public static Dual<T> operator --(Dual<T> value) => value - One;
    public static bool operator ==(Dual<T> a, Dual<T> b) => a.FirstValue == b.FirstValue && a.SecondValue == b.SecondValue;
    public static bool operator !=(Dual<T> a, Dual<T> b) => !(a == b);

    // Ordering is lexicographic, with missing values before present values.
    public int CompareTo(Dual<T> other)
    {
        var first = Nullable.Compare(FirstValue, other.FirstValue);
        return first != 0 ? first : Nullable.Compare(SecondValue, other.SecondValue);
    }
    public int CompareTo(object? other) => other == null ? 1 : other is Dual<T> value
        ? CompareTo(value) : throw new ArgumentException($"Expected {typeof(Dual<T>)}.", nameof(other));
    public bool Equals(Dual<T> other) => Nullable.Equals(FirstValue, other.FirstValue) && Nullable.Equals(SecondValue, other.SecondValue);
    public override bool Equals(object? obj) => obj is Dual<T> value && Equals(value);
    public override int GetHashCode() => HashCode.Combine(FirstValue, SecondValue);
    public static bool operator <(Dual<T> a, Dual<T> b) => a.CompareTo(b) < 0;
    public static bool operator >(Dual<T> a, Dual<T> b) => a.CompareTo(b) > 0;
    public static bool operator <=(Dual<T> a, Dual<T> b) => a.CompareTo(b) <= 0;
    public static bool operator >=(Dual<T> a, Dual<T> b) => a.CompareTo(b) >= 0;

    private static Dual<T> Map(Dual<T> value, Func<T, T> operation) => new(
        value.FirstValue.HasValue ? operation(value.FirstValue.Value) : null,
        value.SecondValue.HasValue ? operation(value.SecondValue.Value) : null);
    private static Dual<T> Zip(Dual<T> a, Dual<T> b, Func<T, T, T> operation) => new(
        a.FirstValue.HasValue && b.FirstValue.HasValue ? operation(a.FirstValue.Value, b.FirstValue.Value) : null,
        a.SecondValue.HasValue && b.SecondValue.HasValue ? operation(a.SecondValue.Value, b.SecondValue.Value) : null);
    private static bool All(Dual<T> value, Func<T, bool> predicate) =>
        (value.FirstValue.HasValue || value.SecondValue.HasValue) &&
        (!value.FirstValue.HasValue || predicate(value.FirstValue.Value)) &&
        (!value.SecondValue.HasValue || predicate(value.SecondValue.Value));
    private static bool Any(Dual<T> value, Func<T, bool> predicate) =>
        value.FirstValue.HasValue && predicate(value.FirstValue.Value) ||
        value.SecondValue.HasValue && predicate(value.SecondValue.Value);

    public static Dual<T> Abs(Dual<T> value) => Map(value, T.Abs);
    public static Dual<T> MaxMagnitude(Dual<T> a, Dual<T> b) => Zip(a, b, T.MaxMagnitude);
    public static Dual<T> MaxMagnitudeNumber(Dual<T> a, Dual<T> b) => Zip(a, b, T.MaxMagnitudeNumber);
    public static Dual<T> MinMagnitude(Dual<T> a, Dual<T> b) => Zip(a, b, T.MinMagnitude);
    public static Dual<T> MinMagnitudeNumber(Dual<T> a, Dual<T> b) => Zip(a, b, T.MinMagnitudeNumber);
    public static Dual<T> Round(Dual<T> value, int digits, MidpointRounding mode) =>
        Map(value, component => T.Round(component, digits, mode));
    public static bool IsCanonical(Dual<T> value) => All(value, T.IsCanonical);
    public static bool IsComplexNumber(Dual<T> value) => Any(value, T.IsComplexNumber);
    public static bool IsEvenInteger(Dual<T> value) => All(value, T.IsEvenInteger);
    public static bool IsFinite(Dual<T> value) => All(value, T.IsFinite);
    public static bool IsImaginaryNumber(Dual<T> value) => All(value, T.IsImaginaryNumber);
    public static bool IsInfinity(Dual<T> value) => Any(value, T.IsInfinity);
    public static bool IsInteger(Dual<T> value) => All(value, T.IsInteger);
    public static bool IsNaN(Dual<T> value) => Any(value, T.IsNaN);
    public static bool IsNegative(Dual<T> value) => All(value, T.IsNegative);
    public static bool IsNegativeInfinity(Dual<T> value) => Any(value, T.IsNegativeInfinity);
    public static bool IsNormal(Dual<T> value) => All(value, T.IsNormal);
    public static bool IsOddInteger(Dual<T> value) => All(value, T.IsOddInteger);
    public static bool IsPositive(Dual<T> value) => All(value, T.IsPositive);
    public static bool IsPositiveInfinity(Dual<T> value) => Any(value, T.IsPositiveInfinity);
    public static bool IsRealNumber(Dual<T> value) => All(value, T.IsRealNumber);
    public static bool IsSubnormal(Dual<T> value) => All(value, T.IsSubnormal);
    public static bool IsZero(Dual<T> value) => All(value, T.IsZero);

    public override string ToString() => ToString(null, CultureInfo.CurrentCulture);
    public string ToString(string? format, IFormatProvider? provider) =>
        $"({(FirstValue.HasValue ? FirstValue.Value.ToString(format, provider) : "null")}; {(SecondValue.HasValue ? SecondValue.Value.ToString(format, provider) : "null")})";
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        var text = ToString(format.IsEmpty ? null : format.ToString(), provider);
        charsWritten = text.Length;
        if (text.AsSpan().TryCopyTo(destination)) return true;
        charsWritten = 0;
        return false;
    }
    public bool TryFormat(Span<byte> destination, out int bytesWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        var text = ToString(format.IsEmpty ? null : format.ToString(), provider);
        if (Encoding.UTF8.GetByteCount(text) <= destination.Length)
        {
            bytesWritten = Encoding.UTF8.GetBytes(text.AsSpan(), destination);
            return true;
        }
        bytesWritten = 0;
        return false;
    }

    public static Dual<T> Parse(string s, IFormatProvider? provider) => Parse(s.AsSpan(), provider);
    public static Dual<T> Parse(ReadOnlySpan<char> s, IFormatProvider? provider) =>
        Parse(s, NumberStyles.Float | NumberStyles.AllowThousands, provider);
    public static Dual<T> Parse(string s, NumberStyles style, IFormatProvider? provider) => Parse(s.AsSpan(), style, provider);
    public static Dual<T> Parse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider) =>
        TryParse(s, style, provider, out var result) ? result : throw new FormatException("Invalid dual number.");
    public static Dual<T> Parse(ReadOnlySpan<byte> s, IFormatProvider? provider) => Parse(Encoding.UTF8.GetString(s), provider);
    public static bool TryParse(string? s, IFormatProvider? provider, out Dual<T> result) => TryParse(s.AsSpan(), provider, out result);
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out Dual<T> result) =>
        TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, provider, out result);
    public static bool TryParse(string? s, NumberStyles style, IFormatProvider? provider, out Dual<T> result) =>
        TryParse(s.AsSpan(), style, provider, out result);
    public static bool TryParse(ReadOnlySpan<byte> s, IFormatProvider? provider, out Dual<T> result) =>
        TryParse(Encoding.UTF8.GetString(s), provider, out result);
    public static bool TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider, out Dual<T> result)
    {
        s = s.Trim();
        if (s.Length >= 2 && s[0] == '(' && s[^1] == ')')
        {
            s = s[1..^1];
            var separator = s.IndexOf(';');
            if (separator >= 0 && TryComponent(s[..separator], style, provider, out var first) &&
                TryComponent(s[(separator + 1)..], style, provider, out var second))
            {
                result = new(first, second);
                return true;
            }
        }
        else if (T.TryParse(s, style, provider, out var value))
        {
            result = new(value, value);
            return true;
        }
        result = default;
        return false;
    }
    private static bool TryComponent(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider, out T? value)
    {
        if (s.Trim().Equals("null", StringComparison.OrdinalIgnoreCase)) { value = null; return true; }
        var success = T.TryParse(s, style, provider, out var number);
        value = success ? number : null;
        return success;
    }

    private bool TryGetSharedValue(out T value)
    {
        value = FirstValue.GetValueOrDefault();
        return FirstValue.HasValue && SecondValue.HasValue && FirstValue.Value == SecondValue.Value;
    }
    private T SharedValue => TryGetSharedValue(out var value) ? value :
        throw new InvalidOperationException("A dual number has a scalar representation only when both components are present and equal.");

    static bool INumberBase<Dual<T>>.TryConvertFromChecked<TOther>(TOther value, out Dual<T> result)
    {
        if (value is Dual<T> dual) { result = dual; return true; }
        try { var scalar = T.CreateChecked(value); result = new(scalar, scalar); return true; }
        catch (NotSupportedException) { result = default; return false; }
    }
    static bool INumberBase<Dual<T>>.TryConvertFromSaturating<TOther>(TOther value, out Dual<T> result)
    {
        if (value is Dual<T> dual) { result = dual; return true; }
        try { var scalar = T.CreateSaturating(value); result = new(scalar, scalar); return true; }
        catch (NotSupportedException) { result = default; return false; }
    }
    static bool INumberBase<Dual<T>>.TryConvertFromTruncating<TOther>(TOther value, out Dual<T> result)
    {
        if (value is Dual<T> dual) { result = dual; return true; }
        try { var scalar = T.CreateTruncating(value); result = new(scalar, scalar); return true; }
        catch (NotSupportedException) { result = default; return false; }
    }
    static bool INumberBase<Dual<T>>.TryConvertToChecked<TOther>(Dual<T> value, out TOther result)
    {
        if (!value.TryGetSharedValue(out var scalar)) { result = default!; return false; }
        try { result = TOther.CreateChecked(scalar); return true; }
        catch (NotSupportedException) { result = default!; return false; }
    }
    static bool INumberBase<Dual<T>>.TryConvertToSaturating<TOther>(Dual<T> value, out TOther result)
    {
        if (!value.TryGetSharedValue(out var scalar)) { result = default!; return false; }
        try { result = TOther.CreateSaturating(scalar); return true; }
        catch (NotSupportedException) { result = default!; return false; }
    }
    static bool INumberBase<Dual<T>>.TryConvertToTruncating<TOther>(Dual<T> value, out TOther result)
    {
        if (!value.TryGetSharedValue(out var scalar)) { result = default!; return false; }
        try { result = TOther.CreateTruncating(scalar); return true; }
        catch (NotSupportedException) { result = default!; return false; }
    }

    // There is no single exponent/significand for unequal components. These scalar
    // encoding operations explicitly reject such values rather than losing a component.
    public int GetExponentByteCount() => SharedValue.GetExponentByteCount();
    public int GetExponentShortestBitLength() => SharedValue.GetExponentShortestBitLength();
    public int GetSignificandBitLength() => SharedValue.GetSignificandBitLength();
    public int GetSignificandByteCount() => SharedValue.GetSignificandByteCount();
    public bool TryWriteExponentBigEndian(Span<byte> destination, out int bytesWritten)
    {
        bytesWritten = 0;
        return TryGetSharedValue(out var value) && value.TryWriteExponentBigEndian(destination, out bytesWritten);
    }
    public bool TryWriteExponentLittleEndian(Span<byte> destination, out int bytesWritten)
    {
        bytesWritten = 0;
        return TryGetSharedValue(out var value) && value.TryWriteExponentLittleEndian(destination, out bytesWritten);
    }
    public bool TryWriteSignificandBigEndian(Span<byte> destination, out int bytesWritten)
    {
        bytesWritten = 0;
        return TryGetSharedValue(out var value) && value.TryWriteSignificandBigEndian(destination, out bytesWritten);
    }
    public bool TryWriteSignificandLittleEndian(Span<byte> destination, out int bytesWritten)
    {
        bytesWritten = 0;
        return TryGetSharedValue(out var value) && value.TryWriteSignificandLittleEndian(destination, out bytesWritten);
    }
}
