using System.Globalization;
using System.Text;

namespace Backtester.Engine.Golden;

public sealed record GoldenOptions
{
    /// <summary>Largest accepted price difference; TradingView exports prices rounded to the tick.</summary>
    public double PriceTolerance { get; init; } = 1e-6;

    public double QuantityTolerance { get; init; } = 1e-6;

    /// <summary>Largest accepted profit difference; null skips the profit check.</summary>
    public double? ProfitTolerance { get; init; } = 0.01;

    /// <summary>
    /// Offset of the chart timezone the export was made in (TradingView writes local times);
    /// null skips the time check.
    /// </summary>
    public TimeSpan? ChartUtcOffset { get; init; } = TimeSpan.Zero;

    /// <summary>Stop listing differences after this many.</summary>
    public int MaxDifferences { get; init; } = 50;
}

public sealed record TradeDifference(int Trade, string Field, string Expected, string Actual);

public sealed record ComparisonReport(int ExpectedTrades, int ActualTrades, IReadOnlyList<TradeDifference> Differences)
{
    public bool Matches => Differences.Count == 0;

    public override string ToString()
    {
        var builder = new StringBuilder();
        builder.AppendLine(Matches
            ? $"OK: {ActualTrades} trades match TradingView"
            : $"{Differences.Count} differences (TradingView {ExpectedTrades} trades, backtester {ActualTrades})");

        foreach (var d in Differences)
            builder.AppendLine($"  trade {d.Trade}, {d.Field}: TradingView {d.Expected}, backtester {d.Actual}");

        return builder.ToString();
    }
}

/// <summary>
/// Compares closed trades with TradingView's list of trades, in order. The open trade at the end
/// of TradingView's list is ignored: whether a trade is still open depends on where the data ends.
/// </summary>
public static class GoldenComparison
{
    public static ComparisonReport Compare(
        IReadOnlyList<ClosedTrade> actual, IReadOnlyList<TradingViewTrade> expected, GoldenOptions? options = null)
    {
        options ??= new GoldenOptions();
        var closed = expected.Where(t => !t.IsOpen).ToList();
        var differences = new List<TradeDifference>();

        void Add(int trade, string field, object expectedValue, object actualValue)
        {
            if (differences.Count < options.MaxDifferences)
                differences.Add(new TradeDifference(trade, field, Format(expectedValue), Format(actualValue)));
        }

        if (closed.Count != actual.Count)
            Add(0, "count", closed.Count, actual.Count);

        for (var i = 0; i < Math.Min(closed.Count, actual.Count); i++)
        {
            var tv = closed[i];
            var ours = actual[i];
            var number = i + 1;

            if (tv.Direction != ours.Direction)
                Add(number, "direction", tv.Direction, ours.Direction);
            if (Math.Abs(tv.EntryPrice - ours.EntryPrice) > options.PriceTolerance)
                Add(number, "entry price", tv.EntryPrice, ours.EntryPrice);
            if (tv.ExitPrice is { } exitPrice && Math.Abs(exitPrice - ours.ExitPrice) > options.PriceTolerance)
                Add(number, "exit price", exitPrice, ours.ExitPrice);
            if (Math.Abs(tv.Quantity - ours.Quantity) > options.QuantityTolerance)
                Add(number, "quantity", tv.Quantity, ours.Quantity);
            if (options.ProfitTolerance is { } tolerance && tv.Profit is { } profit && Math.Abs(profit - ours.Profit) > tolerance)
                Add(number, "profit", profit, ours.Profit);

            if (options.ChartUtcOffset is { } offset)
            {
                var entry = ChartTime(ours.EntryTime, offset);
                if (entry != tv.EntryTime)
                    Add(number, "entry time", tv.EntryTime, entry);
                var exit = ChartTime(ours.ExitTime, offset);
                if (tv.ExitTime is { } exitTime && exit != exitTime)
                    Add(number, "exit time", exitTime, exit);
            }
        }

        return new ComparisonReport(closed.Count, actual.Count, differences);
    }

    /// <summary>Unix milliseconds as the chart shows them, truncated to the minute like the export.</summary>
    private static DateTime ChartTime(long unixMilliseconds, TimeSpan offset)
    {
        var local = DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds).ToOffset(offset).DateTime;
        return new DateTime(local.Year, local.Month, local.Day, local.Hour, local.Minute, 0);
    }

    private static string Format(object value)
        => value switch
        {
            double d => d.ToString("0.##########", CultureInfo.InvariantCulture),
            DateTime t => t.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
        };
}
