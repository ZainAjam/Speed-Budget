namespace SpeedBudget.Data;

/// <summary>
/// Hybrid logical clock timestamp. The string form is fixed-width so ordinal string comparison
/// matches time order, which lets SQLite sort and filter on it directly.
/// </summary>
public readonly record struct Hlc(long Millis, int Counter, string Node)
{
    public override string ToString() => $"{Millis:D15}-{Counter:D5}-{Node}";

    public static Hlc Parse(string s)
    {
        var parts = s.Split('-', 3);
        return new Hlc(long.Parse(parts[0]), int.Parse(parts[1]), parts[2]);
    }
}

/// <summary>Issues strictly increasing timestamps and absorbs timestamps seen from other devices.</summary>
public sealed class HlcClock
{
    private readonly object _gate = new();
    private readonly Func<long> _now;
    private long _millis;
    private int _counter;

    public string Node { get; }

    public HlcClock(string node, Func<long>? now = null)
    {
        Node = node;
        _now = now ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    public Hlc Next()
    {
        lock (_gate)
        {
            var wall = _now();
            if (wall > _millis) { _millis = wall; _counter = 0; }
            else _counter++;
            return new Hlc(_millis, _counter, Node);
        }
    }

    /// <summary>Call for every timestamp pulled from the server so later local edits sort after it.</summary>
    public void Receive(Hlc remote)
    {
        lock (_gate)
        {
            var wall = _now();
            var max = Math.Max(Math.Max(wall, _millis), remote.Millis);
            if (max == _millis && max == remote.Millis) _counter = Math.Max(_counter, remote.Counter);
            else if (max == remote.Millis) _counter = remote.Counter;
            else if (max != _millis) _counter = 0;
            _millis = max;
        }
    }
}
