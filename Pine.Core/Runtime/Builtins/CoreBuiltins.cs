namespace Pine.Core.Runtime.Builtins;

/// <summary>
/// Built-ins needed by the interpreter itself: bar data, <c>na</c>, <c>nz</c>, <c>fixnan</c>
/// and <c>plot</c> (records values).
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
            .Function("fixnan", ["source"], FixNan)
            .Function("indicator", ["title"], _ => Value.Na, acceptsAnyArguments: true)
            .Function("plot", ["series", "title"], Plot, acceptsAnyArguments: true);
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
            .Variable("hlcc4", FromBar(b => (b.High + b.Low + b.Close + b.Close) / 4))
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

    private sealed class LastValueState
    {
        public Value Last;
    }

    /// <summary>Replaces na with the last non-na value seen by this call site.</summary>
    private static Value FixNan(CallContext call)
    {
        var state = call.State<LastValueState>();
        if (!call[0].IsNa)
            state.Last = call[0];
        return state.Last;
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
}
