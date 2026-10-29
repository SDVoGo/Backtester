using Pine.Core.Runtime;

namespace Backtester.Engine.Reporting;

/// <summary>
/// Summary of a backtest, following TradingView's "Performance summary".
/// Ratios that are undefined (no losing trades, too few months) are null.
/// Drawdown and run-up are measured on the equity at each bar's close.
/// </summary>
public sealed record StrategyMetrics
{
    /// <summary>Annual risk-free rate used by Sharpe and Sortino, as TradingView's default.</summary>
    public const double RiskFreeRate = 0.02;

    public double InitialCapital { get; init; }
    public double FinalEquity { get; init; }
    public double NetProfit { get; init; }
    public double NetProfitPercent { get; init; }
    public double GrossProfit { get; init; }
    public double GrossLoss { get; init; }
    public double? ProfitFactor { get; init; }
    public double OpenProfit { get; init; }
    public double CommissionPaid { get; init; }

    public int TotalClosedTrades { get; init; }
    public int TotalOpenTrades { get; init; }
    public int WinningTrades { get; init; }
    public int LosingTrades { get; init; }
    public int EvenTrades { get; init; }
    public double? PercentProfitable { get; init; }

    public double? AverageTrade { get; init; }
    public double? AverageWinningTrade { get; init; }
    public double? AverageLosingTrade { get; init; }
    public double? RatioAverageWinAverageLoss { get; init; }
    public double? LargestWinningTrade { get; init; }
    public double? LargestLosingTrade { get; init; }
    public double? AverageBarsInTrade { get; init; }

    public double MaxDrawdown { get; init; }
    public double MaxDrawdownPercent { get; init; }
    public double MaxRunUp { get; init; }
    public double MaxRunUpPercent { get; init; }

    /// <summary>Price change from the first to the last close, in percent.</summary>
    public double? BuyAndHoldReturnPercent { get; init; }

    /// <summary>On monthly returns of equity, excess over <see cref="RiskFreeRate"/>/12.</summary>
    public double? SharpeRatio { get; init; }
    public double? SortinoRatio { get; init; }

    public static StrategyMetrics Compute(
        StrategySettings settings, IReadOnlyList<ClosedTrade> trades, IReadOnlyList<Trade> openTrades,
        IReadOnlyList<EquityPoint> equity, IReadOnlyList<Bar> bars)
    {
        var initial = settings.InitialCapital;
        var wins = trades.Where(t => t.Profit > 0).ToList();
        var losses = trades.Where(t => t.Profit < 0).ToList();
        var grossProfit = wins.Sum(t => t.Profit);
        var grossLoss = -losses.Sum(t => t.Profit);
        var netProfit = trades.Sum(t => t.Profit);
        var lastClose = bars.Count > 0 ? bars[^1].Close : double.NaN;

        double? averageWin = wins.Count > 0 ? grossProfit / wins.Count : null;
        double? averageLoss = losses.Count > 0 ? grossLoss / losses.Count : null;

        var (maxDrawdown, maxDrawdownPercent, maxRunUp, maxRunUpPercent) = Excursions(initial, equity);
        var (sharpe, sortino) = MonthlyRatios(initial, equity);

        return new StrategyMetrics
        {
            InitialCapital = initial,
            FinalEquity = equity.Count > 0 ? equity[^1].Equity : initial,
            NetProfit = netProfit,
            NetProfitPercent = netProfit / initial * 100,
            GrossProfit = grossProfit,
            GrossLoss = grossLoss,
            ProfitFactor = grossLoss > 0 ? grossProfit / grossLoss : null,
            OpenProfit = openTrades.Sum(t => t.ProfitAt(lastClose)),
            CommissionPaid = trades.Sum(t => t.Commission) + openTrades.Sum(t => t.EntryCommission),
            TotalClosedTrades = trades.Count,
            TotalOpenTrades = openTrades.Count,
            WinningTrades = wins.Count,
            LosingTrades = losses.Count,
            EvenTrades = trades.Count - wins.Count - losses.Count,
            PercentProfitable = trades.Count > 0 ? 100.0 * wins.Count / trades.Count : null,
            AverageTrade = trades.Count > 0 ? netProfit / trades.Count : null,
            AverageWinningTrade = averageWin,
            AverageLosingTrade = averageLoss,
            RatioAverageWinAverageLoss = averageWin is { } w && averageLoss is { } l && l > 0 ? w / l : null,
            LargestWinningTrade = wins.Count > 0 ? wins.Max(t => t.Profit) : null,
            LargestLosingTrade = losses.Count > 0 ? -losses.Min(t => t.Profit) : null,
            AverageBarsInTrade = trades.Count > 0 ? trades.Average(t => t.ExitBar - t.EntryBar) : null,
            MaxDrawdown = maxDrawdown,
            MaxDrawdownPercent = maxDrawdownPercent,
            MaxRunUp = maxRunUp,
            MaxRunUpPercent = maxRunUpPercent,
            BuyAndHoldReturnPercent = bars.Count > 0 && bars[0].Close != 0 ? (lastClose / bars[0].Close - 1) * 100 : null,
            SharpeRatio = sharpe,
            SortinoRatio = sortino,
        };
    }

    /// <summary>Largest fall from a previous peak and largest rise from a previous trough, starting from the initial capital.</summary>
    private static (double Drawdown, double DrawdownPercent, double RunUp, double RunUpPercent) Excursions(
        double initial, IReadOnlyList<EquityPoint> equity)
    {
        double peak = initial, trough = initial;
        double drawdown = 0, drawdownPercent = 0, runUp = 0, runUpPercent = 0;

        foreach (var point in equity)
        {
            peak = Math.Max(peak, point.Equity);
            trough = Math.Min(trough, point.Equity);

            drawdown = Math.Max(drawdown, peak - point.Equity);
            runUp = Math.Max(runUp, point.Equity - trough);
            if (peak > 0)
                drawdownPercent = Math.Max(drawdownPercent, (peak - point.Equity) / peak * 100);
            if (trough > 0)
                runUpPercent = Math.Max(runUpPercent, (point.Equity - trough) / trough * 100);
        }

        return (drawdown, drawdownPercent, runUp, runUpPercent);
    }

    /// <summary>Sharpe and Sortino on calendar-month returns (UTC); null with fewer than two months.</summary>
    private static (double? Sharpe, double? Sortino) MonthlyRatios(double initial, IReadOnlyList<EquityPoint> equity)
    {
        var monthEnds = equity
            .GroupBy(p => DateTimeOffset.FromUnixTimeMilliseconds(p.Time) is var d ? d.Year * 12 + d.Month : 0)
            .Select(g => g.Last().Equity)
            .ToList();

        if (monthEnds.Count < 2)
            return (null, null);

        var excess = new List<double>(monthEnds.Count);
        var previous = initial;
        foreach (var value in monthEnds)
        {
            excess.Add(value / previous - 1 - RiskFreeRate / 12);
            previous = value;
        }

        var mean = excess.Average();
        var deviation = Math.Sqrt(excess.Sum(r => (r - mean) * (r - mean)) / excess.Count);
        var downside = Math.Sqrt(excess.Sum(r => r < 0 ? r * r : 0) / excess.Count);

        return (deviation > 0 ? mean / deviation : null, downside > 0 ? mean / downside : null);
    }
}
