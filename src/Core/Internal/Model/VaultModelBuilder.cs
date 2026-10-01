using System.Linq.Expressions;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace MongoFlow;

internal sealed class VaultModelBuilder<TVault> : VaultModelBuilderBase, IVaultBuilder<TVault> where TVault : MongoVault
{
    private readonly IServiceProvider _services;
    private readonly List<CollectionModelBuilder> _collections;
    private readonly Layered<Func<IMongoDatabase>> _database = new();
    private readonly LayeredList<VaultQueryFilter> _filters = new();
    private readonly HashSet<Type> _applied = [];
    private readonly HashSet<Type> _ownConfigurations = [];
    private readonly HashSet<Type> _skipped = [];
    private bool _skipAll;

    public VaultModelBuilder(IServiceProvider services)
    {
        _services = services;
        _collections = typeof(TVault)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(CreateCollectionBuilder)
            .OfType<CollectionModelBuilder>()
            .ToList();
    }

    public IReadOnlyList<IVaultCollectionInfo> Collections => _collections;

    public IVaultBuilder<TVault> UseDatabase(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        _database.Set(Layer, () => _services.GetRequiredService<IMongoClient>().GetDatabase(name));
        return this;
    }

    public IVaultBuilder<TVault> UseDatabase(IMongoDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);

        _database.Set(Layer, () => database);
        return this;
    }

    public IVaultBuilder<TVault> UseConfiguration<TConfiguration>() where TConfiguration : class, IVaultConfiguration<TVault>
    {
        Apply(typeof(TConfiguration), () => ActivatorUtilities.CreateInstance<TConfiguration>(_services));
        return this;
    }

    public IVaultBuilder<TVault> UseConfiguration(IVaultConfiguration<TVault> configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        Apply(configuration.GetType(), () => configuration);
        return this;
    }

    public IVaultBuilder<TVault> SkipDefaultConfiguration<TConfiguration>() where TConfiguration : class, IVaultConfiguration<TVault>
    {
        ThrowIfInDefault(nameof(SkipDefaultConfiguration));

        _skipped.Add(typeof(TConfiguration));
        return this;
    }

    public IVaultBuilder<TVault> SkipAllDefaultConfigurations()
    {
        ThrowIfInDefault(nameof(SkipAllDefaultConfigurations));

        _skipAll = true;
        return this;
    }

    public IVaultBuilder<TVault> Collection<TDocument>(Expression<Func<TVault, IVaultCollection<TDocument>>> collection,
        Action<IVaultCollectionBuilder<TDocument>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        configure(Find<CollectionModelBuilder<TDocument>>(collection));
        return this;
    }

    public IVaultBuilder<TVault> Collection<TDocument, TKey>(Expression<Func<TVault, IVaultCollection<TDocument, TKey>>> collection,
        Action<IVaultCollectionBuilder<TDocument, TKey>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        configure(Find<CollectionModelBuilder<TDocument, TKey>>(collection));
        return this;
    }

    public IVaultBuilder<TVault> ForEachCollection(IVaultCollectionConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        foreach (var collection in _collections)
        {
            collection.Accept(configuration);
        }

        return this;
    }

    public IVaultBuilder<TVault> QueryFilter<TTarget>(Expression<Func<TTarget, bool>> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        _filters.Add(Layer, new VaultQueryFilter<TTarget>(Layer, Owner, @static: filter));
        return this;
    }

    public IVaultBuilder<TVault> QueryFilter<TTarget>(Func<IServiceProvider, Expression<Func<TTarget, bool>>> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        _filters.Add(Layer, new VaultQueryFilter<TTarget>(Layer, Owner, perQuery: filter));
        return this;
    }

    public IVaultBuilder<TVault> QueryFilter<TTarget>(
        Func<IServiceProvider, CancellationToken, ValueTask<Expression<Func<TTarget, bool>>>> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        _filters.Add(Layer, new VaultQueryFilter<TTarget>(Layer, Owner, @async: filter));
        return this;
    }

    public IVaultBuilder<TVault> AddInterceptor<TInterceptor>(Action<IInterceptorBuilder>? configure = null)
        where TInterceptor : VaultInterceptor
    {
        Register(typeof(TInterceptor), null, null, configure);
        return this;
    }

    public IVaultBuilder<TVault> AddInterceptor(VaultInterceptor interceptor, Action<IInterceptorBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(interceptor);

        Register(null, interceptor, null, configure);
        return this;
    }

    public IVaultBuilder<TVault> AddFeature<TFeature>() where TFeature : class, IVaultFeature =>
        AddFeature(ActivatorUtilities.CreateInstance<TFeature>(_services));

    public IVaultBuilder<TVault> AddFeature<TFeature>(TFeature feature) where TFeature : class, IVaultFeature
    {
        ArgumentNullException.ThrowIfNull(feature);

        var key = TFeature.Key;
        FeatureKeys.ThrowIfDefault(key, nameof(IVaultFeature.Key));

        PushOwner(key);
        try
        {
            feature.Configure(this);
        }
        finally
        {
            PopOwner();
        }

        return this;
    }

    /// <summary>Applies a default configuration, closed over <typeparamref name="TVault"/>, unless the vault skipped it.</summary>
    public void ApplyDefault(Type openConfiguration)
    {
        if (_skipAll)
        {
            return;
        }

        Type configuration;
        try
        {
            configuration = openConfiguration.MakeGenericType(typeof(TVault));
        }
        catch (ArgumentException)
        {
            return; // TVault doesn't satisfy its constraints, so the default isn't meant for this vault.
        }

        if (_skipped.Contains(configuration) || _ownConfigurations.Contains(configuration))
        {
            return;
        }

        Apply(configuration, () => (IVaultConfiguration<TVault>)ActivatorUtilities.CreateInstance(_services, configuration));
    }

    public VaultModel Build()
    {
        var duplicate = _collections.GroupBy(collection => collection.DocumentType).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new VaultConfigurationException(
                $"{typeof(TVault).Name} declares more than one collection of {duplicate.Key.Name} " +
                $"({string.Join(", ", duplicate.Select(collection => collection.PropertyName))}).");
        }

        var database = _database.TryGet(out var resolve)
            ? resolve()
            : throw new VaultConfigurationException(
                $"{typeof(TVault).Name} has no database. Call UseDatabase when registering it, or in a configuration.");

        foreach (var filter in _filters.All)
        {
            foreach (var collection in _collections)
            {
                collection.Apply(filter);
            }
        }

        var collections = _collections.Select(collection => collection.Build(database)).ToArray();

        var interceptors = Interceptors
            .OrderBy(registration => registration.RunsLast)
            .ThenBy(registration => registration.Layer)
            .ThenBy(registration => registration.Order)
            .Select(registration => registration.Build(_collections))
            .ToArray();

        return new VaultModel(typeof(TVault),
            database,
            collections,
            interceptors);
    }

    private void Apply(Type configurationType, Func<IVaultConfiguration<TVault>> create)
    {
        if (!_applied.Add(configurationType))
        {
            return;
        }

        if (Layer == Layer.Own)
        {
            _ownConfigurations.Add(configurationType);
        }

        create().Configure(this);
    }

    private void ThrowIfInDefault(string method)
    {
        if (Layer == Layer.Default)
        {
            throw new InvalidOperationException($"{method} can't be called from a default configuration.");
        }
    }

    private TBuilder Find<TBuilder>(LambdaExpression selector) where TBuilder : CollectionModelBuilder
    {
        ArgumentNullException.ThrowIfNull(selector);

        if (MemberExpressions.GetMember(selector, "collection").Member is not PropertyInfo property)
        {
            throw new ArgumentException($"Expected a collection property, such as x => x.Policies, but got {selector}.", "collection");
        }

        return _collections.FirstOrDefault(collection => IsSameProperty(collection.Property, property)) as TBuilder
            ?? throw new ArgumentException($"{property.Name} isn't a collection declared on {typeof(TVault).Name}.", "collection");
    }

    private static bool IsSameProperty(PropertyInfo declared, PropertyInfo selected)
    {
        if (declared.HasSameMetadataDefinitionAs(selected))
        {
            return true;
        }

        // Selected through an interface the vault implements, such as a default constrained to IAuditedVault.
        if (selected.DeclaringType is not { IsInterface: true } contract || !contract.IsAssignableFrom(typeof(TVault)) ||
            selected.GetMethod is null)
        {
            return false;
        }

        var map = typeof(TVault).GetInterfaceMap(contract);
        var index = Array.IndexOf(map.InterfaceMethods, selected.GetMethod);

        return index >= 0 && declared.GetMethod is { } getter && getter.HasSameMetadataDefinitionAs(map.TargetMethods[index]);
    }

    private CollectionModelBuilder? CreateCollectionBuilder(PropertyInfo property)
    {
        if (!property.PropertyType.IsGenericType)
        {
            return null;
        }

        var definition = property.PropertyType.GetGenericTypeDefinition();
        Type builderType;

        if (definition == typeof(IVaultCollection<>))
        {
            builderType = typeof(CollectionModelBuilder<>).MakeGenericType(property.PropertyType.GetGenericArguments());
        }
        else if (definition == typeof(IVaultCollection<,>))
        {
            builderType = typeof(CollectionModelBuilder<,>).MakeGenericType(property.PropertyType.GetGenericArguments());
        }
        else
        {
            return null;
        }

        if (property.SetMethod is null)
        {
            throw new VaultConfigurationException(
                $"{typeof(TVault).Name}.{property.Name} needs a setter or an init accessor for MongoFlow to fill it.");
        }

        return (CollectionModelBuilder)Activator.CreateInstance(builderType, this, property)!;
    }
}
