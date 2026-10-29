using Pine.Core.Runtime;
using Pine.Core.Runtime.Builtins;
using Pine.Core.Syntax;

namespace Backtester.Engine;

public sealed record BacktestOptions
{
    /// <summary>Price tick: unit of <c>profit</c>/<c>loss</c>/<c>trail_*</c> and slippage, value of <c>syminfo.mintick</c>.</summary>
    public double MinTick { get; init; } = 0.01;

    /// <summary>Overrides for <c>input.*</c> default values, keyed by input title.</summary>
    public IReadOnlyDictionary<string, Value>? Inputs { get; init; }
}

/// <param name="OpenTrades">Trades still open after the last bar (not closed automatically, as on TradingView).</param>
/// <param name="Equity">One point per bar, at its close.</param>
public sealed record BacktestResult(
    StrategySettings Settings,
    IReadOnlyList<ClosedTrade> Trades,
    IReadOnlyList<Trade> OpenTrades,
    IReadOnlyList<EquityPoint> Equity,
    ExecutionResult Execution);

public static class Backtest
{
    public static BacktestResult Run(Script script, IReadOnlyList<Bar> bars, BacktestOptions? options = null)
    {
        options ??= new BacktestOptions();

        var broker = new Broker(options.MinTick);
        var registry = BuiltinRegistry.CreateDefault();
        StrategyBuiltins.Register(registry, broker);

        var execution = Interpreter.Run(script, bars, registry, options.Inputs, broker);
        return new BacktestResult(broker.Settings, broker.ClosedTrades, broker.OpenTrades, broker.Equity, execution);
    }
}
