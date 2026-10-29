namespace Pine.Core.Runtime.Builtins;

/// <summary>
/// Everything that only draws or alerts is accepted and ignored. Drawing objects
/// (<c>line.new</c>, <c>label.new</c>, ...) are na, so <c>na(myLine)</c> checks keep working.
/// </summary>
internal static class DrawingBuiltins
{
    private static readonly string[] Functions =
    [
        "plotshape", "plotchar", "plotarrow", "plotcandle", "plotbar",
        "fill", "barcolor", "bgcolor", "hline", "alert", "alertcondition",
    ];

    // Drawing objects and the namespaces of their style constants (line.style_dotted, shape.triangleup, ...)
    private static readonly string[] Namespaces =
    [
        "line", "label", "box", "table", "linefill", "polyline",
        "plot", "hline", "shape", "location", "size", "display", "position",
        "extend", "xloc", "yloc", "text", "font",
    ];

    public static void Register(BuiltinRegistry registry)
    {
        foreach (var name in Functions)
            registry.NoOp(name);

        foreach (var ns in Namespaces)
            registry.NoOpNamespace(ns);
    }
}
