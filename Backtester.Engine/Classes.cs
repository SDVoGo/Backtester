namespace Backtester.Engine;

public record Bar(DateTime Time, double Open, double High, double Low, double Close, double Volume);

public enum TradeType { Buy, Sell }

// Entità mutabile: nasce aperta, si chiude
public class Trade
{
    public TradeType Type { get; }
    public double OpenPrice { get; }
    public double LotSize { get; }
    public DateTime EntryTime { get; }

    public double? ClosePrice { get; private set; }
    public DateTime? ExitTime { get; private set; }

    public bool IsOpen => ClosePrice == null;

    public Trade(TradeType type, double openPrice, double lotSize, DateTime time)
    {
        Type = type;
        OpenPrice = openPrice;
        LotSize = lotSize;
        EntryTime = time;
    }

    public void Close(double price, DateTime time)
    {
        ClosePrice = price;
        ExitTime = time;
    }

    /// <summary>
    /// Profit and Loss.
    /// </summary>
    public double Pnl
    {
        get
        {
            if (IsOpenl) throw new InvalidOperationException("Trade ancora aperto.");
            double diff = Type switch
            {
                TradeType.Buy => ClosePrice.Value - OpenPrice,
                TradeType.Sell => OpenPrice - ClosePrice.Value
            };
            return diff * LotSize;
        }
    }

    /// <summary>
    /// Capitale usato nel trade.
    /// </summary>
    public double CapitalUsed => OpenPrice * LotSize;

    /// <summary>
    /// Ritorno sull'investimento (in %).
    /// </summary>
    public double Roi => Pnl / CapitalUsed; // % di rendimento
}

public enum TradeType
{
    Buy,
    Sell
}

public class Backtester(double InitialCapital)
{
    private double Capital = InitialCapital;
}

public class BacktestResult(IEnumerable<Trade> Trades)
{

}
