using Pine.Core.Runtime;
using Pine.Core.Runtime.Builtins;

namespace Backtester.Engine;

internal enum OrderKind { Entry, Order, Close, CloseAll, Exit }

/// <summary>A pending order. Entries/orders without prices and closes are market orders.</summary>
internal sealed class Order
{
    public required string Id { get; init; }
    public required OrderKind Kind { get; init; }
    public Direction Direction { get; set; }
    public double? Quantity { get; set; }
    public double? QuantityPercent { get; set; }
    public double? Limit { get; set; }
    public double? Stop { get; set; }
    public string? Comment { get; set; }

    /// <summary>Stop-limit entry whose stop was hit: it now waits for the limit.</summary>
    public bool StopTriggered { get; set; }

    // strategy.exit only
    public string? FromEntry { get; set; }
    public double? ProfitTicks { get; set; }
    public double? LossTicks { get; set; }
    public double? TrailPrice { get; set; }
    public double? TrailPoints { get; set; }
    public double? TrailOffset { get; set; }
    public string? CommentProfit { get; set; }
    public string? CommentLoss { get; set; }
    public string? CommentTrailing { get; set; }
    public bool TrailActive { get; set; }
    public double TrailExtreme { get; set; }

    public bool IsMarket => Kind is OrderKind.Close or OrderKind.CloseAll
        || (Kind is OrderKind.Entry or OrderKind.Order && Limit is null && Stop is null);
}

/// <summary>
/// Simulated broker following TradingView's emulator: orders placed while the script runs on a bar
/// fill from the next bar (market orders at its open, or at this bar's close with
/// <c>process_orders_on_close</c>); stop and limit orders fill along an assumed intrabar path
/// open → nearest extreme → other extreme → close.
/// </summary>
public sealed class Broker(double minTick = 0.01) : IBarHooks
{
    private const double Epsilon = 1e-12;
    private const int MaxFillsPerSegment = 1000;

    private readonly List<Order> _orders = [];
    private readonly List<Trade> _openTrades = [];
    private readonly List<ClosedTrade> _closedTrades = [];
    private readonly List<EquityPoint> _equity = [];
    private readonly List<Snapshot> _snapshots = [];

    private bool _configured;
    private BarContext? _context;
    private double _peakEquity = double.NaN;

    public double MinTick { get; } = minTick;

    public StrategySettings Settings { get; private set; } = new();

    public IReadOnlyList<Trade> OpenTrades => _openTrades;

    public IReadOnlyList<ClosedTrade> ClosedTrades => _closedTrades;

    public IReadOnlyList<EquityPoint> Equity => _equity;

    public double NetProfit { get; private set; }
    public double GrossProfit { get; private set; }
    public double GrossLoss { get; private set; }
    public int WinTrades { get; private set; }
    public int LossTrades { get; private set; }
    public double MaxDrawdown { get; private set; }

    public double PositionSize => _openTrades.Sum(t => t.Quantity * t.Sign);

    /// <summary>Quantity-weighted entry price of the open trades; NaN when flat.</summary>
    public double PositionAveragePrice => AveragePrice(_openTrades);

    public double OpenProfitAt(double price) => _openTrades.Sum(t => t.ProfitAt(price));

    public double EquityAt(double price) => Settings.InitialCapital + NetProfit + OpenProfitAt(price);

    private Bar CurrentBar => _context!.Bar;

    private int BarIndex => _context?.BarIndex ?? 0;

    /// <summary>The first <c>strategy()</c> call wins: settings are constant for the run.</summary>
    internal void Configure(StrategySettings settings)
    {
        if (_configured)
            return;

        Settings = settings;
        _configured = true;
    }

    // ── Order placement (called by strategy.* during the script) ──

    internal void Enter(OrderKind kind, string id, Direction direction, double? quantity, double? limit, double? stop, string? comment)
    {
        _orders.RemoveAll(o => o.Id == id && o.Kind == kind);
        _orders.Add(new Order
        {
            Id = id,
            Kind = kind,
            Direction = direction,
            Quantity = quantity ?? DefaultQuantity(limit ?? stop ?? CurrentBar.Close),
            Limit = limit,
            Stop = stop,
            Comment = comment,
        });
    }

    internal void Close(string? entryId, double? quantity, double? quantityPercent, string? comment, bool immediately)
    {
        var kind = entryId is null ? OrderKind.CloseAll : OrderKind.Close;
        var order = new Order
        {
            Id = entryId ?? "Close all",
            Kind = kind,
            Quantity = quantity,
            QuantityPercent = quantityPercent,
            Comment = comment,
        };

        if (immediately)
        {
            FillMarket(order, CurrentBar.Close);
            return;
        }

        _orders.RemoveAll(o => o.Id == order.Id && o.Kind == kind);
        _orders.Add(order);
    }

    /// <summary>Creates or updates the exit with this id; a trailing stop already running keeps its state.</summary>
    internal void Exit(string id, string? fromEntry, Action<Order> configure)
    {
        var order = _orders.Find(o => o.Id == id && o.Kind == OrderKind.Exit && o.FromEntry == fromEntry);
        if (order is null)
        {
            order = new Order { Id = id, Kind = OrderKind.Exit, FromEntry = fromEntry };
            _orders.Add(order);
        }

        configure(order);
    }

    internal void Cancel(string? id)
        => _orders.RemoveAll(o => id is null || o.Id == id);

    private double DefaultQuantity(double price)
        => Settings.DefaultQuantityType switch
        {
            QuantityType.Cash => Settings.DefaultQuantityValue / price,
            QuantityType.PercentOfEquity => EquityAt(CurrentBar.Close) * Settings.DefaultQuantityValue / 100 / price,
            _ => Settings.DefaultQuantityValue,
        };

    // ── Bar hooks ───────────────────────────────────────────

    public void BeforeBar(BarContext context)
    {
        _context = context;
        if (double.IsNaN(_peakEquity))
            _peakEquity = Settings.InitialCapital;

        var bar = context.Bar;
        FillMarketOrders(bar.Open);
        UpdateTrailing(bar.Open);
        Track(bar.Open);

        var path = Path(bar);
        for (var i = 1; i < path.Length; i++)
        {
            WalkSegment(path[i - 1], path[i]);
            UpdateTrailing(path[i]);
            Track(path[i]);
        }

        _snapshots.Add(Snapshot.Of(this, bar.Close));
    }

    public void AfterBar(BarContext context)
    {
        var bar = context.Bar;
        if (Settings.ProcessOrdersOnClose)
            FillMarketOrders(bar.Close);

        var equity = EquityAt(bar.Close);
        _peakEquity = Math.Max(_peakEquity, equity);
        var drawdown = _peakEquity - equity;
        MaxDrawdown = Math.Max(MaxDrawdown, drawdown);

        _equity.Add(new EquityPoint(context.BarIndex, bar.Time, equity, PositionSize, OpenProfitAt(bar.Close), drawdown));
    }

    /// <summary>State seen by the script <paramref name="offset"/> bars back (for <c>strategy.equity[1]</c>).</summary>
    internal Snapshot? SnapshotAt(int offset)
    {
        var index = BarIndex - offset;
        return index >= 0 && index < _snapshots.Count ? _snapshots[index] : null;
    }

    /// <summary>TradingView's assumption: from the open, price visits the nearer extreme first.</summary>
    private static double[] Path(Bar bar)
        => bar.High - bar.Open <= bar.Open - bar.Low
            ? [bar.Open, bar.High, bar.Low, bar.Close]
            : [bar.Open, bar.Low, bar.High, bar.Close];

    // ── Fills ───────────────────────────────────────────────

    private void FillMarketOrders(double price)
    {
        var market = _orders.Where(o => o.IsMarket).ToList();
        foreach (var order in market)
        {
            _orders.Remove(order);
            FillMarket(order, price);
        }
    }

    private void FillMarket(Order order, double price)
    {
        switch (order.Kind)
        {
            case OrderKind.Entry or OrderKind.Order:
                Open(order, Slip(price, buy: order.Direction == Direction.Long));
                break;

            case OrderKind.Close or OrderKind.CloseAll:
                var targets = order.Kind == OrderKind.CloseAll
                    ? _openTrades.ToList()
                    : _openTrades.Where(t => t.EntryId == order.Id).ToList();
                if (targets.Count == 0)
                    return;

                var total = targets.Sum(t => t.Quantity);
                var quantity = ExitQuantity(order, total);
                CloseTrades(targets, quantity, Slip(price, buy: targets[0].Direction == Direction.Short), order.Id, order.Comment);
                break;
        }
    }

    private double ExitQuantity(Order order, double total)
        => Math.Min(total, order.Quantity ?? (order.QuantityPercent is { } percent ? total * percent / 100 : total));

    private double Slip(double price, bool buy)
        => price + (buy ? 1 : -1) * Settings.Slippage * MinTick;

    private double Commission(double quantity, double price)
        => Settings.CommissionType switch
        {
            CommissionType.CashPerContract => quantity * Settings.CommissionValue,
            CommissionType.CashPerOrder => Settings.CommissionValue,
            _ => quantity * price * Settings.CommissionValue / 100,
        };

    /// <summary>
    /// <c>strategy.entry</c> reverses an opposite position and respects pyramiding;
    /// <c>strategy.order</c> just adds its quantity, reducing an opposite position first.
    /// </summary>
    private void Open(Order order, double price)
    {
        var quantity = order.Quantity ?? 0;
        if (quantity <= Epsilon)
            return;

        var sign = (int)order.Direction;
        var position = PositionSize;

        if (order.Kind == OrderKind.Entry)
        {
            if (position * sign < 0)
                CloseTrades(_openTrades.ToList(), Math.Abs(position), price, order.Id, order.Comment);
            else if (position * sign > 0 && _openTrades.Count >= Settings.Pyramiding)
                return;
        }
        else if (position * sign < 0)
        {
            var reduce = Math.Min(quantity, Math.Abs(position));
            CloseTrades(_openTrades.ToList(), reduce, price, order.Id, order.Comment);
            quantity -= reduce;
            if (quantity <= Epsilon)
                return;
        }

        _openTrades.Add(new Trade
        {
            EntryId = order.Id,
            Direction = order.Direction,
            EntryBar = BarIndex,
            EntryTime = CurrentBar.Time,
            EntryPrice = price,
            EntryComment = order.Comment,
            Quantity = quantity,
            EntryCommission = Commission(quantity, price),
            BestPrice = price,
            WorstPrice = price,
        });
    }

    /// <summary>Closes <paramref name="quantity"/> from the trades in FIFO order, splitting the last one if needed.</summary>
    private void CloseTrades(List<Trade> trades, double quantity, double price, string exitId, string? comment)
    {
        // A cash-per-order commission is charged once for the whole exit order
        var orderCommission = Settings.CommissionType == CommissionType.CashPerOrder ? Settings.CommissionValue : double.NaN;

        foreach (var trade in trades)
        {
            if (quantity <= Epsilon)
                break;

            var take = Math.Min(trade.Quantity, quantity);
            var fraction = take / trade.Quantity;
            var entryCommission = trade.EntryCommission * fraction;
            var exitCommission = double.IsNaN(orderCommission) ? Commission(take, price) : orderCommission;
            orderCommission = double.IsNaN(orderCommission) ? double.NaN : 0;

            trade.Track(price);
            var gross = (price - trade.EntryPrice) * take * trade.Sign;
            var commission = entryCommission + exitCommission;
            var profit = gross - commission;

            _closedTrades.Add(new ClosedTrade(
                Number: _closedTrades.Count + 1,
                EntryId: trade.EntryId,
                ExitId: exitId,
                Direction: trade.Direction,
                Quantity: take,
                EntryBar: trade.EntryBar,
                EntryTime: trade.EntryTime,
                EntryPrice: trade.EntryPrice,
                ExitBar: BarIndex,
                ExitTime: CurrentBar.Time,
                ExitPrice: price,
                Profit: profit,
                ProfitPercent: profit / (trade.EntryPrice * take) * 100,
                Commission: commission,
                RunUp: Math.Max(0, (trade.BestPrice - trade.EntryPrice) * trade.Sign * take),
                Drawdown: Math.Max(0, (trade.EntryPrice - trade.WorstPrice) * trade.Sign * take),
                EntryComment: trade.EntryComment,
                ExitComment: comment));

            NetProfit += profit;
            if (profit > 0)
            {
                GrossProfit += profit;
                WinTrades++;
            }
            else if (profit < 0)
            {
                GrossLoss -= profit;
                LossTrades++;
            }

            trade.Quantity -= take;
            trade.EntryCommission -= entryCommission;
            if (trade.Quantity <= Epsilon)
                _openTrades.Remove(trade);

            quantity -= take;
        }

        // Exits lose their purpose once the position they protect is gone
        if (_openTrades.Count == 0)
            _orders.RemoveAll(o => o.Kind == OrderKind.Exit);
    }

    // ── Intrabar path ───────────────────────────────────────

    /// <param name="FillsOrder">False for the stop of a stop-limit entry, which only arms the limit.</param>
    /// <param name="IsLimit">Limit fills get no slippage.</param>
    private readonly record struct Trigger(Order Order, double Price, bool FillsOrder, bool IsLimit, string? Comment);

    /// <summary>Fills price orders in the order the path from <paramref name="from"/> to <paramref name="to"/> reaches them.</summary>
    private void WalkSegment(double from, double to)
    {
        for (var fills = 0; fills < MaxFillsPerSegment; fills++)
        {
            Trigger? first = null;
            foreach (var order in _orders)
            {
                if (FindTrigger(order, from, to) is { } trigger
                    && (first is null || Math.Abs(trigger.Price - from) < Math.Abs(first.Value.Price - from)))
                    first = trigger;
            }

            if (first is not { } hit)
                return;

            Execute(hit);
            Track(hit.Price);
            from = hit.Price;
        }
    }

    private void Execute(Trigger trigger)
    {
        var order = trigger.Order;
        if (!trigger.FillsOrder)
        {
            // Stop of a stop-limit entry: from now on it is a limit order
            order.StopTriggered = true;
            return;
        }

        _orders.Remove(order);

        if (order.Kind == OrderKind.Exit)
        {
            var targets = ExitTargets(order);
            var price = trigger.IsLimit ? trigger.Price : Slip(trigger.Price, buy: targets[0].Direction == Direction.Short);
            CloseTrades(targets, ExitQuantity(order, targets.Sum(t => t.Quantity)), price, order.Id, trigger.Comment);
            return;
        }

        Open(order, trigger.IsLimit ? trigger.Price : Slip(trigger.Price, buy: order.Direction == Direction.Long));
    }

    private Trigger? FindTrigger(Order order, double from, double to)
    {
        if (order.IsMarket)
            return null;

        if (order.Kind == OrderKind.Exit)
            return FindExitTrigger(order, from, to);

        var buy = order.Direction == Direction.Long;

        if (order.Stop is { } stop && !order.StopTriggered)
        {
            if (Cross(from, to, stop, upward: buy) is not { } price)
                return null;
            // A plain stop order fills; a stop-limit only arms its limit
            return new Trigger(order, price, FillsOrder: order.Limit is null, IsLimit: false, order.Comment);
        }

        return order.Limit is { } limit && Cross(from, to, limit, upward: !buy) is { } fill
            ? new Trigger(order, fill, FillsOrder: true, IsLimit: true, order.Comment)
            : null;
    }

    private Trigger? FindExitTrigger(Order order, double from, double to)
    {
        var targets = ExitTargets(order);
        if (targets.Count == 0)
            return null;

        var isLong = targets[0].Direction == Direction.Long;
        Trigger? best = null;

        void Consider(double? level, bool upward, bool isLimit, string? comment)
        {
            if (level is { } l && Cross(from, to, l, upward) is { } price
                && (best is null || Math.Abs(price - from) < Math.Abs(best.Value.Price - from)))
                best = new Trigger(order, price, FillsOrder: true, isLimit, comment ?? order.Comment);
        }

        // Take profit is above a long position, stops below
        Consider(ExitLimit(order, targets), upward: isLong, isLimit: true, order.CommentProfit);
        Consider(ExitStop(order, targets), upward: !isLong, isLimit: false, order.CommentLoss);
        if (order.TrailActive && order.TrailOffset is { } offset)
            Consider(order.TrailExtreme - (isLong ? 1 : -1) * offset * MinTick, upward: !isLong, isLimit: false, order.CommentTrailing);

        return best;
    }

    /// <summary>
    /// Price at which the move from <paramref name="from"/> to <paramref name="to"/> reaches <paramref name="level"/>
    /// going up (or down); <paramref name="from"/> itself when already beyond it (a gap), null when not reached.
    /// </summary>
    private static double? Cross(double from, double to, double level, bool upward)
    {
        if (upward ? from >= level : from <= level)
            return from;
        if (upward ? to >= level : to <= level)
            return level;
        return null;
    }

    private List<Trade> ExitTargets(Order order)
        => string.IsNullOrEmpty(order.FromEntry)
            ? _openTrades.ToList()
            : _openTrades.Where(t => t.EntryId == order.FromEntry).ToList();

    private static double AveragePrice(IReadOnlyCollection<Trade> trades)
    {
        var quantity = trades.Sum(t => t.Quantity);
        return quantity > 0 ? trades.Sum(t => t.EntryPrice * t.Quantity) / quantity : double.NaN;
    }

    /// <summary>Absolute <c>limit</c>, or <c>profit</c> ticks from the average entry price.</summary>
    private double? ExitLimit(Order order, List<Trade> targets)
        => order.Limit ?? (order.ProfitTicks is { } ticks && targets.Count > 0
            ? AveragePrice(targets) + targets[0].Sign * ticks * MinTick
            : null);

    /// <summary>Absolute <c>stop</c>, or <c>loss</c> ticks from the average entry price.</summary>
    private double? ExitStop(Order order, List<Trade> targets)
        => order.Stop ?? (order.LossTicks is { } ticks && targets.Count > 0
            ? AveragePrice(targets) - targets[0].Sign * ticks * MinTick
            : null);

    /// <summary>Arms trailing stops whose activation level was reached and moves their extreme.</summary>
    private void UpdateTrailing(double price)
    {
        foreach (var order in _orders)
        {
            if (order.Kind != OrderKind.Exit || order.TrailOffset is null)
                continue;

            var targets = ExitTargets(order);
            if (targets.Count == 0)
                continue;

            var sign = targets[0].Sign;
            if (!order.TrailActive)
            {
                var activation = order.TrailPrice
                    ?? (order.TrailPoints is { } points ? AveragePrice(targets) + sign * points * MinTick : (double?)null);
                if (activation is { } level && (price - level) * sign >= 0)
                {
                    order.TrailActive = true;
                    order.TrailExtreme = price;
                }
            }
            else if ((price - order.TrailExtreme) * sign > 0)
            {
                order.TrailExtreme = price;
            }
        }
    }

    private void Track(double price)
    {
        foreach (var trade in _openTrades)
            trade.Track(price);
    }
}

/// <summary>Strategy values as the script saw them on one bar.</summary>
internal sealed record Snapshot(
    double PositionSize,
    double PositionAveragePrice,
    double Equity,
    double NetProfit,
    double OpenProfit,
    int OpenTrades,
    int ClosedTrades,
    int WinTrades,
    int LossTrades,
    double GrossProfit,
    double GrossLoss,
    double MaxDrawdown)
{
    public static Snapshot Of(Broker broker, double price)
        => new(
            broker.PositionSize,
            broker.PositionAveragePrice,
            broker.EquityAt(price),
            broker.NetProfit,
            broker.OpenProfitAt(price),
            broker.OpenTrades.Count,
            broker.ClosedTrades.Count,
            broker.WinTrades,
            broker.LossTrades,
            broker.GrossProfit,
            broker.GrossLoss,
            broker.MaxDrawdown);
}
