namespace MongoFlow;

public interface IMigrationBuilder<TVault> where TVault : MongoVault
{
    IMigrationBuilder<TVault> Add<TMigration>() where TMigration : class, IVaultMigration<TVault>;

    /// <summary>
    /// Adds every non-abstract <see cref="IVaultMigration{TVault}"/> in the assembly that declares
    /// <typeparamref name="T"/>. Migrations written for other vaults are ignored.
    /// </summary>
    IMigrationBuilder<TVault> AddFromAssemblyOf<T>();

    /// <summary>
    /// Where applied migrations are recorded. Defaults to <c>migrations</c>, the collection and document format earlier
    /// MongoFlow versions used, so existing history carries over.
    /// </summary>
    IMigrationBuilder<TVault> CollectionName(string name);
}
