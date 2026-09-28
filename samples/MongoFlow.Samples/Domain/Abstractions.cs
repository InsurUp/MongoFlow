namespace MongoFlow.Samples.Domain;

/// <summary>The platform's tenant. Every agency sees only its own data.</summary>
public readonly record struct AgencyId(Guid Value);

public interface ITenantOwned
{
    AgencyId? AgencyId { get; set; }
}

public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
}

/// <summary>Soft deletion that records when, rather than just whether.</summary>
public interface IDeletedAt
{
    DateTime? DeletedAt { get; set; }
}

public interface IOwnedByUser
{
    string OwnerUserId { get; }
}

public interface ITimestamped
{
    DateTimeOffset CreatedAt { get; set; }

    DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>A document that records domain events for the outbox to store with the change that raised them.</summary>
public interface IRaisesEvents
{
    IReadOnlyList<object> TakeEvents();
}

/// <summary>
/// Reading documents of this type needs <see cref="Permission"/>. Users holding only the <c>.own</c> variant of it see
/// the documents they own.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class RequiresPermissionAttribute(string permission) : Attribute
{
    public string Permission { get; } = permission;
}

/// <summary>The document belongs to a paid module; agencies that haven't bought it can't see it.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ModuleAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    bool IsPlatformAdmin { get; }

    string? UserId { get; }

    AgencyId? AgencyId { get; }

    ValueTask<IReadOnlySet<string>> GetPermissionsAsync(CancellationToken cancellationToken);

    ValueTask<IReadOnlySet<string>> GetModulesAsync(CancellationToken cancellationToken);
}
