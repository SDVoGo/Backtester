namespace Pine.Core.Runtime;

/// <summary>One OHLCV bar. <see cref="Time"/> is the bar open time in unix milliseconds, as Pine's <c>time</c>.</summary>
public readonly record struct Bar(long Time, double Open, double High, double Low, double Close, double Volume);

public sealed class PineRuntimeException(string message, Syntax.SourcePos pos, int barIndex)
    : Exception($"{message} (line {pos.Line}, column {pos.Column}, bar {barIndex})")
{
    public Syntax.SourcePos Pos { get; } = pos;
    public int BarIndex { get; } = barIndex;
}
