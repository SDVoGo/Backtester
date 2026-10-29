using Pine.Core.Syntax;

namespace Pine.Core.Runtime.Builtins;

/// <summary>State of a run visible to built-ins: the bars and the current position.</summary>
public sealed class BarContext(IReadOnlyList<Bar> bars, IReadOnlyDictionary<string, Value>? inputOverrides = null)
{
    internal readonly List<PlotSeries> PlotList = [];
    internal readonly Dictionary<string, Value> InputValues = [];

    public IReadOnlyList<Bar> Bars { get; } = bars;

    /// <summary>Values that replace the <c>defval</c> of <c>input.*</c> calls, keyed by input title.</summary>
    public IReadOnlyDictionary<string, Value> InputOverrides { get; } = inputOverrides ?? new Dictionary<string, Value>();

    public int BarIndex { get; internal set; }

    public Bar Bar => Bars[BarIndex];

    public bool IsFirstBar => BarIndex == 0;

    public bool IsLastBar => BarIndex == Bars.Count - 1;

    /// <summary>Bar <paramref name="offset"/> bars back, or null before the first bar.</summary>
    public Bar? BarAt(int offset)
        => BarIndex - offset is var index && index >= 0 && index < Bars.Count ? Bars[index] : null;
}

/// <summary>Values recorded by one <c>plot()</c> call site; null where the value was <c>na</c>.</summary>
public sealed record PlotSeries(string Title, double?[] Values);

/// <summary>Arguments and per-call-site state for one invocation of a built-in function.</summary>
public sealed class CallContext
{
    private readonly Value[] _arguments;
    private readonly bool[] _provided;
    private readonly InstanceState _state;

    internal CallContext(BarContext execution, CallExpr node, InstanceState state, Value[] arguments, bool[] provided)
    {
        Execution = execution;
        Node = node;
        _state = state;
        _arguments = arguments;
        _provided = provided;
    }

    public BarContext Execution { get; }

    public CallExpr Node { get; }

    public int BarIndex => Execution.BarIndex;

    public Value this[int index] => _arguments[index];

    /// <summary>Argument slots: the declared parameters, plus the extra positional arguments of variadic functions.</summary>
    public int Count => _arguments.Length;

    /// <summary>Whether the argument was passed (positionally or by name).</summary>
    public bool Has(int index) => index < _provided.Length && _provided[index];

    public Value ArgOr(int index, Value fallback)
        => Has(index) ? _arguments[index] : fallback;

    /// <summary>State owned by this call site, created on first use and kept across bars.</summary>
    public T State<T>() where T : class, new()
        => _state.GetOrCreate<T>(Node);

    public double Number(int index)
    {
        var value = _arguments[index];
        return value.IsNumber ? value.AsDouble : throw Error($"Argument {index + 1} must be a number, got {value.Kind}");
    }

    /// <summary>The number, or NaN when the argument is <c>na</c> or missing. NaN is how indicators carry <c>na</c>.</summary>
    public double NumberOrNaN(int index)
        => !Has(index) || _arguments[index].IsNa ? double.NaN : Number(index);

    public bool Bool(int index, bool fallback = false)
    {
        if (!Has(index))
            return fallback;

        var value = _arguments[index];
        return value.Kind switch
        {
            ValueKind.Bool => value.AsBool,
            ValueKind.Na => false,
            ValueKind.Int or ValueKind.Float => value.AsDouble != 0,
            _ => throw Error($"Argument {index + 1} must be a bool, got {value.Kind}"),
        };
    }

    public string String(int index)
    {
        var value = _arguments[index];
        return value.Kind == ValueKind.String ? value.AsString : throw Error($"Argument {index + 1} must be a string, got {value.Kind}");
    }

    public int Length(int index)
    {
        var value = _arguments[index];
        if (!value.IsNumber || value.AsDouble < 1)
            throw Error($"Argument {index + 1} must be a positive length, got '{value}'");
        return (int)value.AsInt;
    }

    public PineRuntimeException Error(string message)
        => new(message, Node.Pos, Execution.BarIndex);
}

public delegate Value BuiltinBody(CallContext call);

/// <param name="Parameters">Parameter names, used to bind named arguments.</param>
/// <param name="AcceptsAnyArguments">Ignore extra positional and unknown named arguments (drawing functions).</param>
/// <param name="Variadic">Extra positional arguments are kept after the declared parameters (<c>math.max(a, b, c)</c>).</param>
public sealed record BuiltinFunction(string Name, string[] Parameters, BuiltinBody Body, bool AcceptsAnyArguments = false, bool Variadic = false);

/// <summary>Built-in variable; <c>offset</c> &gt; 0 is a history reference like <c>close[1]</c>.</summary>
public delegate Value BuiltinVariable(BarContext context, int offset);

public sealed class BuiltinRegistry
{
    private readonly Dictionary<string, BuiltinFunction> _functions = [];
    private readonly Dictionary<string, BuiltinVariable> _variables = [];
    private readonly HashSet<string> _noOpNamespaces = [];

    public static BuiltinRegistry CreateDefault()
    {
        var registry = new BuiltinRegistry();
        CoreBuiltins.Register(registry);
        InputBuiltins.Register(registry);
        MathBuiltins.Register(registry);
        StringBuiltins.Register(registry);
        ColorBuiltins.Register(registry);
        TaBuiltins.Register(registry);
        DrawingBuiltins.Register(registry);
        return registry;
    }

    public BuiltinRegistry Function(string name, string[] parameters, BuiltinBody body, bool acceptsAnyArguments = false, bool variadic = false)
    {
        _functions[name] = new BuiltinFunction(name, parameters, body, acceptsAnyArguments, variadic);
        return this;
    }

    /// <summary>Function that accepts any arguments and returns <c>na</c> (drawing, alerts).</summary>
    public BuiltinRegistry NoOp(string name)
        => Function(name, [], _ => Value.Na, acceptsAnyArguments: true);

    /// <summary>
    /// Every <c>ns.*</c> function not registered explicitly is a no-op, and every <c>ns.*</c>
    /// variable is a string constant holding its own name (<c>line.style_dotted</c>).
    /// </summary>
    public BuiltinRegistry NoOpNamespace(string ns)
    {
        _noOpNamespaces.Add(ns);
        return this;
    }

    public BuiltinRegistry Variable(string name, BuiltinVariable getter)
    {
        _variables[name] = getter;
        return this;
    }

    public BuiltinRegistry Constant(string name, Value value)
        => Variable(name, (_, _) => value);

    public bool TryGetFunction(string name, out BuiltinFunction function)
    {
        if (_functions.TryGetValue(name, out function!))
            return true;

        if (!IsInNoOpNamespace(name))
            return false;

        // Not cached: lookups never mutate the registry, so it can be shared between runs
        function = new BuiltinFunction(name, [], _ => Value.Na, AcceptsAnyArguments: true);
        return true;
    }

    public bool TryGetVariable(string name, out BuiltinVariable variable)
    {
        if (_variables.TryGetValue(name, out variable!))
            return true;

        if (!IsInNoOpNamespace(name))
            return false;

        var constant = Value.String(name);
        variable = (_, _) => constant;
        return true;
    }

    private bool IsInNoOpNamespace(string name)
        => name.LastIndexOf('.') is var dot && dot > 0 && _noOpNamespaces.Contains(name[..dot]);
}
