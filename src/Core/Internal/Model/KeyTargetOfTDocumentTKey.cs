using System.Linq.Expressions;

namespace MongoFlow;

internal sealed class KeyTarget<TDocument, TKey>(KeyModel<TDocument, TKey> model, TKey key) : KeyTarget<TDocument>
{
    public override object Key => key!;

    public override Expression<Func<TDocument, bool>> Filter() => model.Filter(key);
}
