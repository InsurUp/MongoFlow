using System.Linq.Expressions;
using System.Reflection;
using MongoDB.Driver;

namespace MongoFlow;

/// <summary>A vault as built at startup: shared by every instance, never changed.</summary>
internal sealed class VaultModel
{
    private readonly Action<MongoVault, object>[] _setters;

    public VaultModel(Type vaultType, IMongoDatabase database, CollectionModel[] collections, InterceptorModel[] interceptors,
        MigrationModel? migrations)
    {
        VaultType = vaultType;
        Database = database;
        Collections = collections;
        Interceptors = interceptors;
        Migrations = migrations;
        CollectionsByDocument = collections.ToDictionary(collection => collection.DocumentType);
        _setters = collections.Select(collection => CreateSetter(collection.Property)).ToArray();
    }

    public Type VaultType { get; }

    public IMongoDatabase Database { get; }

    public IMongoClient Client => Database.Client;

    public IReadOnlyList<CollectionModel> Collections { get; }

    public IReadOnlyDictionary<Type, CollectionModel> CollectionsByDocument { get; }

    public IReadOnlyList<InterceptorModel> Interceptors { get; }

    public MigrationModel? Migrations { get; }

    public void SetCollection(MongoVault vault, CollectionModel collection, object value) => _setters[collection.Index](vault, value);

    private static Action<MongoVault, object> CreateSetter(PropertyInfo property)
    {
        var vault = Expression.Parameter(typeof(MongoVault), "vault");
        var value = Expression.Parameter(typeof(object), "value");

        return Expression.Lambda<Action<MongoVault, object>>(
            Expression.Call(Expression.Convert(vault, property.DeclaringType!), property.SetMethod!,
                Expression.Convert(value, property.PropertyType)),
            vault,
            value).Compile();
    }
}

internal sealed record MigrationModel(IReadOnlyList<Type> Types, string CollectionName);

internal sealed class MigrationModelBuilder<TVault>(VaultModelBuilderBase vault) : IMigrationBuilder<TVault> where TVault : MongoVault
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
        _types.Count == 0 ? null : new MigrationModel(_types, _collectionName.TryGet(out var name) ? name : "migrations");

    private void AddType(Type type)
    {
        if (!_types.Contains(type))
        {
            _types.Add(type);
        }
    }
}
