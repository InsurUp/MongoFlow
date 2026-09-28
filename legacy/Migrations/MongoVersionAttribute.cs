namespace MongoFlow;

[AttributeUsage(AttributeTargets.Class)]
public sealed class MongoVersionAttribute : Attribute
{
    public string Version { get; }

    public MongoVersionAttribute(string version)
    {
        Version = version;
    }
}