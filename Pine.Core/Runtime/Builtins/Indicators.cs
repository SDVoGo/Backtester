namespace Pine.Core.Runtime.Builtins;

// Building blocks for stateful built-ins. They work on doubles where NaN is na, so they
// compose without Value boxing; Value.Float turns NaN back into na at the end.
// Each one is owned by a single call site (directly or inside that call site's state).

/// <summary>The most recent values pushed, newest first; grows up to the longest window asked for.</summary>
internal sealed class History
{
    private double[] _buffer = new double[8];
    private int _head = -1;     // index of the newest value
    private int _count;

    public int Count => _count;

    /// <param name="keep">Number of values the caller will read back; the buffer grows to hold them.</param>
    public void Push(double value, int keep)
    {
        if (keep > _buffer.Length)
            Grow(Math.Min(Math.Max(keep, _buffer.Length * 2), Series.MaxBarsBack + 1));

        _head = (_head + 1) % _buffer.Length;
        _buffer[_head] = value;
        if (_count < _buffer.Length)
            _count++;
    }

    /// <summary>Value <paramref name="offset"/> pushes back (0 = newest); NaN when not available.</summary>
    public double this[int offset]
    {
        get
        {
            if (offset < 0 || offset >= _count)
                return double.NaN;

            var index = _head - offset;
            return _buffer[index < 0 ? index + _buffer.Length : index];
        }
    }

    /// <summary>Sum of the newest <paramref name="length"/> values; NaN if any is NaN.</summary>
    public double Sum(int length)
    {
        var sum = 0.0;
        for (var i = 0; i < length; i++)
            sum += this[i];
        return sum;
    }

    private void Grow(int size)
    {
        if (size <= _buffer.Length)
            return;

        // Unroll the ring so the oldest value is at 0
        var grown = new double[size];
        for (var i = 0; i < _count; i++)
            grown[i] = this[_count - 1 - i];

        _buffer = grown;
        _head = _count - 1;
    }
}

/// <summary>Simple moving average; NaN until <c>length</c> values are seen or if any of them is NaN.</summary>
internal sealed class SmaCalc
{
    private readonly History _history = new();

    public double Next(double value, int length)
    {
        _history.Push(value, length);
        return _history.Count < length ? double.NaN : _history.Sum(length) / length;
    }
}

/// <summary>
/// Exponential smoothing seeded with an SMA, as Pine's <c>ta.ema</c> (alpha = 2/(n+1)) and
/// <c>ta.rma</c> (alpha = 1/n): <c>na(prev) ? sma(src, n) : alpha * src + (1 - alpha) * prev</c>.
/// </summary>
internal sealed class SmoothedCalc
{
    private readonly SmaCalc _seed = new();
    private double _previous = double.NaN;

    public double Next(double value, int length, double alpha)
    {
        // The seed SMA sees every bar, so after an na the average restarts from a clean window
        var sma = _seed.Next(value, length);
        _previous = double.IsNaN(_previous) ? sma : alpha * value + (1 - alpha) * _previous;
        return _previous;
    }

    public double Ema(double value, int length) => Next(value, length, 2.0 / (length + 1));

    public double Rma(double value, int length) => Next(value, length, 1.0 / length);
}

/// <summary>Standard deviation over the last <c>length</c> values, as Pine's <c>ta.stdev</c>.</summary>
internal sealed class StdevCalc
{
    private readonly History _history = new();

    public double Next(double value, int length, bool biased)
    {
        _history.Push(value, length);
        if (_history.Count < length)
            return double.NaN;

        var mean = _history.Sum(length) / length;
        var squares = 0.0;
        for (var i = 0; i < length; i++)
        {
            var deviation = _history[i] - mean;
            // Pine clamps deviations that are pure rounding noise to zero
            if (Math.Abs(deviation) <= 1e-10)
                deviation = 0;
            squares += deviation * deviation;
        }

        var divisor = biased ? length : length - 1;
        return divisor > 0 ? Math.Sqrt(squares / divisor) : double.NaN;
    }
}

internal static class TrueRange
{
    /// <summary>
    /// <c>max(high - low, |high - close[1]|, |low - close[1]|)</c>; without a previous bar it is
    /// <c>high - low</c> when <paramref name="handleNa"/>, NaN otherwise.
    /// </summary>
    public static double Of(Bar bar, Bar? previous, bool handleNa)
    {
        if (previous is not { } prev)
            return handleNa ? bar.High - bar.Low : double.NaN;

        return Math.Max(bar.High - bar.Low, Math.Max(Math.Abs(bar.High - prev.Close), Math.Abs(bar.Low - prev.Close)));
    }
}
