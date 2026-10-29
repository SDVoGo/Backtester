namespace Pine.Core.Runtime.Builtins;

/// <summary>
/// Minimal built-ins needed by the interpreter itself: bar data, <c>na</c>, <c>nz</c>,
/// <c>plot</c> (records values) and <c>ta.sma</c> as reference stateful function.
/// The full library comes in phase 3.
/// </summary>
internal static class CoreBuiltins
{
    public static void Register(BuiltinRegistry registry)
    {
        RegisterBarData(registry);

        registry
            .Constant("na", Value.Na)
            .Function("na", ["x"], call => Value.Bool(call[0].IsNa))
            .Function("nz", ["source", "replacement"], Nz)
            .Function("indicator", ["title"], _ => Value.Na, acceptsAnyArguments: true)
            .Function("plot", ["series", "title"], Plot, acceptsAnyArguments: true)
            .Function("ta.sma", ["source", "length"], Sma);
    }

    private static void RegisterBarData(BuiltinRegistry registry)
    {
        static BuiltinVariable FromBar(Func<Bar, double> selector)
            => (context, offset) => context.BarAt(offset) is { } bar ? Value.Float(selector(bar)) : Value.Na;

        registry
            .Variable("open", FromBar(b => b.Open))
            .Variable("high", FromBar(b => b.High))
            .Variable("low", FromBar(b => b.Low))
            .Variable("close", FromBar(b => b.Close))
            .Variable("volume", FromBar(b => b.Volume))
            .Variable("hl2", FromBar(b => (b.High + b.Low) / 2))
            .Variable("hlc3", FromBar(b => (b.High + b.Low + b.Close) / 3))
            .Variable("ohlc4", FromBar(b => (b.Open + b.High + b.Low + b.Close) / 4))
            .Variable("time", (context, offset) => context.BarAt(offset) is { } bar ? Value.Int(bar.Time) : Value.Na)
            .Variable("bar_index", (context, offset) => context.BarIndex >= offset ? Value.Int(context.BarIndex - offset) : Value.Na)
            .Variable("last_bar_index", (context, _) => Value.Int(context.Bars.Count - 1))
            .Variable("barstate.isfirst", (context, offset) => Value.Bool(context.BarIndex - offset == 0))
            .Variable("barstate.islast", (context, offset) => Value.Bool(offset == 0 && context.IsLastBar))
            .Variable("barstate.isconfirmed", (_, _) => Value.True)
            .Variable("barstate.ishistory", (_, _) => Value.True)
            .Variable("barstate.isrealtime", (_, _) => Value.False)
            .Variable("barstate.isnew", (_, _) => Value.True);
    }

    private static Value Nz(CallContext call)
    {
        var source = call[0];
        if (!source.IsNa)
            return source;

        return call.ArgOr(1, Value.Int(0));
    }

    private sealed class PlotState
    {
        public PlotSeries? Series;
    }

    private static Value Plot(CallContext call)
    {
        var state = call.State<PlotState>();
        if (state.Series is null)
        {
            var execution = call.Execution;
            var title = call.Has(1) && call[1].Kind == ValueKind.String
                ? call[1].AsString
                : $"Plot {execution.PlotList.Count + 1}";

            state.Series = new PlotSeries(title, new double?[execution.Bars.Count]);
            execution.PlotList.Add(state.Series);
        }

        var value = call[0];
        state.Series.Values[call.BarIndex] = value.IsNumber ? value.AsDouble : null;
        return Value.Na;
    }

    private sealed class WindowState
    {
        public readonly Queue<double> Values = new();
    }

    /// <summary>Simple moving average; na until <c>length</c> values are seen or if any is na.</summary>
    private static Value Sma(CallContext call)
    {
        var length = call.Length(1);
        var window = call.State<WindowState>().Values;

        window.Enqueue(call[0].IsNumber ? call[0].AsDouble : double.NaN);
        while (window.Count > length)
            window.Dequeue();

        if (window.Count < length)
            return Value.Na;

        var sum = 0.0;
        foreach (var value in window)
            sum += value;

        return Value.Float(sum / length);
    }
}
