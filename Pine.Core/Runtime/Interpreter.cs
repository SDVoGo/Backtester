using System.Globalization;
using Pine.Core.Runtime.Builtins;
using Pine.Core.Syntax;

namespace Pine.Core.Runtime;

/// <param name="Inputs">Value used for each <c>input.*</c>, keyed by title (default or override).</param>
public sealed record ExecutionResult(
    IReadOnlyList<PlotSeries> Plots,
    IReadOnlyDictionary<string, Value> Globals,
    IReadOnlyDictionary<string, Value> Inputs);

/// <summary>Called around each script execution; the backtest broker fills orders here.</summary>
public interface IBarHooks
{
    /// <summary>Before the script runs on the bar: the bar's open, high and low are already known.</summary>
    void BeforeBar(BarContext context);

    /// <summary>After the script ran on the bar, at its close.</summary>
    void AfterBar(BarContext context);
}

/// <summary>
/// Tree-walking interpreter following Pine's execution model:
/// the script runs once per bar; every variable is a <see cref="Series"/>;
/// <c>var</c> keeps its value across bars; each user-function call site and each
/// stateful built-in call site owns its own <see cref="InstanceState"/>.
/// </summary>
public sealed class Interpreter
{
    private const int MaxLoopIterations = 1_000_000;

    private enum Flow { Normal, Break, Continue }

    private readonly Script _script;
    private readonly BuiltinRegistry _builtins;
    private readonly BarContext _context;
    private readonly InstanceState _rootState = new();
    private readonly Dictionary<string, FunctionDeclaration> _functions = [];
    private readonly HashSet<FunctionDeclaration> _callStack = new(ReferenceEqualityComparer.Instance);

    private Environment _globals = new(null, isGlobal: true);
    private InstanceState _state;
    private Flow _flow;

    private readonly IBarHooks? _hooks;

    private Interpreter(
        Script script, IReadOnlyList<Bar> bars, BuiltinRegistry builtins, IReadOnlyDictionary<string, Value>? inputs, IBarHooks? hooks)
    {
        _script = script;
        _builtins = builtins;
        _hooks = hooks;
        _context = new BarContext(bars, inputs);
        _state = _rootState;
    }

    /// <param name="inputs">Overrides for <c>input.*</c> default values, keyed by input title.</param>
    public static ExecutionResult Run(
        Script script,
        IReadOnlyList<Bar> bars,
        BuiltinRegistry? builtins = null,
        IReadOnlyDictionary<string, Value>? inputs = null,
        IBarHooks? hooks = null)
        => new Interpreter(script, bars, builtins ?? BuiltinRegistry.CreateDefault(), inputs, hooks).Run();

    private int BarIndex => _context.BarIndex;

    private ExecutionResult Run()
    {
        foreach (var function in _script.Statements.OfType<FunctionDeclaration>())
        {
            if (!_functions.TryAdd(function.Name, function))
                throw Error($"Function '{function.Name}' is already declared", function.Pos);
        }

        for (var bar = 0; bar < _context.Bars.Count; bar++)
        {
            _context.BarIndex = bar;
            _globals = new Environment(null, isGlobal: true);
            _state = _rootState;
            _flow = Flow.Normal;
            _hooks?.BeforeBar(_context);

            foreach (var statement in _script.Statements)
            {
                if (statement is not FunctionDeclaration)
                    Execute(statement, _globals);
            }

            _hooks?.AfterBar(_context);
        }

        var globals = _globals.Variables.ToDictionary(v => v.Key, v => v.Value.Current);
        return new ExecutionResult(_context.PlotList, globals, _context.InputValues);
    }

    private PineRuntimeException Error(string message, SourcePos pos)
        => new(message, pos, BarIndex);

    // ── Statements ──────────────────────────────────────────

    private Value Execute(Statement statement, Environment env)
        => statement switch
        {
            VariableDeclaration declaration => Declare(declaration, env),
            TupleDeclaration declaration => DeclareTuple(declaration, env),
            Assignment assignment => Assign(assignment, env),
            ExpressionStatement expression => Evaluate(expression.Expression, env),
            BreakStatement => SetFlow(Flow.Break),
            ContinueStatement => SetFlow(Flow.Continue),
            FunctionDeclaration function => throw Error($"Function '{function.Name}' must be declared in the global scope", function.Pos),
            _ => throw Error($"Unsupported statement {statement.GetType().Name}", statement.Pos),
        };

    private Value SetFlow(Flow flow)
    {
        _flow = flow;
        return Value.Na;
    }

    /// <summary>Runs the statements in order; the value is the last statement's.</summary>
    private Value ExecuteBlock(Block block, Environment env)
    {
        var last = Value.Na;
        foreach (var statement in block.Statements)
        {
            last = Execute(statement, env);
            if (_flow != Flow.Normal)
                break;
        }
        return last;
    }

    private void Define(Environment env, string name, Series series, SourcePos pos)
    {
        if (!env.TryDefine(name, series))
            throw Error($"Variable '{name}' is already declared in this scope", pos);
    }

    private Value Declare(VariableDeclaration declaration, Environment env)
    {
        var series = _state.GetOrCreate<Series>(declaration);

        // var/varip: initialised once, then the previous value carries over
        var value = declaration.Mode != DeclarationMode.Default && series.HasValue
            ? series.Current
            : Coerce(Evaluate(declaration.Value, env), declaration.Type);

        series.Set(value, BarIndex);
        Define(env, declaration.Name, series, declaration.Pos);
        return value;
    }

    private Value DeclareTuple(TupleDeclaration declaration, Environment env)
    {
        var value = Evaluate(declaration.Value, env);
        if (value.Kind != ValueKind.Tuple || value.AsTuple.Length != declaration.Names.Count)
            throw Error($"Expected a tuple of {declaration.Names.Count} values, got '{value}'", declaration.Pos);

        var items = value.AsTuple;
        var series = _state.GetOrCreate(declaration, () => declaration.Names.Select(_ => new Series()).ToArray());

        for (var i = 0; i < items.Length; i++)
        {
            series[i].Set(items[i], BarIndex);
            Define(env, declaration.Names[i], series[i], declaration.Pos);
        }

        return value;
    }

    private Value Assign(Assignment assignment, Environment env)
    {
        if (assignment.Target is not IdentifierExpr target)
            throw Error("Only variables can be assigned", assignment.Pos);

        var series = env.Find(target.Name, out var owner)
            ?? throw Error($"Undeclared variable '{target.Name}'", target.Pos);

        if (owner!.IsGlobal && _callStack.Count > 0)
            throw Error($"Cannot modify global variable '{target.Name}' in a function", target.Pos);

        var value = Evaluate(assignment.Value, env);

        if (assignment.Operator != AssignOp.Reassign)
        {
            var op = assignment.Operator switch
            {
                AssignOp.Add => BinaryOp.Add,
                AssignOp.Subtract => BinaryOp.Subtract,
                AssignOp.Multiply => BinaryOp.Multiply,
                AssignOp.Divide => BinaryOp.Divide,
                _ => BinaryOp.Modulo,
            };
            value = Binary(op, series.Current, value, assignment.Pos);
        }

        series.Set(value, BarIndex);
        return value;
    }

    private static Value Coerce(Value value, TypeRef? type)
        => type is { Name: "float", IsArray: false } && value.Kind == ValueKind.Int
            ? Value.Float(value.AsDouble)
            : value;

    // ── Expressions ─────────────────────────────────────────

    private Value Evaluate(Expr expr, Environment env)
        => expr switch
        {
            IntLiteral literal => Value.Int(literal.Value),
            FloatLiteral literal => Value.Float(literal.Value),
            BoolLiteral literal => Value.Bool(literal.Value),
            StringLiteral literal => Value.String(literal.Value),
            ColorLiteral literal => Value.Color(ParseColor(literal.Hex)),
            IdentifierExpr identifier => Identifier(identifier, env),
            MemberExpr member => Member(member, env),
            HistoryExpr history => History(history, env),
            CallExpr call => Call(call, env),
            TupleExpr tuple => Value.Tuple(tuple.Elements.Select(e => Evaluate(e, env)).ToArray()),
            UnaryExpr unary => Unary(unary, env),
            BinaryExpr binary => Binary(binary, env),
            TernaryExpr ternary => IsTrue(Evaluate(ternary.Condition, env), ternary.Condition.Pos)
                ? Evaluate(ternary.Then, env)
                : Evaluate(ternary.Else, env),
            IfExpr @if => If(@if, env),
            SwitchExpr @switch => Switch(@switch, env),
            ForExpr @for => For(@for, env),
            ForInExpr forIn => ForIn(forIn, env),
            WhileExpr @while => While(@while, env),
            _ => throw Error($"Unsupported expression {expr.GetType().Name}", expr.Pos),
        };

    private static uint ParseColor(string hex)
    {
        var rgba = uint.Parse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return hex.Length == 7 ? (rgba << 8) | 0xFF : rgba;
    }

    private Value Identifier(IdentifierExpr identifier, Environment env)
    {
        if (env.Find(identifier.Name, out _) is { } series)
            return series.Current;

        if (_builtins.TryGetVariable(identifier.Name, out var builtin))
            return builtin(_context, 0);

        throw Error($"Undeclared identifier '{identifier.Name}'", identifier.Pos);
    }

    private Value Member(MemberExpr member, Environment env)
        => BuiltinVariableAt(member, env, 0);

    private Value BuiltinVariableAt(MemberExpr member, Environment env, int offset)
    {
        var name = QualifiedName(member);
        if (name is null || env.Find(RootName(member), out _) is not null)
            throw Error("Field access on objects is not supported yet", member.Pos);

        if (_builtins.TryGetVariable(name, out var builtin))
            return builtin(_context, offset);

        throw Error($"Unknown built-in '{name}'", member.Pos);
    }

    /// <summary><c>ta.sma</c> for a chain of identifiers, null otherwise.</summary>
    private static string? QualifiedName(Expr expr)
        => expr switch
        {
            IdentifierExpr identifier => identifier.Name,
            MemberExpr member => QualifiedName(member.Target) is { } target ? $"{target}.{member.Name}" : null,
            _ => null,
        };

    private static string RootName(Expr expr)
        => expr switch
        {
            MemberExpr member => RootName(member.Target),
            IdentifierExpr identifier => identifier.Name,
            _ => "",
        };

    private Value History(HistoryExpr history, Environment env)
    {
        var offsetValue = Evaluate(history.Offset, env);
        if (!offsetValue.IsNumber || offsetValue.AsDouble < 0)
            throw Error($"History offset must be a non-negative number, got '{offsetValue}'", history.Offset.Pos);

        var offset = (int)offsetValue.AsInt;
        if (offset > Series.MaxBarsBack)
            throw Error($"History offset {offset} exceeds max_bars_back ({Series.MaxBarsBack})", history.Offset.Pos);

        switch (history.Target)
        {
            case IdentifierExpr identifier:
                if (env.Find(identifier.Name, out _) is { } series)
                    return series.Get(offset);
                if (_builtins.TryGetVariable(identifier.Name, out var builtin))
                    return builtin(_context, offset);
                throw Error($"Undeclared identifier '{identifier.Name}'", identifier.Pos);

            case MemberExpr member:
                return BuiltinVariableAt(member, env, offset);

            default:
                // Any other expression: record its value on every evaluation and read back
                var hidden = _state.GetOrCreate<Series>(history);
                hidden.Set(Evaluate(history.Target, env), BarIndex);
                return hidden.Get(offset);
        }
    }

    private Value Unary(UnaryExpr unary, Environment env)
    {
        var operand = Evaluate(unary.Operand, env);

        return unary.Operator switch
        {
            UnaryOp.Not => Value.Bool(!IsTrue(operand, unary.Pos)),
            _ when operand.IsNa => Value.Na,
            UnaryOp.Negate when operand.IsNumber => Value.Number(-operand.AsDouble, operand.Kind == ValueKind.Int),
            UnaryOp.Plus when operand.IsNumber => operand,
            _ => throw Error($"Unary operator cannot be applied to {operand.Kind}", unary.Pos),
        };
    }

    private Value Binary(BinaryExpr binary, Environment env)
    {
        // and/or short-circuit, as in Pine v6
        switch (binary.Operator)
        {
            case BinaryOp.And:
                return Value.Bool(IsTrue(Evaluate(binary.Left, env), binary.Left.Pos)
                    && IsTrue(Evaluate(binary.Right, env), binary.Right.Pos));
            case BinaryOp.Or:
                return Value.Bool(IsTrue(Evaluate(binary.Left, env), binary.Left.Pos)
                    || IsTrue(Evaluate(binary.Right, env), binary.Right.Pos));
        }

        return Binary(binary.Operator, Evaluate(binary.Left, env), Evaluate(binary.Right, env), binary.Pos);
    }

    private Value Binary(BinaryOp op, Value left, Value right, SourcePos pos)
    {
        switch (op)
        {
            case BinaryOp.Equal:
                return Value.Bool(!left.IsNa && !right.IsNa && AreEqual(left, right));
            case BinaryOp.NotEqual:
                return Value.Bool(!left.IsNa && !right.IsNa && !AreEqual(left, right));
            case BinaryOp.Less or BinaryOp.LessEqual or BinaryOp.Greater or BinaryOp.GreaterEqual:
                return Value.Bool(!left.IsNa && !right.IsNa && Compare(op, left, right, pos));
        }

        if (left.IsNa || right.IsNa)
            return Value.Na;

        if (op == BinaryOp.Add && (left.Kind == ValueKind.String || right.Kind == ValueKind.String))
        {
            if (left.Kind != ValueKind.String || right.Kind != ValueKind.String)
                throw Error($"Cannot add {left.Kind} and {right.Kind}, use str.tostring()", pos);
            return Value.String(left.AsString + right.AsString);
        }

        if (!left.IsNumber || !right.IsNumber)
            throw Error($"Arithmetic operator cannot be applied to {left.Kind} and {right.Kind}", pos);

        var a = left.AsDouble;
        var b = right.AsDouble;
        var bothInt = left.Kind == ValueKind.Int && right.Kind == ValueKind.Int;

        return op switch
        {
            BinaryOp.Add => Value.Number(a + b, bothInt),
            BinaryOp.Subtract => Value.Number(a - b, bothInt),
            BinaryOp.Multiply => Value.Number(a * b, bothInt),
            // Division always yields float; by zero yields na
            BinaryOp.Divide => b == 0 ? Value.Na : Value.Float(a / b),
            BinaryOp.Modulo => b == 0 ? Value.Na : Value.Number(a % b, bothInt),
            _ => throw Error($"Unsupported operator {op}", pos),
        };
    }

    private static bool AreEqual(Value left, Value right)
        => left.IsNumber && right.IsNumber
            ? left.AsDouble == right.AsDouble
            : left.Equals(right);

    private bool Compare(BinaryOp op, Value left, Value right, SourcePos pos)
    {
        int order;
        if (left.IsNumber && right.IsNumber)
            order = left.AsDouble.CompareTo(right.AsDouble);
        else if (left.Kind == ValueKind.String && right.Kind == ValueKind.String)
            order = string.CompareOrdinal(left.AsString, right.AsString);
        else
            throw Error($"Cannot compare {left.Kind} and {right.Kind}", pos);

        return op switch
        {
            BinaryOp.Less => order < 0,
            BinaryOp.LessEqual => order <= 0,
            BinaryOp.Greater => order > 0,
            _ => order >= 0,
        };
    }

    /// <summary>Condition value: na counts as false.</summary>
    private bool IsTrue(Value value, SourcePos pos)
        => value.Kind switch
        {
            ValueKind.Bool => value.AsBool,
            ValueKind.Na => false,
            ValueKind.Int or ValueKind.Float => value.AsDouble != 0,
            _ => throw Error($"Expected a bool condition, got {value.Kind}", pos),
        };

    // ── Structures ──────────────────────────────────────────

    private Value If(IfExpr @if, Environment env)
    {
        if (IsTrue(Evaluate(@if.Condition, env), @if.Condition.Pos))
            return ExecuteBlock(@if.Then, new Environment(env));

        return @if.Else is null ? Value.Na : ExecuteBlock(@if.Else, new Environment(env));
    }

    private Value Switch(SwitchExpr @switch, Environment env)
    {
        Value? subject = @switch.Subject is null ? null : Evaluate(@switch.Subject, env);

        foreach (var @case in @switch.Cases)
        {
            var matches = @case.Condition is null
                || (subject is { } value
                    ? AreEqual(value, Evaluate(@case.Condition, env))
                    : IsTrue(Evaluate(@case.Condition, env), @case.Condition.Pos));

            if (matches)
                return ExecuteBlock(@case.Body, new Environment(env));
        }

        return Value.Na;
    }

    private Value For(ForExpr @for, Environment env)
    {
        var from = Evaluate(@for.From, env);
        var to = Evaluate(@for.To, env);
        var step = @for.Step is null ? (Value?)null : Evaluate(@for.Step, env);

        if (!from.IsNumber || !to.IsNumber || step is { IsNumber: false })
            throw Error("Loop bounds must be numbers", @for.Pos);

        var start = from.AsDouble;
        var end = to.AsDouble;
        var increment = step?.AsDouble ?? (start <= end ? 1 : -1);
        if (increment == 0)
            throw Error("Loop step cannot be zero", @for.Pos);

        var isInt = from.Kind == ValueKind.Int && (step is null || step.Value.Kind == ValueKind.Int);
        var counter = _state.GetOrCreate<Series>(@for);
        var last = Value.Na;
        var iterations = 0;

        for (var i = start; increment > 0 ? i <= end : i >= end; i += increment)
        {
            CheckIterations(ref iterations, @for.Pos);

            var scope = new Environment(env);
            counter.Set(Value.Number(i, isInt), BarIndex);
            Define(scope, @for.Variable, counter, @for.Pos);

            last = ExecuteBlock(@for.Body, scope);
            if (EndIteration())
                break;
        }

        return last;
    }

    private Value ForIn(ForInExpr forIn, Environment env)
    {
        var collection = Evaluate(forIn.Collection, env);
        if (collection.IsNa)
            return Value.Na;
        if (collection.Kind != ValueKind.Tuple)
            throw Error($"Cannot iterate over {collection.Kind}", forIn.Collection.Pos);

        var series = _state.GetOrCreate(forIn, () => new[] { new Series(), new Series() });
        var items = collection.AsTuple;
        var last = Value.Na;

        for (var i = 0; i < items.Length; i++)
        {
            var scope = new Environment(env);
            if (forIn.IndexVariable is not null)
            {
                series[0].Set(Value.Int(i), BarIndex);
                Define(scope, forIn.IndexVariable, series[0], forIn.Pos);
            }
            series[1].Set(items[i], BarIndex);
            Define(scope, forIn.ItemVariable, series[1], forIn.Pos);

            last = ExecuteBlock(forIn.Body, scope);
            if (EndIteration())
                break;
        }

        return last;
    }

    private Value While(WhileExpr @while, Environment env)
    {
        var last = Value.Na;
        var iterations = 0;

        while (IsTrue(Evaluate(@while.Condition, env), @while.Condition.Pos))
        {
            CheckIterations(ref iterations, @while.Pos);

            last = ExecuteBlock(@while.Body, new Environment(env));
            if (EndIteration())
                break;
        }

        return last;
    }

    private void CheckIterations(ref int iterations, SourcePos pos)
    {
        if (++iterations > MaxLoopIterations)
            throw Error($"Loop exceeded {MaxLoopIterations} iterations", pos);
    }

    /// <returns>true if the loop must stop (break).</returns>
    private bool EndIteration()
    {
        var flow = _flow;
        _flow = Flow.Normal;
        return flow == Flow.Break;
    }

    // ── Calls ───────────────────────────────────────────────

    private Value Call(CallExpr call, Environment env)
    {
        var name = QualifiedName(call.Callee)
            ?? throw Error("Expression is not callable", call.Pos);

        if (call.Callee is IdentifierExpr && _functions.TryGetValue(name, out var function))
            return CallUser(function, call, env);

        if (_builtins.TryGetFunction(name, out var builtin))
            return CallBuiltin(builtin, call, env);

        throw Error($"Unknown function '{name}'", call.Pos);
    }

    /// <summary>Evaluates arguments in the caller's scope and binds them to parameter positions.</summary>
    private (Value[] Values, bool[] Provided) BindArguments(
        CallExpr call, Environment env, string name, IReadOnlyList<string> parameters, bool acceptsAny, bool variadic = false)
    {
        var slots = variadic ? Math.Max(parameters.Count, call.Arguments.Count(a => a.Name is null)) : parameters.Count;
        var values = new Value[slots];
        var provided = new bool[slots];

        for (var i = 0; i < call.Arguments.Count; i++)
        {
            var argument = call.Arguments[i];
            var index = argument.Name is null ? i : IndexOf(parameters, argument.Name);

            if (index < 0 || index >= slots)
            {
                if (acceptsAny)
                    continue;

                throw Error(argument.Name is null
                    ? $"Too many arguments for '{name}'"
                    : $"'{name}' has no parameter '{argument.Name}'", argument.Pos);
            }

            if (provided[index])
                throw Error($"Argument '{(index < parameters.Count ? parameters[index] : index + 1)}' passed twice", argument.Pos);

            values[index] = Evaluate(argument.Value, env);
            provided[index] = true;
        }

        return (values, provided);
    }

    private static int IndexOf(IReadOnlyList<string> parameters, string name)
    {
        for (var i = 0; i < parameters.Count; i++)
        {
            if (parameters[i] == name)
                return i;
        }
        return -1;
    }

    private Value CallBuiltin(BuiltinFunction builtin, CallExpr call, Environment env)
    {
        var (values, provided) = BindArguments(call, env, builtin.Name, builtin.Parameters, builtin.AcceptsAnyArguments, builtin.Variadic);
        return builtin.Body(new CallContext(_context, call, _state, values, provided));
    }

    private Value CallUser(FunctionDeclaration function, CallExpr call, Environment env)
    {
        if (_callStack.Contains(function))
            throw Error($"Recursive call to '{function.Name}' is not allowed", call.Pos);

        var parameters = function.Parameters.Select(p => p.Name).ToArray();
        var (values, provided) = BindArguments(call, env, function.Name, parameters, acceptsAny: false);

        // Each call site has its own instance: locals, var and nested calls persist per call site
        var instance = _state.GetOrCreate<InstanceState>(call);
        var scope = new Environment(_globals);

        var callerState = _state;
        _state = instance;
        _callStack.Add(function);

        try
        {
            for (var i = 0; i < function.Parameters.Count; i++)
            {
                var parameter = function.Parameters[i];
                var value = provided[i] ? values[i]
                    : parameter.Default is not null ? Evaluate(parameter.Default, scope)
                    : throw Error($"Missing argument '{parameter.Name}' for '{function.Name}'", call.Pos);

                var series = instance.GetOrCreate<Series>(parameter);
                series.Set(Coerce(value, parameter.Type), BarIndex);
                Define(scope, parameter.Name, series, parameter.Pos);
            }

            var result = ExecuteBlock(function.Body, scope);
            _flow = Flow.Normal;
            return result;
        }
        finally
        {
            _state = callerState;
            _callStack.Remove(function);
        }
    }
}
