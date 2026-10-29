using System.Globalization;
using Backtester.Engine;
using Backtester.Engine.Golden;
using Backtester.Engine.Reporting;
using Pine.Core.Lexing;
using Pine.Core.Parsing;
using Pine.Core.Runtime;

const string Usage = """
    Usage: backtest --script <strategy.pine> --data <ohlcv.csv> [options]

      --out <dir>             Write trades.csv, equity.csv, metrics.csv, plots.csv (default: results)
      --mintick <tick>        Price tick (default: 0.01)
      --input "<title>=<v>"   Override an input by title; repeatable
      --compare <trades.csv>  Compare with TradingView's exported "List of trades"
      --utc-offset <+hh:mm>   Timezone of the TradingView export (default: +00:00)

    Exit codes: 0 ok, 1 error, 2 trades differ from TradingView.
    """;

try
{
    var options = ParseArguments(args);
    if (options is null)
    {
        Console.WriteLine(Usage);
        return args.Length == 0 ? 0 : 1;
    }

    var script = Parser.Parse(File.ReadAllText(options.Script));
    var bars = OhlcvCsv.ReadFile(options.Data);
    var result = Backtest.Run(script, bars, new BacktestOptions { MinTick = options.MinTick, Inputs = options.Inputs });

    var files = CsvReport.WriteAll(result, options.Out);
    PrintSummary(result, bars.Count);
    Console.WriteLine();
    foreach (var file in files)
        Console.WriteLine($"  {file}");

    if (options.Compare is null)
        return 0;

    var report = GoldenComparison.Compare(result.Trades, TradingViewTrades.ReadFile(options.Compare),
        new GoldenOptions { ChartUtcOffset = options.UtcOffset });
    Console.WriteLine();
    Console.Write(report);
    return report.Matches ? 0 : 2;
}
catch (Exception ex) when (ex is LexerException or ParseException or PineRuntimeException or CsvFormatException
                               or FormatException or IOException or ArgumentException)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}

static CliOptions? ParseArguments(string[] args)
{
    string? script = null, data = null, compare = null, outDir = "results";
    var minTick = 0.01;
    var offset = TimeSpan.Zero;
    var inputs = new Dictionary<string, Value>();

    for (var i = 0; i < args.Length; i++)
    {
        string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Missing value for {args[i]}");

        switch (args[i])
        {
            case "--script": script = Next(); break;
            case "--data": data = Next(); break;
            case "--out": outDir = Next(); break;
            case "--compare": compare = Next(); break;
            case "--mintick": minTick = double.Parse(Next(), CultureInfo.InvariantCulture); break;
            case "--utc-offset": offset = TimeSpan.Parse(Next().TrimStart('+'), CultureInfo.InvariantCulture); break;
            case "--input":
                var pair = Next();
                var equals = pair.IndexOf('=');
                if (equals <= 0)
                    throw new ArgumentException($"Expected --input \"Title=value\", got '{pair}'");
                inputs[pair[..equals]] = InputValues.FromText(pair[(equals + 1)..]);
                break;
            case "--help" or "-h":
                return null;
            default:
                throw new ArgumentException($"Unknown option '{args[i]}'\n\n{Usage}");
        }
    }

    if (script is null || data is null)
        return null;

    return new CliOptions(script, data, outDir, minTick, inputs, compare, offset);
}

static void PrintSummary(BacktestResult result, int barCount)
{
    var m = result.Metrics;
    static string N(double? value) => value is { } v ? v.ToString("#,0.##", CultureInfo.InvariantCulture) : "n/a";

    Console.WriteLine($"{result.Settings.Title} — {barCount} bars");
    Console.WriteLine();
    Console.WriteLine($"  Net profit          {N(m.NetProfit)} ({N(m.NetProfitPercent)}%)");
    Console.WriteLine($"  Gross profit / loss {N(m.GrossProfit)} / {N(m.GrossLoss)}");
    Console.WriteLine($"  Profit factor       {N(m.ProfitFactor)}");
    Console.WriteLine($"  Closed trades       {m.TotalClosedTrades} ({m.WinningTrades} won, {m.LosingTrades} lost, {N(m.PercentProfitable)}% profitable)");
    Console.WriteLine($"  Open trades         {m.TotalOpenTrades} (open profit {N(m.OpenProfit)})");
    Console.WriteLine($"  Max drawdown        {N(m.MaxDrawdown)} ({N(m.MaxDrawdownPercent)}%)");
    Console.WriteLine($"  Buy & hold          {N(m.BuyAndHoldReturnPercent)}%");
    Console.WriteLine($"  Sharpe / Sortino    {N(m.SharpeRatio)} / {N(m.SortinoRatio)}");
    Console.WriteLine($"  Commission paid     {N(m.CommissionPaid)}");
}

internal sealed record CliOptions(
    string Script, string Data, string Out, double MinTick,
    IReadOnlyDictionary<string, Value> Inputs, string? Compare, TimeSpan UtcOffset);
