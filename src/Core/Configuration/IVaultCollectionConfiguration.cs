namespace MongoFlow;

/// <summary>
/// Configures collections whose document type is only known at runtime, such as every collection whose document
/// carries an attribute. Apply it with <see cref="IVaultBuilder{TVault}.ForEachCollection"/>.
/// </summary>
/// <remarks>
/// <see cref="Configure{TDocument}"/> is called once per collection with a builder typed to that collection's document,
/// so everything it adds is checked by the compiler. Return early for collections it should not touch.
/// </remarks>
public interface IVaultCollectionConfiguration
{
    void Configure<TDocument>(IVaultCollectionBuilder<TDocument> collection);
}
