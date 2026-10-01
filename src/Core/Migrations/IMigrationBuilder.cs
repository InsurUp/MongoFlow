namespace MongoFlow;

/// <summary>Declares a vault's migrations, applied by <see cref="IVaultMigrator"/>.</summary>
public interface IMigrationBuilder<TVault> where TVault : MongoVault
{
    IMigrationBuilder<TVault> Add<TMigration>() where TMigration : class, IVaultMigration<TVault>;

    /// <summary>
    /// Adds every concrete <see cref="IVaultMigration{TVault}"/> in the assembly that declares <typeparamref name="T"/>.
    /// Migrations written for other vaults are left out.
    /// </summary>
    IMigrationBuilder<TVault> AddFromAssemblyOf<T>();

    /// <summary>
    /// The collection applied migrations are recorded in, in the vault's database. Defaults to <c>migrations</c>, with the
    /// documents earlier MongoFlow versions wrote, so their history carries over. Vaults sharing a database need one each.
    /// </summary>
    IMigrationBuilder<TVault> CollectionName(string name);
}
