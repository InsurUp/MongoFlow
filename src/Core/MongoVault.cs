using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace MongoFlow;

public abstract class MongoVault : IDisposable
{
    private readonly VaultConfigurationManager _configurationManager;
    private readonly List<VaultOperation> _operations = [];

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

    public DocumentSet<TDocument> Set<TDocument>()
    {
        return new DocumentSet<TDocument>(this);
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
        List<(VaultInterceptor, VaultInterceptorContext)> interceptorMappings = [];

        foreach (var (name, interceptor) in interceptors)
        {
            var interceptorOperations = operations
                .Where(x => !x.InterceptorDisableContext.AllDisabled && !x.InterceptorDisableContext.DisabledItems.Contains(name))
                .ToList();
            
            if (interceptorOperations.Count == 0)
            {
                continue;
            }
            
            var interceptorContext = new VaultInterceptorContext(this, interceptorOperations, session, ServiceProvider);
            
            interceptorMappings.Add((interceptor, interceptorContext));
        }

        foreach (var (interceptor, context) in interceptorMappings)
        {
            await interceptor.SavingChangesAsync(context, cancellationToken);
        }

        var affected = 0;

        try
        {
            var diagnosticEnabled = interceptorMappings.Any(x => x.Item2.DiagnosticsEnabled);
            var operationContext = new VaultOperationContext(session, this, diagnosticEnabled);

            foreach (var operation in operations)
            {
                affected += await operation.ExecuteAsync(operationContext, cancellationToken);
            }

            foreach (var (interceptor, context) in interceptorMappings)
            {
                await interceptor.SavedChangesAsync(context, affected, cancellationToken);
            }
        }
        catch (Exception e)
        {
            foreach (var (interceptor, context) in interceptorMappings)
            {
                await interceptor.SaveChangesFailedAsync(e, context, cancellationToken);
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

    internal void AddOperation(VaultOperation operation)
    {
        if (!_operations.Exists(x => x.DocumentType == operation.DocumentType &&
                                     x.CurrentDocument is not null &&
                                     x.CurrentDocument == operation.CurrentDocument))
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