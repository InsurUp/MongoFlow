using System.Reflection;

namespace MongoFlow;

public readonly struct DocumentProperty
{
    public DocumentProperty(PropertyInfo property, Type documentType)
    {
        Property = property;
        DocumentType = documentType;
    }

    public string Name => Property.Name;
    public PropertyInfo Property { get; }
    public Type DocumentType { get; }

}
