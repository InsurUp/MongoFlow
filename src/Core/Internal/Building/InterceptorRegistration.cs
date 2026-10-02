namespace MongoFlow;

internal sealed class InterceptorRegistration : IInterceptorBuilder
{
    public required Layer Layer { get; init; }

    public required int Order { get; init; }

    public Type? Type { get; init; }

    public VaultInterceptor? Instance { get; init; }

    /// <summary>The collection it's registered on, or <see langword="null"/> when it's registered on the vault.</summary>
    public CollectionModelBuilder? Collection { get; init; }

    public FeatureKey? Owner { get; init; }

    /// <summary>
    /// Runs after every other interceptor before the write, so it sees the operations as they'll be written, and before
    /// every other one after it. Only for built-in features, such as the concurrency token.
    /// </summary>
    public bool RunsLast { get; set; }

    public Func<IVaultCollectionInfo, bool>? Predicate { get; private set; }

    IInterceptorBuilder IInterceptorBuilder.For(Func<IVaultCollectionInfo, bool> collections)
    {
        ArgumentNullException.ThrowIfNull(collections);

        var previous = Predicate;
        Predicate = previous is null ? collections : collection => previous(collection) && collections(collection);
        return this;
    }

    public InterceptorModel Build(IReadOnlyList<CollectionModelBuilder> collections)
    {
        var visible = collections
            .Where(collection => (Collection is null ? Predicate?.Invoke(collection) ?? true : Collection == collection) &&
                                 (Owner is not { } owner || !collection.OptedOut.Contains(owner)))
            .Select(collection => collection.Model!)
            .ToHashSet<IVaultCollectionInfo>(ReferenceEqualityComparer.Instance);

        return new InterceptorModel(Type, Instance, Owner, visible);
    }
}
