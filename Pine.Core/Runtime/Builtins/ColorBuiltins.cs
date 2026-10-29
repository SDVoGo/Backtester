namespace Pine.Core.Runtime.Builtins;

/// <summary>
/// <c>color.*</c>. Colors only reach drawing functions (no-ops), but scripts compute them,
/// so they must evaluate. Transparency is Pine's 0 (opaque) .. 100 (invisible).
/// </summary>
internal static class ColorBuiltins
{
    private static readonly (string Name, uint Rgb)[] Palette =
    [
        ("aqua", 0x00BCD4), ("black", 0x363A45), ("blue", 0x2962FF), ("fuchsia", 0xE040FB),
        ("gray", 0x787B86), ("green", 0x4CAF50), ("lime", 0x00E676), ("maroon", 0x880E4F),
        ("navy", 0x311B92), ("olive", 0x808000), ("orange", 0xFF9800), ("purple", 0x9C27B0),
        ("red", 0xF23645), ("silver", 0xB2B5BE), ("teal", 0x089981), ("white", 0xFFFFFF),
        ("yellow", 0xFDD835),
    ];

    public static void Register(BuiltinRegistry registry)
    {
        foreach (var (name, rgb) in Palette)
            registry.Constant($"color.{name}", Value.Color((rgb << 8) | 0xFF));

        registry
            .Function("color.new", ["color", "transp"], New)
            .Function("color.rgb", ["red", "green", "blue", "transp"], Rgb)
            .Function("color.r", ["color"], call => Channel(call, 24))
            .Function("color.g", ["color"], call => Channel(call, 16))
            .Function("color.b", ["color"], call => Channel(call, 8))
            .Function("color.t", ["color"], call => call[0].IsNa ? Value.Na : Value.Float(Transparency(call[0].AsColor & 0xFF)))
            .Function("color.from_gradient", ["value", "bottom_value", "top_value", "bottom_color", "top_color"], Gradient);
    }

    private static uint Alpha(double transparency)
        => (uint)Math.Round(255 * (100 - Math.Clamp(transparency, 0, 100)) / 100);

    private static double Transparency(uint alpha)
        => Math.Round(100 - alpha * 100 / 255.0, 2);

    private static Value New(CallContext call)
    {
        if (call[0].IsNa)
            return Value.Na;
        if (call[0].Kind != ValueKind.Color)
            throw call.Error($"Argument 1 must be a color, got {call[0].Kind}");

        var transparency = call.Has(1) && !call[1].IsNa ? call.Number(1) : 0;
        return Value.Color((call[0].AsColor & 0xFFFFFF00) | Alpha(transparency));
    }

    private static Value Rgb(CallContext call)
    {
        if (call[0].IsNa || call[1].IsNa || call[2].IsNa)
            return Value.Na;

        static uint Byte(double value) => (uint)Math.Clamp(Math.Round(value), 0, 255);
        var transparency = call.Has(3) && !call[3].IsNa ? call.Number(3) : 0;

        return Value.Color((Byte(call.Number(0)) << 24) | (Byte(call.Number(1)) << 16) | (Byte(call.Number(2)) << 8) | Alpha(transparency));
    }

    private static Value Channel(CallContext call, int shift)
        => call[0].IsNa ? Value.Na : Value.Int((call[0].AsColor >> shift) & 0xFF);

    /// <summary>Linear blend of the two colors (alpha included) by the position of value between the bounds.</summary>
    private static Value Gradient(CallContext call)
    {
        if (call[0].IsNa || call[3].IsNa || call[4].IsNa)
            return Value.Na;

        var bottom = call.Number(1);
        var top = call.Number(2);
        var position = top == bottom ? 0 : Math.Clamp((call.Number(0) - bottom) / (top - bottom), 0, 1);
        var from = call[3].AsColor;
        var to = call[4].AsColor;

        uint result = 0;
        for (var shift = 0; shift < 32; shift += 8)
        {
            var a = (from >> shift) & 0xFF;
            var b = (to >> shift) & 0xFF;
            result |= (uint)Math.Round(a + (b - (double)a) * position) << shift;
        }

        return Value.Color(result);
    }
}
