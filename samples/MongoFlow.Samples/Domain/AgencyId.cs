namespace MongoFlow.Samples.Domain;

/// <summary>The platform's tenant. Every agency sees only its own data.</summary>
public readonly record struct AgencyId(Guid Value);
