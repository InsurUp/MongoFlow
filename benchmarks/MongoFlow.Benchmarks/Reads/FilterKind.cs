namespace MongoFlow.Benchmarks;

/// <summary>The kinds of query filter a read starts from.</summary>
public enum FilterKind
{
    /// <summary>No query filter.</summary>
    None,

    /// <summary>The same filter for every request, such as soft delete's: joined once, at startup.</summary>
    Static,

    /// <summary>A filter decided per query from the request's services, such as a tenant's.</summary>
    PerQuery,

    /// <summary>A per-query filter resolved asynchronously.</summary>
    Async,

    /// <summary>Multi-tenancy's filter, built per query from the current tenant.</summary>
    Tenant,

    /// <summary>A static filter, read through a view with its feature switched off.</summary>
    FeatureOff
}
