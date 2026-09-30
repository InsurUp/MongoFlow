using System.Reflection;

namespace MongoFlow;

internal sealed class MigrationModelBuilder<TVault>(VaultModelBuilderBase vault)
    : IMigrationBuilder<TVault> where TVault : MongoVault
{
    private readonly List<Type> _types = [];
    private readonly Layered<string> _collectionName = new();

    public IMigrationBuilder<TVault> Add<TMigration>() where TMigration : class, IVaultMigration<TVault>
    {
        AddType(typeof(TMigration));
        return this;
    }

    public IMigrationBuilder<TVault> AddFromAssemblyOf<T>()
    {
        Type?[] types;
        try
        {
            types = typeof(T).Assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            types = exception.Types;
        }

        foreach (var type in types)
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

    public MigrationModel? Build() =>
        _types.Count == 0
            ? null
            : new MigrationModel(_types, _collectionName.TryGet(out var name) ? name : "migrations");

    private void AddType(Type type)
    {
        if (!_types.Contains(type))
        {
            _types.Add(type);
        }
    }
}
