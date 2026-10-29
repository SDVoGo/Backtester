using System.Globalization;
using System.Text.RegularExpressions;

namespace Pine.Core.Runtime.Builtins;

/// <summary><c>str.*</c> and the <c>format.*</c> constants.</summary>
internal static partial class StringBuiltins
{
    // No symbol info yet: format.mintick assumes a tick of 0.01
    private const string MintickFormat = "0.##";
    private const string DefaultFloatFormat = "0.##########";

    public static void Register(BuiltinRegistry registry)
    {
        registry
            .Constant("format.inherit", Value.String("inherit"))
            .Constant("format.price", Value.String("price"))
            .Constant("format.mintick", Value.String("mintick"))
            .Constant("format.percent", Value.String("percent"))
            .Constant("format.volume", Value.String("volume"))
            .Function("str.tostring", ["value", "format"], call
                => Value.String(ToString(call[0], call.Has(1) && call[1].Kind == ValueKind.String ? call[1].AsString : null)))
            .Function("str.tonumber", ["string"], ToNumber)
            .Function("str.format", ["formatString"], Format, variadic: true)
            .Function("str.length", ["string"], call => Map(call, s => Value.Int(s.Length)))
            .Function("str.upper", ["source"], call => Map(call, s => Value.String(s.ToUpperInvariant())))
            .Function("str.lower", ["source"], call => Map(call, s => Value.String(s.ToLowerInvariant())))
            .Function("str.contains", ["source", "str"], call => Map(call, s => Value.Bool(s.Contains(call.String(1), StringComparison.Ordinal))))
            .Function("str.startswith", ["source", "str"], call => Map(call, s => Value.Bool(s.StartsWith(call.String(1), StringComparison.Ordinal))))
            .Function("str.endswith", ["source", "str"], call => Map(call, s => Value.Bool(s.EndsWith(call.String(1), StringComparison.Ordinal))))
            .Function("str.pos", ["source", "str"], call => Map(call, s => s.IndexOf(call.String(1), StringComparison.Ordinal) is var i && i >= 0 ? Value.Int(i) : Value.Na))
            .Function("str.replace_all", ["source", "target", "replacement"], call => Map(call, s => Value.String(s.Replace(call.String(1), call.String(2), StringComparison.Ordinal))))
            .Function("str.substring", ["source", "begin_pos", "end_pos"], Substring);
    }

    private static Value Map(CallContext call, Func<string, Value> function)
        => call[0].IsNa ? Value.Na : function(call.String(0));

    /// <summary>
    /// Pine format strings (<c>"#.##"</c>, <c>"0.000"</c>) share the syntax of .NET custom numeric formats.
    /// na is <c>"NaN"</c>, as in Pine.
    /// </summary>
    public static string ToString(Value value, string? format = null)
    {
        if (value.IsNa)
            return "NaN";
        if (!value.IsNumber)
            return value.ToString();

        var number = value.AsDouble;
        return format switch
        {
            null or "inherit" or "price" => value.Kind == ValueKind.Int
                ? value.AsInt.ToString(CultureInfo.InvariantCulture)
                : number.ToString(DefaultFloatFormat, CultureInfo.InvariantCulture),
            "mintick" => number.ToString(MintickFormat, CultureInfo.InvariantCulture),
            "percent" => number.ToString("0.##", CultureInfo.InvariantCulture) + "%",
            "volume" => Volume(number),
            _ => number.ToString(format, CultureInfo.InvariantCulture),
        };
    }

    private static string Volume(double number)
    {
        var (scaled, suffix) = Math.Abs(number) switch
        {
            >= 1e9 => (number / 1e9, "B"),
            >= 1e6 => (number / 1e6, "M"),
            >= 1e3 => (number / 1e3, "K"),
            _ => (number, ""),
        };
        return scaled.ToString("0.###", CultureInfo.InvariantCulture) + suffix;
    }

    private static Value ToNumber(CallContext call)
        => !call[0].IsNa && double.TryParse(call.String(0), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? Value.Float(number)
            : Value.Na;

    private static Value Substring(CallContext call)
    {
        if (call[0].IsNa)
            return Value.Na;

        var source = call.String(0);
        var begin = (int)Math.Clamp(call.Number(1), 0, source.Length);
        var end = call.Has(2) ? (int)Math.Clamp(call.Number(2), begin, source.Length) : source.Length;
        return Value.String(source[begin..end]);
    }

    [GeneratedRegex(@"\{(\d+)(?:,\s*number\s*(?:,\s*([^}]*))?)?\}")]
    private static partial Regex Placeholder();

    /// <summary>Java MessageFormat subset: <c>{0}</c> and <c>{0,number,#.##}</c>.</summary>
    private static Value Format(CallContext call)
    {
        var template = call.String(0);
        var result = Placeholder().Replace(template, match =>
        {
            var index = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) + 1;
            if (index >= call.Count)
                return match.Value;

            var format = match.Groups[2].Success ? match.Groups[2].Value.Trim() : null;
            return ToString(call[index], format is "" ? null : format);
        });

        return Value.String(result);
    }
}
