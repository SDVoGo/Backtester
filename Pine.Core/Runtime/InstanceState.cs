namespace Pine.Core.Runtime;

/// <summary>
/// State that persists across bars for one instance of a scope: the script itself,
/// or one call site of a user function. Slots are keyed by AST node identity, so each
/// declaration, history expression and call site (built-in or user) owns its own state.
/// </summary>
public sealed class InstanceState
{
    private readonly Dictionary<object, object> _slots = new(ReferenceEqualityComparer.Instance);

    public T GetOrCreate<T>(object key) where T : class, new()
        => GetOrCreate(key, static () => new T());

    public T GetOrCreate<T>(object key, Func<T> factory) where T : class
    {
        if (_slots.TryGetValue(key, out var slot))
            return (T)slot;

        var created = factory();
        _slots[key] = created;
        return created;
    }
}
