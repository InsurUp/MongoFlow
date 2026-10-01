namespace MongoFlow;

internal sealed class MigrationModelBuilder<TVault>(VaultModelBuilderBase vault) : IMigrationBuilder<TVault>
    where TVault : MongoVault
{
    private const string DefaultCollectionName = "migrations";

    private readonly List<Type> _types = [];
    private readonly Layered<string> _collectionName = new();

    public IMigrationBuilder<TVault> Add<TMigration>() where TMigration : class, IVaultMigration<TVault>
    {
        AddType(typeof(TMigration));
        return this;
    }

    public IMigrationBuilder<TVault> AddFromAssemblyOf<T>()
    {
        foreach (var type in typeof(T).Assembly.GetTypes())
        {
            if (type is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false } &&
                type.IsAssignableTo(typeof(IVaultMigration<TVault>)))
            {
                AddType(type);
            }
        }

        return this;
    }

    public IMigrationBuilder<TVault> CollectionName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        _collectionName.Set(vault.Layer, name);
        return this;
    }

    /// <summary>The migrations, or <see langword="null"/> when the vault has none.</summary>
    public MigrationModel? Build() =>
        _types.Count == 0
            ? null
            : new MigrationModel(_types, _collectionName.TryGet(out var name) ? name : DefaultCollectionName);

    private void AddType(Type type)
    {
        if (!_types.Contains(type))
        {
            _types.Add(type);
        }
    }
}
