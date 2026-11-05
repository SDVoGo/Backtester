namespace Backtester.Operation;

/// <summary>
/// Direzione del trade.
/// </summary>
public enum TradeType { Buy, Sell }

/// <summary>
/// Rappresenta un trade.
/// </summary>
/// <param name="type"></param>
/// <param name="openPrice"></param>
/// <param name="lotSize"></param>
/// <param name="time"></param>
public class Trade(TradeType type, double openPrice, double lotSize, DateTime time)
{
    public TradeType Type { get; } = type;
    public double OpenPrice { get; } = openPrice;
    public double LotSize { get; } = lotSize > 0 ? lotSize : throw new ArgumentOutOfRangeException(nameof(lotSize));
    public DateTime EntryTime { get; } = time;

    public double? ClosePrice { get; private set; }
    public DateTime? ExitTime { get; private set; }

    public bool IsOpen => ClosePrice == null;
    public double Pips
    {
        get
        {
            if (IsOpen)
                throw new InvalidOperationException("Trade ancora aperto.");

            double closePrice = ClosePrice!.Value;

            return Type switch
            {
                TradeType.Buy => closePrice - OpenPrice,
                TradeType.Sell => OpenPrice - closePrice,
                _ => throw new NotImplementedException(),
            };
        }
    }

    public void Close(double price, DateTime time)
    {
        if (!IsOpen) throw new InvalidOperationException("Trade ancora aperto.");

        ClosePrice = price;
        ExitTime = time;
    }

    /// <summary>
    /// Profit and Loss.
    /// </summary>
    public double Pnl => Pips * LotSize;

    /// <summary>
    /// Capitale usato nel trade.
    /// </summary>
    public double CapitalUsed => OpenPrice * LotSize;

    /// <summary>
    /// Ritorno sull'investimento (in %).
    /// </summary>
    public double Roi => Pnl / CapitalUsed; // % di rendimento
}