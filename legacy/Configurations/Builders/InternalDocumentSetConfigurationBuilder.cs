using System.Linq.Expressions;
using System.Reflection;

namespace MongoFlow;

internal sealed class InternalDocumentSetConfigurationBuilder
{
    private readonly VaultProperty _vaultProperty;

    internal InternalDocumentSetConfigurationBuilder(VaultProperty vaultProperty)
    {
        _vaultProperty = vaultProperty;
    }

    private string? _name;
    private DocumentProperty? _key;
    private readonly HashSet<IQueryFilterDefinition> _queryFilterDefinitions = [];

    public void Key(DocumentProperty property)
    {
        _key = property;
    }

    public void Key(string name)
    {
        var property = _vaultProperty.DocumentType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);

        if (property is null)
        {
            throw new VaultConfigurationException($"Property {name} not found in {_vaultProperty.DocumentType.Name}");
        }

        Key(property);
    }

    public void Key(PropertyInfo propertyInfo)
    {
        var documentProperty = new DocumentProperty(propertyInfo, _vaultProperty.DocumentType);

        Key(documentProperty);
    }

    public void Name(string name)
    {
        _name = name;
    }

    internal void AddQueryFilter(IQueryFilterDefinition queryFilterDefinition)
    {
        _queryFilterDefinitions.Add(queryFilterDefinition);
    }

    public void AddQueryFilter(string? name, LambdaExpression expression)
    {
        _queryFilterDefinitions.Add(new StaticQueryFilterDefinition(expression, name));
    }

    public void AddQueryFilter(string? name, Func<IServiceProvider, LambdaExpression> expressionProvider)
    {
        _queryFilterDefinitions.Add(new ServiceProviderQueryFilterDefinition(expressionProvider, name));
    }

    internal DocumentSetConfiguration Build()
    {
        return new DocumentSetConfiguration(key: _key ?? CreateDefaultDocumentKeyProperty(),
            name: _name ?? _vaultProperty.Name,
            queryFilterDefinitions: _queryFilterDefinitions);
    }

    private DocumentProperty CreateDefaultDocumentKeyProperty()
    {
        var idProperty = _vaultProperty.FindIdProperty();

        if (idProperty is null)
        {
            throw new VaultConfigurationException($"ID property not found in {_vaultProperty.DocumentType.Name}");
        }

        return new DocumentProperty(idProperty, _vaultProperty.DocumentType);
    }

}
