namespace Pine.Core.Runtime.Builtins;

/// <summary><c>math.*</c>. Any na argument gives na; int-preserving functions keep int results for int arguments.</summary>
internal static class MathBuiltins
{
    public static void Register(BuiltinRegistry registry)
    {
        registry
            .Constant("math.pi", Value.Float(Math.PI))
            .Constant("math.e", Value.Float(Math.E))
            .Constant("math.phi", Value.Float((1 + Math.Sqrt(5)) / 2))
            .Constant("math.rphi", Value.Float((Math.Sqrt(5) - 1) / 2))
            .Function("math.abs", ["number"], call => Unary(call, Math.Abs, keepInt: true))
            .Function("math.sign", ["number"], call => Unary(call, x => Math.Sign(x)))
            .Function("math.ceil", ["number"], call => ToInt(call, Math.Ceiling))
            .Function("math.floor", ["number"], call => ToInt(call, Math.Floor))
            .Function("math.round", ["number", "precision"], Round)
            .Function("math.sqrt", ["number"], call => Unary(call, Math.Sqrt))
            .Function("math.exp", ["number"], call => Unary(call, Math.Exp))
            .Function("math.log", ["number"], call => Unary(call, Math.Log))
            .Function("math.log10", ["number"], call => Unary(call, Math.Log10))
            .Function("math.sin", ["angle"], call => Unary(call, Math.Sin))
            .Function("math.cos", ["angle"], call => Unary(call, Math.Cos))
            .Function("math.tan", ["angle"], call => Unary(call, Math.Tan))
            .Function("math.asin", ["angle"], call => Unary(call, Math.Asin))
            .Function("math.acos", ["angle"], call => Unary(call, Math.Acos))
            .Function("math.atan", ["angle"], call => Unary(call, Math.Atan))
            .Function("math.todegrees", ["radians"], call => Unary(call, x => x * 180 / Math.PI))
            .Function("math.toradians", ["degrees"], call => Unary(call, x => x * Math.PI / 180))
            .Function("math.pow", ["base", "exponent"], Pow)
            .Function("math.max", ["number0", "number1"], call => Aggregate(call, Math.Max), variadic: true)
            .Function("math.min", ["number0", "number1"], call => Aggregate(call, Math.Min), variadic: true)
            .Function("math.avg", ["number0", "number1"], Avg, variadic: true)
            .Function("math.sum", ["source", "length"], Sum);
    }

    private static Value Unary(CallContext call, Func<double, double> function, bool keepInt = false)
    {
        var value = call[0];
        if (value.IsNa)
            return Value.Na;

        var result = function(call.Number(0));
        return keepInt && value.Kind == ValueKind.Int ? Value.Int((long)result) : Value.Float(result);
    }

    private static Value ToInt(CallContext call, Func<double, double> function)
        => call[0].IsNa ? Value.Na : Value.Int((long)function(call.Number(0)));

    /// <summary>Halves round away from zero, as in Pine; with a precision the result is float.</summary>
    private static Value Round(CallContext call)
    {
        if (call[0].IsNa)
            return Value.Na;

        var number = call.Number(0);
        if (!call.Has(1))
            return Value.Int((long)Math.Round(number, MidpointRounding.AwayFromZero));

        if (call[1].IsNa)
            return Value.Na;

        var precision = (int)Math.Clamp(call.Number(1), 0, 15);
        return Value.Float(Math.Round(number, precision, MidpointRounding.AwayFromZero));
    }

    private static Value Pow(CallContext call)
    {
        if (call[0].IsNa || call[1].IsNa)
            return Value.Na;

        var result = Math.Pow(call.Number(0), call.Number(1));
        var isInt = call[0].Kind == ValueKind.Int && call[1].Kind == ValueKind.Int && call[1].AsDouble >= 0;
        return Value.Number(result, isInt && Math.Abs(result) < 9e15);
    }

    private static Value Aggregate(CallContext call, Func<double, double, double> combine)
    {
        if (call.Count < 2 || !call.Has(1))
            throw call.Error("Expected at least two arguments");

        var allInt = true;
        var result = double.NaN;
        for (var i = 0; i < call.Count; i++)
        {
            var value = call[i];
            if (value.IsNa)
                return Value.Na;

            var number = call.Number(i);
            allInt &= value.Kind == ValueKind.Int;
            result = i == 0 ? number : combine(result, number);
        }

        return Value.Number(result, allInt);
    }

    private static Value Avg(CallContext call)
    {
        if (call.Count < 2 || !call.Has(1))
            throw call.Error("Expected at least two arguments");

        var sum = 0.0;
        for (var i = 0; i < call.Count; i++)
        {
            if (call[i].IsNa)
                return Value.Na;
            sum += call.Number(i);
        }

        return Value.Float(sum / call.Count);
    }

    /// <summary>Sliding sum of the last <c>length</c> values; na until enough values or if any is na.</summary>
    private static Value Sum(CallContext call)
    {
        var length = call.Length(1);
        var history = call.State<History>();
        history.Push(call.NumberOrNaN(0), length);

        return Value.Float(history.Count < length ? double.NaN : history.Sum(length));
    }
}
