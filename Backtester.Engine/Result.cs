using Backtester.Operation;

namespace Backtester.Result;

public class Result(IEnumerable<Trade> Trades)
{
    Dictionary<DateTime, Snapshots> Snapshots = [];
}

/// <summary>
/// Risultato ad ogni operazioni (apertura/chiusura) trade.
/// </summary>
public class Snapshots
{

}