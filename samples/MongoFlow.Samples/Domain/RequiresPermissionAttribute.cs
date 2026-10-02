namespace MongoFlow.Samples.Domain;

/// <summary>
/// Reading documents of this type needs <see cref="Permission"/>. Users holding only the <c>.own</c> variant of it see
/// the documents they own.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class RequiresPermissionAttribute(string permission) : Attribute
{
    public string Permission { get; } = permission;
}
