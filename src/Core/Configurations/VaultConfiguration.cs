namespace MongoFlow;

internal sealed class VaultConfiguration
{
    private readonly Dictionary<Type, DocumentSetConfiguration> _setConfigurations;
    private readonly List<IVaultInterceptorProvider> _interceptors;

    public VaultConfiguration(Dictionary<Type, DocumentSetConfiguration> setConfigurations,
        List<IVaultInterceptorProvider> interceptors,
        IMongoDatabase database)
    {
        _setConfigurations = setConfigurations;
        _interceptors = interceptors;
        Database = database;
    }

    public DocumentSetConfiguration GetDocumentSetConfiguration<TDocumentType>()
    {
        return GetDocumentSetConfiguration(typeof(TDocumentType));
    }

    public DocumentSetConfiguration GetDocumentSetConfiguration(Type type)
    {
        if (_setConfigurations.TryGetValue(type, out var documentSetConfiguration))
        {
            return documentSetConfiguration;
        }

        throw new VaultConfigurationException($"Document type {type.Name} not found in {nameof(VaultConfiguration)}");
    }

    public IReadOnlyCollection<IVaultInterceptorProvider> Interceptors => _interceptors;
    public IReadOnlyDictionary<Type, DocumentSetConfiguration> SetConfigurations => _setConfigurations;
    public IMongoDatabase Database { get; }
}
