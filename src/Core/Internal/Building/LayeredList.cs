namespace MongoFlow;

internal sealed class LayeredList<T>
{
    private readonly List<T> _default = [];
    private readonly List<T> _own = [];

    public void Add(Layer layer, T item) => (layer == Layer.Own ? _own : _default).Add(item);

    public IEnumerable<T> All => _default.Concat(_own);
}
