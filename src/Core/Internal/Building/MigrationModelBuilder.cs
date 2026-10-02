using System.Reflection;
using Semver;

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
    /// <exception cref="VaultConfigurationException">
    /// The vault's <see cref="MongoVersionAttribute"/> isn't a semantic version, or the vault has no migrations to reach it.
    /// </exception>
    public MigrationModel? Build()
    {
        var target = Target();
        if (_types.Count == 0)
        {
            return target is null
                ? null
                : throw new VaultConfigurationException(
                    $"{typeof(TVault).Name} is at version {target} by its [MongoVersion], but declares no migrations. " +
                    "Declare them with Migrations(m => ...), or remove the attribute.");
        }

        return new MigrationModel(_types, _collectionName.TryGet(out var name) ? name : DefaultCollectionName, target);
    }

    private static SemVersion? Target()
    {
        if (typeof(TVault).GetCustomAttribute<MongoVersionAttribute>() is not { } attribute)
        {
            return null;
        }

        return SemVersion.TryParse(attribute.Version, SemVersionStyles.Strict, out var version)
            ? version
            : throw new VaultConfigurationException(
                $"{typeof(TVault).Name}'s [MongoVersion(\"{attribute.Version}\")] isn't a semantic version, such as 2.0.0.");
    }

    private void AddType(Type type)
    {
        if (!_types.Contains(type))
        {
            _types.Add(type);
        }
    }
}
