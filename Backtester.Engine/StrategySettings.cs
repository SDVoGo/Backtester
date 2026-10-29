namespace Backtester.Engine;

public enum QuantityType { Fixed, Cash, PercentOfEquity }

public enum CommissionType { Percent, CashPerContract, CashPerOrder }

/// <summary>Arguments of the script's <c>strategy()</c> call, with Pine's defaults.</summary>
public sealed record StrategySettings
{
    public string Title { get; init; } = "";
    public double InitialCapital { get; init; } = 1_000_000;
    public QuantityType DefaultQuantityType { get; init; } = QuantityType.Fixed;
    public double DefaultQuantityValue { get; init; } = 1;

    /// <summary>Maximum entries in the same direction (<c>strategy.entry</c> only).</summary>
    public int Pyramiding { get; init; } = 1;

    public CommissionType CommissionType { get; init; } = CommissionType.Percent;
    public double CommissionValue { get; init; }

    /// <summary>Ticks added against the trader on market and stop fills.</summary>
    public int Slippage { get; init; }

    /// <summary>Fill market orders at the close of the bar that placed them instead of the next open.</summary>
    public bool ProcessOrdersOnClose { get; init; }
}
