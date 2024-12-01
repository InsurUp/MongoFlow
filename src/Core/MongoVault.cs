using Microsoft.Extensions.DependencyInjection;

namespace MongoFlow;

public abstract class MongoVault : IDisposable
{
    private readonly VaultConfigurationManager _configurationManager;
    private readonly List<IVaultOperation> _operations = [];

    private MongoVaultTransaction? _transaction;

    protected MongoVault(VaultConfigurationManager configurationManager)
    {
        _configurationManager = configurationManager;

        DocumentTypes = VaultPropertyCache.GetProperties(GetType()).Values;

        foreach (var documentType in DocumentTypes)
        {
            var setType = typeof(DocumentSet<>).MakeGenericType(documentType.DocumentType);
            var set = Activator.CreateInstance(setType, this, false)!;
            documentType.PropertyInfo.SetValue(this, set);
        }
    }

    protected IEnumerable<VaultProperty> DocumentTypes { get; }

    internal VaultConfiguration Configuration => _configurationManager.Configuration;

    internal IServiceProvider ServiceProvider => _configurationManager.ServiceProvider;
    
    private IMongoGlobalTransactionManager? GlobalTransactionManager => ServiceProvider.GetService<IMongoGlobalTransactionManager>();

    internal IMongoDatabase MongoDatabase => Configuration.Database!;

    public bool MigrationEnabled => _configurationManager.MigrationEnabled;
    
    public IMongoVaultMigrationManager MigrationManager => _configurationManager.MigrationManager;

    internal IMongoCollection<TDocument> GetCollection<TDocument>()
    {
        var setConfiguration = Configuration.GetDocumentSetConfiguration<TDocument>();

        return MongoDatabase.GetCollection<TDocument>(setConfiguration.Name);
    }

    public DocumentSet<TDocument> Set<TDocument>(bool ignoreQueryFilter = false)
    {
        return new DocumentSet<TDocument>(this, ignoreQueryFilter);
    }

    public bool IsInTransaction => _transaction is not null || GlobalTransactionManager?.CurrentTransaction is not null;
    
    public IMongoVaultTransaction? CurrentTransaction => _transaction ?? GlobalTransactionManager?.CurrentTransaction;

    public IMongoVaultTransaction BeginTransaction()
    {
        if (IsInTransaction)
        {
            throw new InvalidOperationException("BeginTransaction cannot be called inside a transaction.");
        }

        _transaction = new MongoVaultTransaction(this, MongoDatabase.Client.StartSession());

        return _transaction;
    }

    internal void ClearTransaction()
    {
        _transaction = null;
    }

    public DocumentProperty GetDocumentKeyProperty(Type documentType)
    {
        return Configuration.GetDocumentSetConfiguration(documentType).Key;
    }

    public virtual async Task<int> SaveAsync(CancellationToken cancellationToken = default)
    {
        var operations = _operations.ToList();

        _operations.Clear();

        if (operations.Count == 0)
        {
            return 0;
        }
        
        var session = CurrentTransaction is not null
            ? CurrentTransaction.Session
            : await MongoDatabase.Client.StartSessionAsync(cancellationToken: cancellationToken);

        if (!session.IsInTransaction)
        {
            session.StartTransaction();
        }

        var interceptors = _configurationManager.ResolveInterceptors();
        var interceptorContext = new VaultInterceptorContext(this, operations, session, ServiceProvider);

        foreach (var interceptor in interceptors)
        {
            await interceptor.SavingChangesAsync(interceptorContext, cancellationToken);
        }

        var affected = 0;

        try
        {
            foreach (var operation in operations)
            {
                affected += await operation.ExecuteAsync(session, cancellationToken);
            }

            foreach (var interceptor in interceptors)
            {
                await interceptor.SavedChangesAsync(interceptorContext, affected, cancellationToken);
            }
        }
        catch (Exception e)
        {
            foreach (var interceptor in interceptors)
            {
                await interceptor.SaveChangesFailedAsync(e, interceptorContext, cancellationToken);
            }

            if (CurrentTransaction is null)
            {
                await session.AbortTransactionAsync(cancellationToken);
            }

            throw;
        }
        
        if (CurrentTransaction is null)
        {
            try
            {
                await session.CommitTransactionAsync(cancellationToken);
            }
            finally
            {
                session.Dispose();
            }
        }

        return affected;
    }

    internal void AddOperation(IVaultOperation operation)
    {
        if (!_operations.Exists(x => x.DocumentType == operation.DocumentType))
        {
            _operations.Add(operation);
        }
    }

    public void Dispose()
    {
        _transaction?.Dispose();
        GC.SuppressFinalize(this);
    }
}