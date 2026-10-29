namespace Pine.Core.Runtime;

/// <summary>
/// History of a variable: one slot per bar on which it was written.
/// Writing twice on the same bar (reassignments, loop iterations) overwrites the slot,
/// so <c>x[1]</c> is the value on the previous bar where the declaring scope ran.
/// Keeps at most <see cref="MaxBarsBack"/> slots in a ring buffer.
/// </summary>
public sealed class Series
{
    public const int MaxBarsBack = 5000;

    private Value[] _buffer = new Value[4];
    private int _head = -1;     // index of the most recent slot
    private int _count;
    private int _lastBar = -1;

    public bool HasValue => _count > 0;

    public Value Current => _count > 0 ? _buffer[_head] : Value.Na;

    public void Set(Value value, int barIndex)
    {
        if (barIndex != _lastBar || _count == 0)
        {
            Push();
            _lastBar = barIndex;
        }

        _buffer[_head] = value;
    }

    /// <summary>Value <paramref name="offset"/> slots back; na when not available.</summary>
    public Value Get(int offset)
    {
        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset), "History offset cannot be negative");
        if (offset > MaxBarsBack)
            throw new ArgumentOutOfRangeException(nameof(offset), $"History offset exceeds max_bars_back ({MaxBarsBack})");

        if (offset >= _count)
            return Value.Na;

        var index = (_head - offset) % _buffer.Length;
        return _buffer[index < 0 ? index + _buffer.Length : index];
    }

    private void Push()
    {
        if (_count == _buffer.Length && _buffer.Length <= MaxBarsBack)
            Grow();

        _head = (_head + 1) % _buffer.Length;
        if (_count < _buffer.Length)
            _count++;
    }

    private void Grow()
    {
        var size = Math.Min(_buffer.Length * 2, MaxBarsBack + 1);
        if (size == _buffer.Length)
            return;

        // Unroll the ring so the oldest slot is at 0
        var grown = new Value[size];
        for (var i = 0; i < _count; i++)
            grown[i] = Get(_count - 1 - i);

        _buffer = grown;
        _head = _count - 1;
    }
}
