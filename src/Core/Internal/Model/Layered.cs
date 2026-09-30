namespace MongoFlow;

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
