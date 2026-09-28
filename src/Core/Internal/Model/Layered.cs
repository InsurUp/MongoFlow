namespace MongoFlow;

/// <summary>
/// Where a setting came from. The vault's own configuration runs first so its skips are known before defaults apply,
/// but defaults still count as coming first: an own value wins, and default list items come before own ones.
/// </summary>
internal enum Layer
{
    Default,
    Own
}

internal sealed class Layered<T>
{
    private (bool IsSet, T Value) _default;
    private (bool IsSet, T Value) _own;

    public void Set(Layer layer, T value)
    {
        if (layer == Layer.Own)
        {
            _own = (true, value);
        }
        else
        {
            _default = (true, value);
        }
    }

    public bool TryGet(out T value)
    {
        (var isSet, value) = _own.IsSet ? _own : _default;
        return isSet;
    }
}

internal sealed class LayeredList<T>
{
    private readonly List<T> _default = [];
    private readonly List<T> _own = [];

    public void Add(Layer layer, T item) => (layer == Layer.Own ? _own : _default).Add(item);

    public IEnumerable<T> All => _default.Concat(_own);
}
