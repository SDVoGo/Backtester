using Pine.Core.Syntax;

namespace Pine.Core.Runtime.Builtins;

/// <summary>State of a run visible to built-ins: the bars and the current position.</summary>
public sealed class BarContext(IReadOnlyList<Bar> bars)
{
    internal readonly List<PlotSeries> PlotList = [];

    public IReadOnlyList<Bar> Bars { get; } = bars;

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

    /// <summary>Whether the argument was passed (positionally or by name).</summary>
    public bool Has(int index) => _provided[index];

    public Value ArgOr(int index, Value fallback)
        => _provided[index] ? _arguments[index] : fallback;

    /// <summary>State owned by this call site, created on first use and kept across bars.</summary>
    public T State<T>() where T : class, new()
        => _state.GetOrCreate<T>(Node);

    public double Number(int index)
    {
        var value = _arguments[index];
        return value.IsNumber ? value.AsDouble : throw Error($"Argument {index + 1} must be a number, got {value.Kind}");
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
public sealed record BuiltinFunction(string Name, string[] Parameters, BuiltinBody Body, bool AcceptsAnyArguments = false);

/// <summary>Built-in variable; <c>offset</c> &gt; 0 is a history reference like <c>close[1]</c>.</summary>
public delegate Value BuiltinVariable(BarContext context, int offset);

public sealed class BuiltinRegistry
{
    private readonly Dictionary<string, BuiltinFunction> _functions = [];
    private readonly Dictionary<string, BuiltinVariable> _variables = [];

    public static BuiltinRegistry CreateDefault()
    {
        var registry = new BuiltinRegistry();
        CoreBuiltins.Register(registry);
        return registry;
    }

    public BuiltinRegistry Function(string name, string[] parameters, BuiltinBody body, bool acceptsAnyArguments = false)
    {
        _functions[name] = new BuiltinFunction(name, parameters, body, acceptsAnyArguments);
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
        => _functions.TryGetValue(name, out function!);

    public bool TryGetVariable(string name, out BuiltinVariable variable)
        => _variables.TryGetValue(name, out variable!);
}
