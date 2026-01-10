namespace MongoFlow;

public static class MultiTenancyDocumentSetExtensions
{
    public static IDocumentSet<T> DisableMultiTenancy<T>(this IDocumentSet<T> documentSet)
    {
        return documentSet
            .DisableQueryFilters("multi-tenancy")
            .DisableInterceptors("multi-tenancy");
    }
}