namespace MongoFlow;

public static class MultiTenancyDocumentSetExtensions
{
    public static DocumentSet<T> DisableMultiTenancy<T>(this DocumentSet<T> documentSet)
    {
        return documentSet
            .DisableQueryFilters("multi-tenancy")
            .DisableInterceptors("multi-tenancy");
    }
}