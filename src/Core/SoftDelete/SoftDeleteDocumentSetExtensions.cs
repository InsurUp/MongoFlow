namespace MongoFlow;

public static class SoftDeleteDocumentSetExtensions
{
    public static DocumentSet<T> DisableSoftDelete<T>(this DocumentSet<T> documentSet)
    {
        return documentSet
            .DisableQueryFilters("soft-delete")
            .DisableInterceptors("soft-delete");
    }
}