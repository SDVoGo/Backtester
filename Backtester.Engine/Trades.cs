namespace Backtester.Engine;

public enum Direction { Long = 1, Short = -1 }

/// <summary>An open position fragment created by one entry fill.</summary>
public sealed class Trade
{
    public required string EntryId { get; init; }
    public required Direction Direction { get; init; }
    public required int EntryBar { get; init; }
    public required long EntryTime { get; init; }
    public required double EntryPrice { get; init; }
    public string? EntryComment { get; init; }

    /// <summary>Remaining quantity, always positive; reduced by partial exits.</summary>
    public double Quantity { get; internal set; }

    /// <summary>Entry commission not yet charged to a closed fragment.</summary>
    public double EntryCommission { get; internal set; }

    /// <summary>Best and worst prices reached while open, for run-up and drawdown.</summary>
    public double BestPrice { get; internal set; }
    public double WorstPrice { get; internal set; }

    internal int Sign => (int)Direction;

    public double ProfitAt(double price) => (price - EntryPrice) * Quantity * Sign;

    internal void Track(double price)
    {
        if ((price - BestPrice) * Sign > 0)
            BestPrice = price;
        if ((price - WorstPrice) * Sign < 0)
            WorstPrice = price;
    }
}

/// <param name="Profit">Net of entry and exit commissions.</param>
/// <param name="RunUp">Largest open profit (before commissions) while the trade was open.</param>
/// <param name="Drawdown">Largest open loss (before commissions), as a positive amount.</param>
public sealed record ClosedTrade(
    int Number,
    string EntryId,
    string ExitId,
    Direction Direction,
    double Quantity,
    int EntryBar,
    long EntryTime,
    double EntryPrice,
    int ExitBar,
    long ExitTime,
    double ExitPrice,
    double Profit,
    double ProfitPercent,
    double Commission,
    double RunUp,
    double Drawdown,
    string? EntryComment,
    string? ExitComment);

/// <summary>Strategy state at the close of a bar.</summary>
public sealed record EquityPoint(int Bar, long Time, double Equity, double PositionSize, double OpenProfit, double Drawdown);
