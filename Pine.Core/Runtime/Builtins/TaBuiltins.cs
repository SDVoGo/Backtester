namespace Pine.Core.Runtime.Builtins;

/// <summary>
/// <c>ta.*</c>, following the reference implementations in the Pine manual. Each call site keeps
/// its own state, so a function updates only on bars where its call is executed (as in Pine).
/// </summary>
internal static class TaBuiltins
{
    public static void Register(BuiltinRegistry registry)
    {
        registry
            .Variable("ta.tr", (context, offset) => context.BarAt(offset) is { } bar
                ? Value.Float(TrueRange.Of(bar, context.BarAt(offset + 1), handleNa: false))
                : Value.Na)
            .Function("ta.tr", ["handle_na"], call => Value.Float(TrueRange.Of(call.Execution.Bar, call.Execution.BarAt(1), call.Bool(0))))
            .Function("ta.sma", ["source", "length"], call => Value.Float(call.State<SmaCalc>().Next(call.NumberOrNaN(0), call.Length(1))))
            .Function("ta.ema", ["source", "length"], call => Value.Float(call.State<SmoothedCalc>().Ema(call.NumberOrNaN(0), call.Length(1))))
            .Function("ta.rma", ["source", "length"], call => Value.Float(call.State<SmoothedCalc>().Rma(call.NumberOrNaN(0), call.Length(1))))
            .Function("ta.wma", ["source", "length"], Wma)
            .Function("ta.vwma", ["source", "length"], Vwma)
            .Function("ta.atr", ["length"], Atr)
            .Function("ta.rsi", ["source", "length"], Rsi)
            .Function("ta.stdev", ["source", "length", "biased"], call
                => Value.Float(call.State<StdevCalc>().Next(call.NumberOrNaN(0), call.Length(1), call.Bool(2, fallback: true))))
            .Function("ta.highest", ["source", "length"], call => Extreme(call, call.Execution.Bar.High, Math.Max))
            .Function("ta.lowest", ["source", "length"], call => Extreme(call, call.Execution.Bar.Low, Math.Min))
            .Function("ta.crossover", ["source1", "source2"], call => Cross(call, up: true, down: false))
            .Function("ta.crossunder", ["source1", "source2"], call => Cross(call, up: false, down: true))
            .Function("ta.cross", ["source1", "source2"], call => Cross(call, up: true, down: true))
            .Function("ta.change", ["source", "length"], Change)
            .Function("ta.mom", ["source", "length"], call => Momentum(call, roc: false))
            .Function("ta.roc", ["source", "length"], call => Momentum(call, roc: true))
            .Function("ta.rising", ["source", "length"], call => Trend(call, rising: true))
            .Function("ta.falling", ["source", "length"], call => Trend(call, rising: false))
            .Function("ta.cum", ["source"], Cum)
            .Function("ta.barssince", ["condition"], BarsSince)
            .Function("ta.valuewhen", ["condition", "source", "occurrence"], ValueWhen)
            .Function("ta.macd", ["source", "fastlen", "slowlen", "siglen"], Macd)
            .Function("ta.bb", ["series", "length", "mult"], Bollinger)
            .Function("ta.supertrend", ["factor", "atrPeriod"], Supertrend);
    }

    /// <summary>Weights length, length-1, ..., 1 from the newest value back.</summary>
    private static Value Wma(CallContext call)
    {
        var length = call.Length(1);
        var history = call.State<History>();
        history.Push(call.NumberOrNaN(0), length);
        if (history.Count < length)
            return Value.Na;

        double sum = 0, norm = 0;
        for (var i = 0; i < length; i++)
        {
            var weight = length - i;
            sum += history[i] * weight;
            norm += weight;
        }

        return Value.Float(sum / norm);
    }

    private sealed class VwmaState
    {
        public readonly SmaCalc Weighted = new();
        public readonly SmaCalc Volume = new();
    }

    private static Value Vwma(CallContext call)
    {
        var length = call.Length(1);
        var state = call.State<VwmaState>();
        var volume = call.Execution.Bar.Volume;

        var weighted = state.Weighted.Next(call.NumberOrNaN(0) * volume, length);
        return Value.Float(weighted / state.Volume.Next(volume, length));
    }

    /// <summary><c>ta.rma(ta.tr(true), length)</c>.</summary>
    private static Value Atr(CallContext call)
        => Value.Float(AtrNext(call, call.State<SmoothedCalc>(), call.Length(0)));

    private static double AtrNext(CallContext call, SmoothedCalc rma, int length)
        => rma.Rma(TrueRange.Of(call.Execution.Bar, call.Execution.BarAt(1), handleNa: true), length);

    private sealed class RsiState
    {
        public readonly SmoothedCalc Up = new();
        public readonly SmoothedCalc Down = new();
        public double Previous = double.NaN;
    }

    /// <summary>RMA of gains over RMA of losses; 100 when there are no losses, 0 when there are no gains.</summary>
    private static Value Rsi(CallContext call)
    {
        var length = call.Length(1);
        var state = call.State<RsiState>();
        var source = call.NumberOrNaN(0);

        var change = source - state.Previous;
        state.Previous = source;

        var up = state.Up.Rma(double.IsNaN(change) ? double.NaN : Math.Max(change, 0), length);
        var down = state.Down.Rma(double.IsNaN(change) ? double.NaN : Math.Max(-change, 0), length);

        if (double.IsNaN(up) || double.IsNaN(down))
            return Value.Na;
        if (down == 0)
            return Value.Float(100);
        if (up == 0)
            return Value.Float(0);

        return Value.Float(100 - 100 / (1 + up / down));
    }

    /// <summary>
    /// <c>ta.highest(source, length)</c> or <c>ta.highest(length)</c> on high/low.
    /// na until <c>length</c> bars are seen; na values in the window are skipped.
    /// </summary>
    private static Value Extreme(CallContext call, double barValue, Func<double, double, double> pick)
    {
        var (source, length) = call.Has(1)
            ? (call.NumberOrNaN(0), call.Length(1))
            : (barValue, call.Length(0));

        var history = call.State<History>();
        history.Push(source, length);
        if (history.Count < length)
            return Value.Na;

        var result = double.NaN;
        for (var i = 0; i < length; i++)
        {
            var value = history[i];
            if (!double.IsNaN(value))
                result = double.IsNaN(result) ? value : pick(result, value);
        }

        return Value.Float(result);
    }

    private sealed class PairState
    {
        public double First = double.NaN;
        public double Second = double.NaN;
    }

    /// <summary>
    /// Over: <c>a &gt; b and a[1] &lt;= b[1]</c>; under: <c>a &lt; b and a[1] &gt;= b[1]</c>.
    /// Any na makes the comparison false (NaN comparisons are false in C# too).
    /// </summary>
    private static Value Cross(CallContext call, bool up, bool down)
    {
        var state = call.State<PairState>();
        var a = call.NumberOrNaN(0);
        var b = call.NumberOrNaN(1);

        var result = (up && a > b && state.First <= state.Second)
            || (down && a < b && state.First >= state.Second);

        state.First = a;
        state.Second = b;
        return Value.Bool(result);
    }

    /// <summary>History of the raw argument, for functions that compare <c>source</c> with <c>source[length]</c>.</summary>
    private static Value Back(CallContext call, out Value current, int defaultLength = 1)
    {
        var length = call.Has(1) ? call.Length(1) : defaultLength;
        if (length > Series.MaxBarsBack)
            throw call.Error($"Length {length} exceeds max_bars_back ({Series.MaxBarsBack})");

        var series = call.State<Series>();
        current = call[0];
        series.Set(current, call.BarIndex);
        return series.Get(length);
    }

    /// <summary><c>source - source[length]</c>; for bools, whether the value changed.</summary>
    private static Value Change(CallContext call)
    {
        var previous = Back(call, out var current);
        if (current.IsNa || previous.IsNa)
            return Value.Na;

        if (current.Kind == ValueKind.Bool)
            return Value.Bool(current.AsBool != previous.AsBool);

        if (!current.IsNumber || !previous.IsNumber)
            throw call.Error($"Cannot compute the change of {current.Kind}");

        var isInt = current.Kind == ValueKind.Int && previous.Kind == ValueKind.Int;
        return Value.Number(current.AsDouble - previous.AsDouble, isInt);
    }

    /// <summary><c>ta.mom</c>: <c>source - source[length]</c>; <c>ta.roc</c>: the same as a percentage of <c>source[length]</c>.</summary>
    private static Value Momentum(CallContext call, bool roc)
    {
        if (!call.Has(1))
            throw call.Error("Missing argument 'length'");

        var previous = Back(call, out var current);
        if (!current.IsNumber || !previous.IsNumber)
            return Value.Na;

        var difference = current.AsDouble - previous.AsDouble;
        return Value.Float(roc ? 100 * difference / previous.AsDouble : difference);
    }

    /// <summary>True when <c>source</c> is above (below) every one of its previous <c>length</c> values.</summary>
    private static Value Trend(CallContext call, bool rising)
    {
        var length = call.Length(1);
        var history = call.State<History>();
        var source = call.NumberOrNaN(0);
        history.Push(source, length + 1);

        if (history.Count <= length || double.IsNaN(source))
            return Value.False;

        for (var i = 1; i <= length; i++)
        {
            var previous = history[i];
            if (!(rising ? source > previous : source < previous))
                return Value.False;
        }

        return Value.True;
    }

    private sealed class SumState
    {
        public double Sum;
    }

    /// <summary>Running total of all values seen; na values count as zero.</summary>
    private static Value Cum(CallContext call)
    {
        var state = call.State<SumState>();
        var value = call.NumberOrNaN(0);
        if (!double.IsNaN(value))
            state.Sum += value;
        return Value.Float(state.Sum);
    }

    private sealed class CounterState
    {
        public int? Bars;
    }

    /// <summary>Bars since the condition was last true (0 on that bar); na if it never was.</summary>
    private static Value BarsSince(CallContext call)
    {
        var state = call.State<CounterState>();
        if (call.Bool(0))
            state.Bars = 0;
        else if (state.Bars is { } bars)
            state.Bars = bars + 1;

        return state.Bars is { } count ? Value.Int(count) : Value.Na;
    }

    private sealed class OccurrencesState
    {
        public readonly List<Value> Values = [];
    }

    /// <summary>Value of <c>source</c> on the <c>occurrence</c>-th most recent bar where the condition was true (0 = latest).</summary>
    private static Value ValueWhen(CallContext call)
    {
        var values = call.State<OccurrencesState>().Values;
        if (call.Bool(0))
        {
            values.Add(call[1]);
            if (values.Count > Series.MaxBarsBack)
                values.RemoveAt(0);
        }

        var occurrence = call.Has(2) && call[2].IsNumber ? (int)call[2].AsInt : 0;
        if (occurrence < 0)
            throw call.Error("Occurrence cannot be negative");

        return occurrence < values.Count ? values[values.Count - 1 - occurrence] : Value.Na;
    }

    private sealed class MacdState
    {
        public readonly SmoothedCalc Fast = new();
        public readonly SmoothedCalc Slow = new();
        public readonly SmoothedCalc Signal = new();
    }

    /// <returns><c>[macd, signal, histogram]</c>.</returns>
    private static Value Macd(CallContext call)
    {
        var state = call.State<MacdState>();
        var source = call.NumberOrNaN(0);

        var macd = state.Fast.Ema(source, call.Length(1)) - state.Slow.Ema(source, call.Length(2));
        var signal = state.Signal.Ema(macd, call.Length(3));

        return Value.Tuple([Value.Float(macd), Value.Float(signal), Value.Float(macd - signal)]);
    }

    private sealed class BollingerState
    {
        public readonly SmaCalc Basis = new();
        public readonly StdevCalc Deviation = new();
    }

    /// <returns><c>[basis, upper, lower]</c>: SMA plus/minus <c>mult</c> standard deviations.</returns>
    private static Value Bollinger(CallContext call)
    {
        var length = call.Length(1);
        var state = call.State<BollingerState>();
        var source = call.NumberOrNaN(0);

        var basis = state.Basis.Next(source, length);
        var deviation = call.Number(2) * state.Deviation.Next(source, length, biased: true);

        return Value.Tuple([Value.Float(basis), Value.Float(basis + deviation), Value.Float(basis - deviation)]);
    }

    private sealed class SupertrendState
    {
        public readonly SmoothedCalc Atr = new();
        public double PreviousAtr = double.NaN;
        public double PreviousUpper = double.NaN;
        public double PreviousLower = double.NaN;
        public double PreviousSupertrend = double.NaN;
    }

    /// <summary>
    /// Line-by-line port of the manual's <c>pine_supertrend</c>, including its na quirks:
    /// <c>nz()</c> on the previous bands and false comparisons with na.
    /// </summary>
    /// <returns><c>[supertrend, direction]</c>; direction is -1 in an uptrend, 1 in a downtrend.</returns>
    private static Value Supertrend(CallContext call)
    {
        var factor = call.Number(0);
        var state = call.State<SupertrendState>();
        var bar = call.Execution.Bar;
        var previousClose = call.Execution.BarAt(1)?.Close ?? double.NaN;

        var atr = AtrNext(call, state.Atr, call.Length(1));
        var source = (bar.High + bar.Low) / 2;
        var upper = source + factor * atr;
        var lower = source - factor * atr;
        var previousLower = Nz(state.PreviousLower);
        var previousUpper = Nz(state.PreviousUpper);

        lower = lower > previousLower || previousClose < previousLower ? lower : previousLower;
        upper = upper < previousUpper || previousClose > previousUpper ? upper : previousUpper;

        int direction;
        if (double.IsNaN(state.PreviousAtr))
            direction = 1;
        else if (state.PreviousSupertrend == previousUpper)
            direction = bar.Close > upper ? -1 : 1;
        else
            direction = bar.Close < lower ? 1 : -1;

        var supertrend = direction == -1 ? lower : upper;

        state.PreviousAtr = atr;
        state.PreviousUpper = upper;
        state.PreviousLower = lower;
        state.PreviousSupertrend = supertrend;

        return Value.Tuple([Value.Float(supertrend), Value.Int(direction)]);
    }

    private static double Nz(double value) => double.IsNaN(value) ? 0 : value;
}
