using System.Reflection;

namespace MongoFlow;

public readonly struct VaultProperty
{
    public VaultProperty(string name, Type documentType, PropertyInfo propertyInfo)
    {
        Name = name;
        DocumentType = documentType;
        PropertyInfo = propertyInfo;
    }

    public string Name { get; }
    public Type DocumentType { get; }
    public PropertyInfo PropertyInfo { get; }

    public PropertyInfo? FindIdProperty()
    {
        return DocumentType.GetProperty("Id", BindingFlags.Public | BindingFlags.Instance);
    }
}
