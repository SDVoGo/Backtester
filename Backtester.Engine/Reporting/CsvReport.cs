using System.Globalization;
using System.Text;

namespace Backtester.Engine.Reporting;

/// <summary>
/// Writes a <see cref="BacktestResult"/> as CSV: comma separated, invariant culture,
/// times as ISO 8601 UTC, empty fields for missing values.
/// </summary>
public static class CsvReport
{
    public const string TradesFile = "trades.csv";
    public const string EquityFile = "equity.csv";
    public const string MetricsFile = "metrics.csv";
    public const string PlotsFile = "plots.csv";

    /// <summary>Writes the four reports into <paramref name="directory"/>, creating it if needed.</summary>
    /// <returns>The paths written.</returns>
    public static IReadOnlyList<string> WriteAll(BacktestResult result, string directory)
    {
        Directory.CreateDirectory(directory);
        var files = new (string Name, Action<BacktestResult, TextWriter> Write)[]
        {
            (TradesFile, WriteTrades),
            (EquityFile, WriteEquity),
            (MetricsFile, WriteMetrics),
            (PlotsFile, WritePlots),
        };

        var paths = new List<string>();
        foreach (var (name, write) in files)
        {
            var path = Path.Combine(directory, name);
            using var writer = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            write(result, writer);
            paths.Add(path);
        }
        return paths;
    }

    /// <summary>One row per closed trade, then one per open trade (exit fields empty, profit at the last close).</summary>
    public static void WriteTrades(BacktestResult result, TextWriter writer)
    {
        Row(writer, "trade", "status", "direction", "entry_id", "entry_time", "entry_bar", "entry_price",
            "exit_id", "exit_time", "exit_bar", "exit_price", "quantity", "profit", "profit_percent",
            "cumulative_profit", "commission", "run_up", "drawdown", "entry_comment", "exit_comment");

        var cumulative = 0.0;
        foreach (var t in result.Trades)
        {
            cumulative += t.Profit;
            Row(writer, Int(t.Number), "closed", Direction(t.Direction), t.EntryId, Time(t.EntryTime), Int(t.EntryBar), Number(t.EntryPrice),
                t.ExitId, Time(t.ExitTime), Int(t.ExitBar), Number(t.ExitPrice), Number(t.Quantity), Number(t.Profit), Number(t.ProfitPercent),
                Number(cumulative), Number(t.Commission), Number(t.RunUp), Number(t.Drawdown), t.EntryComment, t.ExitComment);
        }

        var lastClose = result.Equity.Count > 0 ? result.Equity[^1].Close : double.NaN;
        var number = result.Trades.Count;
        foreach (var t in result.OpenTrades)
        {
            var profit = t.ProfitAt(lastClose) - t.EntryCommission;
            Row(writer, Int(++number), "open", Direction(t.Direction), t.EntryId, Time(t.EntryTime), Int(t.EntryBar), Number(t.EntryPrice),
                null, null, null, null, Number(t.Quantity), Number(profit), Number(profit / (t.EntryPrice * t.Quantity) * 100),
                null, Number(t.EntryCommission), null, null, t.EntryComment, null);
        }
    }

    public static void WriteEquity(BacktestResult result, TextWriter writer)
    {
        Row(writer, "bar", "time", "close", "equity", "position_size", "open_profit", "drawdown");
        foreach (var p in result.Equity)
            Row(writer, Int(p.Bar), Time(p.Time), Number(p.Close), Number(p.Equity), Number(p.PositionSize), Number(p.OpenProfit), Number(p.Drawdown));
    }

    /// <summary>Two columns, <c>metric,value</c>, one row per <see cref="StrategyMetrics"/> property in snake_case.</summary>
    public static void WriteMetrics(BacktestResult result, TextWriter writer)
    {
        Row(writer, "metric", "value");
        foreach (var property in typeof(StrategyMetrics).GetProperties())
        {
            var value = property.GetValue(result.Metrics) switch
            {
                double d => Number(d),
                int i => Int(i),
                null => null,
                var other => Convert.ToString(other, CultureInfo.InvariantCulture),
            };
            Row(writer, SnakeCase(property.Name), value);
        }
    }

    /// <summary>One row per bar, one column per <c>plot()</c> call (by title).</summary>
    public static void WritePlots(BacktestResult result, TextWriter writer)
    {
        var plots = result.Execution.Plots;
        Row(writer, ["time", .. plots.Select(p => p.Title)]);

        for (var bar = 0; bar < result.Equity.Count; bar++)
        {
            var values = plots.Select(p => p.Values[bar] is { } v ? Number(v) : null);
            Row(writer, [Time(result.Equity[bar].Time), .. values]);
        }
    }

    private static string Number(double value)
        => double.IsFinite(value) ? value.ToString("0.##########", CultureInfo.InvariantCulture) : "";

    private static string Int(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Time(long unixMilliseconds)
        => DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static string Direction(Direction direction) => direction == Engine.Direction.Long ? "long" : "short";

    private static string SnakeCase(string name)
    {
        var builder = new StringBuilder();
        foreach (var c in name)
        {
            if (char.IsUpper(c) && builder.Length > 0)
                builder.Append('_');
            builder.Append(char.ToLowerInvariant(c));
        }
        return builder.ToString();
    }

    private static void Row(TextWriter writer, params string?[] fields)
        => writer.WriteLine(string.Join(',', fields.Select(Escape)));

    /// <summary>RFC 4180: quote fields containing separators, quotes or line breaks.</summary>
    private static string Escape(string? field)
    {
        if (string.IsNullOrEmpty(field))
            return "";
        return field.IndexOfAny([',', '"', '\n', '\r']) >= 0
            ? $"\"{field.Replace("\"", "\"\"")}\""
            : field;
    }
}
