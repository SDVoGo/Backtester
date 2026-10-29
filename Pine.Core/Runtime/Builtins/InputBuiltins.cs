using Pine.Core.Syntax;

namespace Pine.Core.Runtime.Builtins;

/// <summary>
/// <c>input.*</c>: return <c>defval</c>, or the override passed to the run for the input's title.
/// Overrides are checked against <c>minval</c>/<c>maxval</c>/<c>options</c>; every value used is
/// recorded in <see cref="BarContext.InputValues"/>.
/// </summary>
internal static class InputBuiltins
{
    private enum InputType { Any, Int, Float, Bool, String, Color, Source }

    // Positional order of input.int/input.float; options can also be the third positional argument
    private static readonly string[] NumberParameters = ["defval", "title", "minval", "maxval", "step", "options"];
    private static readonly string[] StringParameters = ["defval", "title", "options"];
    private static readonly string[] PlainParameters = ["defval", "title"];

    public static void Register(BuiltinRegistry registry)
    {
        static BuiltinBody Input(InputType type) => call => Read(call, type);

        registry
            .Function("input", PlainParameters, Input(InputType.Any), acceptsAnyArguments: true)
            .Function("input.int", NumberParameters, Input(InputType.Int), acceptsAnyArguments: true)
            .Function("input.float", NumberParameters, Input(InputType.Float), acceptsAnyArguments: true)
            .Function("input.bool", PlainParameters, Input(InputType.Bool), acceptsAnyArguments: true)
            .Function("input.string", StringParameters, Input(InputType.String), acceptsAnyArguments: true)
            .Function("input.color", PlainParameters, Input(InputType.Color), acceptsAnyArguments: true)
            .Function("input.source", PlainParameters, Input(InputType.Source), acceptsAnyArguments: true)
            .Function("input.timeframe", StringParameters, Input(InputType.String), acceptsAnyArguments: true)
            .Function("input.session", StringParameters, Input(InputType.String), acceptsAnyArguments: true)
            .Function("input.symbol", PlainParameters, Input(InputType.String), acceptsAnyArguments: true)
            .Function("input.text_area", PlainParameters, Input(InputType.String), acceptsAnyArguments: true)
            .Function("input.price", PlainParameters, Input(InputType.Float), acceptsAnyArguments: true)
            .Function("input.time", PlainParameters, Input(InputType.Int), acceptsAnyArguments: true);
    }

    private static Value Read(CallContext call, InputType type)
    {
        if (!call.Has(0))
            throw call.Error("Missing argument 'defval'");

        var title = call.Has(1) && call[1].Kind == ValueKind.String ? call[1].AsString : null;
        var execution = call.Execution;

        Value value;
        if (title is not null && execution.InputOverrides.TryGetValue(title, out var overridden))
        {
            value = type == InputType.Source ? Source(call, overridden) : Convert(call, overridden, type, title);
            Validate(call, value, type, title);
            execution.InputValues[title] = type == InputType.Source ? overridden : value;
        }
        else
        {
            value = type == InputType.Float && call[0].Kind == ValueKind.Int ? Value.Float(call[0].AsDouble) : call[0];
            if (title is not null)
                execution.InputValues[title] = type == InputType.Source && call.Node.Arguments[0].Value is IdentifierExpr source
                    ? Value.String(source.Name)
                    : value;
        }

        return value;
    }

    private static Value Convert(CallContext call, Value value, InputType type, string title)
    {
        var converted = (type, value.Kind) switch
        {
            (InputType.Any, _) => value,
            (InputType.Int, ValueKind.Int) => value,
            (InputType.Int, ValueKind.Float) when value.AsDouble == Math.Floor(value.AsDouble) => Value.Int(value.AsInt),
            (InputType.Float, ValueKind.Int or ValueKind.Float) => Value.Float(value.AsDouble),
            (InputType.Bool, ValueKind.Bool) => value,
            (InputType.String, ValueKind.String) => value,
            (InputType.Color, ValueKind.Color) => value,
            _ => (Value?)null,
        };

        return converted ?? throw call.Error($"Input '{title}' expects {type.ToString().ToLowerInvariant()}, got '{value}'");
    }

    private static void Validate(CallContext call, Value value, InputType type, string title)
    {
        if (type is InputType.Int or InputType.Float)
        {
            if (call.Has(2) && call[2].IsNumber && value.AsDouble < call[2].AsDouble)
                throw call.Error($"Input '{title}' = {value} is below minval {call[2]}");
            if (call.Has(3) && call[3].IsNumber && value.AsDouble > call[3].AsDouble)
                throw call.Error($"Input '{title}' = {value} is above maxval {call[3]}");
        }

        var options = type switch
        {
            InputType.Int or InputType.Float when call.Has(5) => call[5],
            InputType.Int or InputType.Float when call.Has(2) && call[2].Kind == ValueKind.Tuple => call[2],
            InputType.String when call.Has(2) => call[2],
            _ => Value.Na,
        };

        if (options.Kind == ValueKind.Tuple && !options.AsTuple.Any(o => o == value || (o.IsNumber && value.IsNumber && o.AsDouble == value.AsDouble)))
            throw call.Error($"Input '{title}' = {value} is not one of the options {options}");
    }

    /// <summary>A source override is the name of a bar field: <c>"open"</c>, <c>"hl2"</c>, ...</summary>
    private static Value Source(CallContext call, Value name)
    {
        var bar = call.Execution.Bar;
        double? price = name.Kind == ValueKind.String
            ? name.AsString switch
            {
                "open" => bar.Open,
                "high" => bar.High,
                "low" => bar.Low,
                "close" => bar.Close,
                "volume" => bar.Volume,
                "hl2" => (bar.High + bar.Low) / 2,
                "hlc3" => (bar.High + bar.Low + bar.Close) / 3,
                "ohlc4" => (bar.Open + bar.High + bar.Low + bar.Close) / 4,
                "hlcc4" => (bar.High + bar.Low + bar.Close + bar.Close) / 4,
                _ => null,
            }
            : null;

        return price is { } p ? Value.Float(p) : throw call.Error($"Unknown source '{name}'");
    }
}
