using System.Globalization;
using System.Text.Json;
using Pine.Core.Runtime;

namespace Backtester.Engine;

/// <summary>Builds <c>input.*</c> overrides from text (command line, form fields) or JSON.</summary>
public static class InputValues
{
    /// <summary><c>"20"</c> → int, <c>"2.5"</c> → float, <c>"true"</c>/<c>"false"</c> → bool, <c>#RRGGBB[AA]</c> → color, anything else → string.</summary>
    public static Value FromText(string text)
    {
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
            return Value.Int(integer);
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            return Value.Float(number);
        if (bool.TryParse(text, out var flag))
            return Value.Bool(flag);
        if (text.StartsWith('#') && text.Length is 7 or 9
            && uint.TryParse(text.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgba))
            return Value.Color(text.Length == 7 ? (rgba << 8) | 0xFF : rgba);

        return Value.String(text);
    }

    public static Value FromJson(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetInt64(out var integer) => Value.Int(integer),
            JsonValueKind.Number => Value.Float(element.GetDouble()),
            JsonValueKind.True => Value.True,
            JsonValueKind.False => Value.False,
            JsonValueKind.String => FromText(element.GetString()!) is { Kind: ValueKind.Color } color ? color : Value.String(element.GetString()!),
            _ => throw new FormatException($"Unsupported input value {element}"),
        };

    /// <summary>A JSON object of title → value, e.g. <c>{"Fast SMA": 20, "Mode": "Bands"}</c>.</summary>
    public static Dictionary<string, Value> ParseJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new FormatException("Inputs must be a JSON object of title → value");

        return document.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => FromJson(p.Value));
    }
}
