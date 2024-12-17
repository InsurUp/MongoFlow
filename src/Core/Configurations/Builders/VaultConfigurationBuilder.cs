using System.Linq.Expressions;
using MongoDB.Driver;

namespace MongoFlow;

public class VaultConfigurationBuilder
{
    private readonly Type _vaultType;
    private readonly Dictionary<Type, InternalDocumentSetConfigurationBuilder> _documentSetConfigurationBuilders = [];
    private readonly List<IVaultInterceptorProvider> _interceptors = [];
    private IMongoDatabase? _database;

    internal VaultConfigurationBuilder(Type vaultType)
    {
        _vaultType = vaultType;
        var documentSetProperties = VaultPropertyCache.GetProperties(vaultType);

        foreach (var (type, documentSetProperty) in documentSetProperties)
        {
            _documentSetConfigurationBuilders[type] = new InternalDocumentSetConfigurationBuilder(documentSetProperty);
        }
    }
    
    public IEnumerable<VaultProperty> Properties => VaultPropertyCache.GetProperties(_vaultType).Values;

    public void ConfigureDocumentType<TDocument>(Action<DocumentSetConfigurationBuilder<TDocument>> configure)
    {
        var internalBuilder = _documentSetConfigurationBuilders.TryGetValue(typeof(TDocument), out var existingBuilder)
            ? existingBuilder
            : throw new VaultConfigurationException($"Document type {typeof(TDocument).Name} not found in {_vaultType.Name}");

        var builder = new DocumentSetConfigurationBuilder<TDocument>(internalBuilder);

        configure(builder);

        _documentSetConfigurationBuilders[typeof(TDocument)] = internalBuilder;
    }

    public void ConfigureDocumentType(Type type, Action<DocumentSetConfigurationBuilder> configure)
    {
        if (_documentSetConfigurationBuilders.TryGetValue(type, out var existingBuilder))
        {
            var builder = new DocumentSetConfigurationBuilder(existingBuilder);

            configure(builder);
        }
        else
        {
            throw new VaultConfigurationException($"Document type {type.Name} not found in {_vaultType.Name}");
        }
    }

    public void AddInterceptor<TInterceptor>(params object[] args) where TInterceptor : VaultInterceptor
    {
        AddInterceptor<TInterceptor>(null, args);
    }
    
    public void AddInterceptor<TInterceptor>(string? name, params object[] args) where TInterceptor : VaultInterceptor
    {
        _interceptors.Add(new ServiceVaultInterceptorProvider(typeof(TInterceptor), name, args));
    }

    public void AddInterceptor(VaultInterceptor interceptor)
    {
        AddInterceptor(null, interceptor);
    }
    
    public void AddInterceptor(string? name, VaultInterceptor interceptor)
    {
        _interceptors.Add(new StaticVaultInterceptorProvider(interceptor, name));
    }

    public void SetDatabase(IMongoDatabase database)
    {
        _database = database;
    }

    public void AddSoftDelete<TInterface>(VaultSoftDeleteOptions<TInterface> options)
    {
        var body = Expression.Not(options.IsDeletedAccessor.Body);
        var expression = Expression.Lambda(body, options.IsDeletedAccessor.Parameters[0]);

        AddMultiQueryFilters<TInterface>("soft-delete", expression);

        AddInterceptor("soft-delete", new SoftDeleteInterceptor<TInterface>(options));
    }

    public void AddMultiTenancy<TInterface, TTenantId>(VaultMultiTenancyOptions<TInterface, TTenantId> options) where TTenantId : struct
    {
        AddMultiQueryFilters<TInterface>("multi-tenancy", serviceProvider =>
        {
            var tenantId = options.TenantIdProvider(serviceProvider);

            var body = options.TenantIdAccessor.Body;

            var constantExpression = Expression.Constant(tenantId, typeof(TTenantId?));

            var comparison = Expression.Equal(body, constantExpression);

            return Expression.Lambda<Func<TInterface, bool>>(
                comparison,
                options.TenantIdAccessor.Parameters[0]
            );
        });

        AddInterceptor("multi-tenancy", new MultiTenancyInterceptor<TInterface, TTenantId>(options));
    }

    public void AddMultiQueryFilters<TInterface>(Expression<Func<TInterface, bool>> expression)
    {
        AddMultiQueryFilters(null, expression);
    }
    
    public void AddMultiQueryFilters<TInterface>(string? name, Expression<Func<TInterface, bool>> expression)
    {
        var documentSetProperties = VaultPropertyCache.GetProperties(_vaultType);

        foreach (var (type, _) in documentSetProperties)
        {
            if (typeof(TInterface).IsAssignableFrom(type))
            {
                ConfigureDocumentType(type, builder =>
                {
                    builder.AddQueryFilter(name, expression);
                });
            }
        }
    }

    public void AddMultiQueryFilters<TInterface>(Func<IServiceProvider, Expression<Func<TInterface, bool>>> expressionProvider)
    {
        AddMultiQueryFilters(null, expressionProvider);
    }
    
    public void AddMultiQueryFilters<TInterface>(string? name, Func<IServiceProvider, Expression<Func<TInterface, bool>>> expressionProvider)
    {
        var documentSetProperties = VaultPropertyCache.GetProperties(_vaultType);

        foreach (var (type, _) in documentSetProperties)
        {
            if (typeof(TInterface).IsAssignableFrom(type))
            {
                ConfigureDocumentType(type, builder =>
                {
                    builder.AddQueryFilter(name, expressionProvider);
                });
            }
        }
    }

    public void AddMultiQueryFilters<TInterface>(LambdaExpression expression)
    {
        AddMultiQueryFilters<TInterface>(null, expression);
    }
    
    public void AddMultiQueryFilters<TInterface>(string? name, LambdaExpression expression)
    {
        var documentSetProperties = VaultPropertyCache.GetProperties(_vaultType);

        foreach (var (type, _) in documentSetProperties)
        {
            if (typeof(TInterface).IsAssignableFrom(type))
            {
                ConfigureDocumentType(type, builder =>
                {
                    builder.AddQueryFilter(name, expression);
                });
            }
        }
    }

    internal VaultConfiguration Build()
    {
        var setConfigurations = _documentSetConfigurationBuilders
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Build());

        if (_database is null)
        {
            throw new VaultConfigurationException("Database is required for VaultConfiguration");
        }

        return new VaultConfiguration(setConfigurations, _interceptors, _database);
    }
}
