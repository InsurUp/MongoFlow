namespace MongoFlow;

public static class SoftDeleteDocumentSetExtensions
{
    public static IDocumentSet<T> DisableSoftDelete<T>(this IDocumentSet<T> documentSet)
    {
        return documentSet
            .DisableQueryFilters("soft-delete")
            .DisableInterceptors("soft-delete");
    }
}