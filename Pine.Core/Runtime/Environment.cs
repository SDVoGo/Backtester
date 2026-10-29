namespace Pine.Core.Runtime;

/// <summary>
/// Lexical scope for one execution of a block. Names map to series that live in
/// <see cref="InstanceState"/>, so the scope itself is rebuilt on every bar.
/// </summary>
internal sealed class Environment(Environment? parent, bool isGlobal = false)
{
    private readonly Environment? _parent = parent;
    private Dictionary<string, Series>? _variables;

    public bool IsGlobal { get; } = isGlobal;

    /// <returns>false if the name is already declared in this scope.</returns>
    public bool TryDefine(string name, Series series)
        => (_variables ??= []).TryAdd(name, series);

    public Series? Find(string name, out Environment? owner)
    {
        for (var scope = this; scope is not null; scope = scope._parent)
        {
            if (scope._variables is not null && scope._variables.TryGetValue(name, out var series))
            {
                owner = scope;
                return series;
            }
        }

        owner = null;
        return null;
    }

    public IEnumerable<KeyValuePair<string, Series>> Variables
        => _variables ?? [];
}
