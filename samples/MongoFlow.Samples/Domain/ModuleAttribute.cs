namespace MongoFlow.Samples.Domain;

/// <summary>The document belongs to a paid module; agencies that haven't bought it can't see it.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ModuleAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
