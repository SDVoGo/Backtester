using Pine.Core.Runtime;
using Pine.Core.Runtime.Builtins;

namespace Backtester.Engine;

/// <summary><c>strategy()</c> and <c>strategy.*</c>, bound to one <see cref="Broker"/> for one run.</summary>
internal static class StrategyBuiltins
{
    // Positional order of strategy(); later parameters are only accepted by name and ignored
    private static readonly string[] StrategyParameters =
    [
        "title", "shorttitle", "overlay", "format", "precision", "scale", "pyramiding",
        "calc_on_order_fills", "calc_on_every_tick", "max_bars_back", "backtest_fill_limits_assumption",
        "default_qty_type", "default_qty_value", "initial_capital", "currency", "slippage",
        "commission_type", "commission_value", "process_orders_on_close",
    ];

    private static readonly string[] EntryParameters =
        ["id", "direction", "qty", "limit", "stop", "oca_name", "oca_type", "comment", "alert_message", "disable_alert"];

    private static readonly string[] CloseParameters =
        ["id", "comment", "qty", "qty_percent", "alert_message", "immediately", "disable_alert"];

    private static readonly string[] CloseAllParameters =
        ["comment", "alert_message", "immediately", "disable_alert"];

    private static readonly string[] ExitParameters =
    [
        "id", "from_entry", "qty", "qty_percent", "profit", "limit", "loss", "stop",
        "trail_price", "trail_points", "trail_offset", "oca_name", "comment",
        "comment_profit", "comment_loss", "comment_trailing",
        "alert_message", "alert_profit", "alert_loss", "alert_trailing", "disable_alert",
    ];

    public static void Register(BuiltinRegistry registry, Broker broker)
    {
        registry
            .Constant("strategy.long", Value.String("long"))
            .Constant("strategy.short", Value.String("short"))
            .Constant("strategy.fixed", Value.String("fixed"))
            .Constant("strategy.cash", Value.String("cash"))
            .Constant("strategy.percent_of_equity", Value.String("percent_of_equity"))
            .Constant("strategy.commission.percent", Value.String("percent"))
            .Constant("strategy.commission.cash_per_contract", Value.String("cash_per_contract"))
            .Constant("strategy.commission.cash_per_order", Value.String("cash_per_order"))
            .Constant("syminfo.mintick", Value.Float(broker.MinTick))
            .NoOpNamespace("currency")
            .Function("strategy", StrategyParameters, call => Configure(call, broker), acceptsAnyArguments: true)
            .Function("strategy.entry", EntryParameters, call => Enter(call, broker, OrderKind.Entry), acceptsAnyArguments: true)
            .Function("strategy.order", EntryParameters, call => Enter(call, broker, OrderKind.Order), acceptsAnyArguments: true)
            .Function("strategy.close", CloseParameters, call => Close(call, broker), acceptsAnyArguments: true)
            .Function("strategy.close_all", CloseAllParameters, call => CloseAll(call, broker), acceptsAnyArguments: true)
            .Function("strategy.exit", ExitParameters, call => Exit(call, broker), acceptsAnyArguments: true)
            .Function("strategy.cancel", ["id"], call => Done(() => broker.Cancel(call.String(0))))
            .Function("strategy.cancel_all", [], _ => Done(() => broker.Cancel(null)));

        RegisterVariables(registry, broker);
        RegisterTradeFunctions(registry, broker);
    }

    private static Value Done(Action action)
    {
        action();
        return Value.Na;
    }

    private static double? OptionalNumber(CallContext call, int index)
        => call.Has(index) && call[index].IsNumber ? call[index].AsDouble : null;

    private static string? OptionalString(CallContext call, int index)
        => call.Has(index) && call[index].Kind == ValueKind.String ? call[index].AsString : null;

    private static Value Configure(CallContext call, Broker broker)
    {
        var defaults = new StrategySettings();
        var settings = new StrategySettings
        {
            Title = OptionalString(call, 0) ?? defaults.Title,
            Pyramiding = (int)(OptionalNumber(call, 6) ?? defaults.Pyramiding),
            DefaultQuantityType = OptionalString(call, 11) switch
            {
                null or "fixed" => QuantityType.Fixed,
                "cash" => QuantityType.Cash,
                "percent_of_equity" => QuantityType.PercentOfEquity,
                var other => throw call.Error($"Unknown default_qty_type '{other}'"),
            },
            DefaultQuantityValue = OptionalNumber(call, 12) ?? defaults.DefaultQuantityValue,
            InitialCapital = OptionalNumber(call, 13) ?? defaults.InitialCapital,
            Slippage = (int)(OptionalNumber(call, 15) ?? defaults.Slippage),
            CommissionType = OptionalString(call, 16) switch
            {
                null or "percent" => CommissionType.Percent,
                "cash_per_contract" => CommissionType.CashPerContract,
                "cash_per_order" => CommissionType.CashPerOrder,
                var other => throw call.Error($"Unknown commission_type '{other}'"),
            },
            CommissionValue = OptionalNumber(call, 17) ?? defaults.CommissionValue,
            ProcessOrdersOnClose = call.Bool(18),
        };

        // pyramiding = 0 means "no pyramiding" in Pine, like 1
        broker.Configure(settings with { Pyramiding = Math.Max(1, settings.Pyramiding) });
        return Value.Na;
    }

    private static Direction ParseDirection(CallContext call, int index)
        => OptionalString(call, index) switch
        {
            "long" => Direction.Long,
            "short" => Direction.Short,
            _ => throw call.Error("Direction must be strategy.long or strategy.short"),
        };

    private static Value Enter(CallContext call, Broker broker, OrderKind kind)
    {
        broker.Enter(kind, call.String(0), ParseDirection(call, 1),
            quantity: OptionalNumber(call, 2),
            limit: OptionalNumber(call, 3),
            stop: OptionalNumber(call, 4),
            comment: OptionalString(call, 7));
        return Value.Na;
    }

    private static Value Close(CallContext call, Broker broker)
    {
        broker.Close(call.String(0), OptionalNumber(call, 2), OptionalNumber(call, 3), OptionalString(call, 1), call.Bool(5));
        return Value.Na;
    }

    private static Value CloseAll(CallContext call, Broker broker)
    {
        broker.Close(null, null, null, OptionalString(call, 0), call.Bool(2));
        return Value.Na;
    }

    private static Value Exit(CallContext call, Broker broker)
    {
        broker.Exit(call.String(0), OptionalString(call, 1), order =>
        {
            order.Quantity = OptionalNumber(call, 2);
            order.QuantityPercent = OptionalNumber(call, 3);
            order.ProfitTicks = OptionalNumber(call, 4);
            order.Limit = OptionalNumber(call, 5);
            order.LossTicks = OptionalNumber(call, 6);
            order.Stop = OptionalNumber(call, 7);
            order.TrailPrice = OptionalNumber(call, 8);
            order.TrailPoints = OptionalNumber(call, 9);
            order.TrailOffset = OptionalNumber(call, 10);
            order.Comment = OptionalString(call, 12);
            order.CommentProfit = OptionalString(call, 13);
            order.CommentLoss = OptionalString(call, 14);
            order.CommentTrailing = OptionalString(call, 15);
        });
        return Value.Na;
    }

    private static void RegisterVariables(BuiltinRegistry registry, Broker broker)
    {
        // offset 0 is live (it sees fills made by this bar's script with immediately = true),
        // history comes from the snapshot taken before each bar's script
        BuiltinVariable Number(Func<Broker, BarContext, double> live, Func<Snapshot, double> past)
            => (context, offset) => Value.Float(offset == 0
                ? live(broker, context)
                : broker.SnapshotAt(offset) is { } snapshot ? past(snapshot) : double.NaN);

        BuiltinVariable Count(Func<Broker, int> live, Func<Snapshot, int> past)
            => (_, offset) => offset == 0
                ? Value.Int(live(broker))
                : broker.SnapshotAt(offset) is { } snapshot ? Value.Int(past(snapshot)) : Value.Na;

        registry
            .Variable("strategy.position_size", Number((b, _) => b.PositionSize, s => s.PositionSize))
            .Variable("strategy.position_avg_price", Number((b, _) => b.PositionAveragePrice, s => s.PositionAveragePrice))
            .Variable("strategy.equity", Number((b, c) => b.EquityAt(c.Bar.Close), s => s.Equity))
            .Variable("strategy.openprofit", Number((b, c) => b.OpenProfitAt(c.Bar.Close), s => s.OpenProfit))
            .Variable("strategy.netprofit", Number((b, _) => b.NetProfit, s => s.NetProfit))
            .Variable("strategy.grossprofit", Number((b, _) => b.GrossProfit, s => s.GrossProfit))
            .Variable("strategy.grossloss", Number((b, _) => b.GrossLoss, s => s.GrossLoss))
            .Variable("strategy.max_drawdown", Number((b, _) => b.MaxDrawdown, s => s.MaxDrawdown))
            .Variable("strategy.initial_capital", (_, _) => Value.Float(broker.Settings.InitialCapital))
            .Variable("strategy.opentrades", Count(b => b.OpenTrades.Count, s => s.OpenTrades))
            .Variable("strategy.closedtrades", Count(b => b.ClosedTrades.Count, s => s.ClosedTrades))
            .Variable("strategy.wintrades", Count(b => b.WinTrades, s => s.WinTrades))
            .Variable("strategy.losstrades", Count(b => b.LossTrades, s => s.LossTrades))
            .Variable("strategy.eventrades", Count(
                b => b.ClosedTrades.Count - b.WinTrades - b.LossTrades,
                s => s.ClosedTrades - s.WinTrades - s.LossTrades))
            .Variable("strategy.position_entry_name", (_, _) => broker.OpenTrades.Count > 0
                ? Value.String(broker.OpenTrades[^1].EntryId)
                : Value.Na);
    }

    /// <summary><c>strategy.opentrades.*(n)</c> and <c>strategy.closedtrades.*(n)</c>; n counts from 0 (oldest).</summary>
    private static void RegisterTradeFunctions(BuiltinRegistry registry, Broker broker)
    {
        void Open(string name, Func<Trade, Value> selector)
            => registry.Function($"strategy.opentrades.{name}", ["trade_num"], call
                => (int)call.Number(0) is var n && n >= 0 && n < broker.OpenTrades.Count ? selector(broker.OpenTrades[n]) : Value.Na);

        void Closed(string name, Func<ClosedTrade, Value> selector)
            => registry.Function($"strategy.closedtrades.{name}", ["trade_num"], call
                => (int)call.Number(0) is var n && n >= 0 && n < broker.ClosedTrades.Count ? selector(broker.ClosedTrades[n]) : Value.Na);

        Open("entry_price", t => Value.Float(t.EntryPrice));
        Open("entry_bar_index", t => Value.Int(t.EntryBar));
        Open("entry_time", t => Value.Int(t.EntryTime));
        Open("entry_id", t => Value.String(t.EntryId));
        Open("size", t => Value.Float(t.Quantity * t.Sign));

        Closed("entry_price", t => Value.Float(t.EntryPrice));
        Closed("exit_price", t => Value.Float(t.ExitPrice));
        Closed("entry_bar_index", t => Value.Int(t.EntryBar));
        Closed("exit_bar_index", t => Value.Int(t.ExitBar));
        Closed("entry_id", t => Value.String(t.EntryId));
        Closed("exit_id", t => Value.String(t.ExitId));
        Closed("profit", t => Value.Float(t.Profit));
        Closed("commission", t => Value.Float(t.Commission));
        Closed("size", t => Value.Float(t.Quantity * (int)t.Direction));
    }
}
