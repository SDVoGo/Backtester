using System.Globalization;

namespace Pine.Core.Runtime;

public enum ValueKind : byte
{
    Na,
    Int,
    Float,
    Bool,
    String,
    Color,
    Tuple,
    Object,
}

/// <summary>
/// A Pine runtime value. <c>default(Value)</c> is <c>na</c>.
/// Ints are stored as double: exact up to 2^53, enough for prices, counters and unix-ms timestamps.
/// Colors are stored as 0xRRGGBBAA.
/// </summary>
public readonly struct Value : IEquatable<Value>
{
    private readonly double _number;
    private readonly object? _reference;

    public ValueKind Kind { get; }

    private Value(ValueKind kind, double number, object? reference)
    {
        Kind = kind;
        _number = number;
        _reference = reference;
    }

    public static readonly Value Na = default;
    public static readonly Value True = Bool(true);
    public static readonly Value False = Bool(false);

    public static Value Int(long value) => new(ValueKind.Int, value, null);

    /// <summary>NaN and infinities become <c>na</c>, as in Pine.</summary>
    public static Value Float(double value)
        => double.IsFinite(value) ? new(ValueKind.Float, value, null) : Na;

    public static Value Bool(bool value) => new(ValueKind.Bool, value ? 1 : 0, null);

    public static Value String(string value) => new(ValueKind.String, 0, value);

    public static Value Color(uint rgba) => new(ValueKind.Color, rgba, null);

    public static Value Tuple(Value[] items) => new(ValueKind.Tuple, 0, items);

    public static Value Object(object value) => new(ValueKind.Object, 0, value);

    /// <summary>Int if <paramref name="value"/> is integral and both operands were ints, Float otherwise.</summary>
    public static Value Number(double value, bool isInt)
        => isInt ? Int((long)value) : Float(value);

    public bool IsNa => Kind == ValueKind.Na;

    public bool IsNumber => Kind is ValueKind.Int or ValueKind.Float;

    public double AsDouble => IsNumber ? _number : throw InvalidCast("number");

    public long AsInt => IsNumber ? (long)_number : throw InvalidCast("int");

    public bool AsBool => Kind == ValueKind.Bool ? _number != 0 : throw InvalidCast("bool");

    public string AsString => Kind == ValueKind.String ? (string)_reference! : throw InvalidCast("string");

    public uint AsColor => Kind == ValueKind.Color ? (uint)_number : throw InvalidCast("color");

    public Value[] AsTuple => Kind == ValueKind.Tuple ? (Value[])_reference! : throw InvalidCast("tuple");

    public object AsObject => Kind == ValueKind.Object ? _reference! : throw InvalidCast("object");

    /// <summary>The number, or null when <c>na</c>.</summary>
    public double? AsNullableDouble => IsNa ? null : AsDouble;

    private InvalidCastException InvalidCast(string expected)
        => new($"Cannot use {Kind} value '{this}' as {expected}");

    public bool Equals(Value other)
        => Kind == other.Kind && _number.Equals(other._number) && Equals(_reference, other._reference);

    public override bool Equals(object? obj)
        => obj is Value other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(Kind, _number, _reference);

    public static bool operator ==(Value left, Value right) => left.Equals(right);

    public static bool operator !=(Value left, Value right) => !left.Equals(right);

    public override string ToString()
        => Kind switch
        {
            ValueKind.Na => "na",
            ValueKind.Int => ((long)_number).ToString(CultureInfo.InvariantCulture),
            ValueKind.Float => _number.ToString("R", CultureInfo.InvariantCulture),
            ValueKind.Bool => _number != 0 ? "true" : "false",
            ValueKind.String => (string)_reference!,
            ValueKind.Color => (uint)_number is var c && (c & 0xFF) == 0xFF
                ? $"#{c >> 8:X6}"
                : $"#{(uint)_number:X8}",
            ValueKind.Tuple => $"[{string.Join(", ", (Value[])_reference!)}]",
            _ => _reference?.ToString() ?? "object",
        };
}
