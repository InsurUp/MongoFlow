using Microsoft.Extensions.DependencyInjection;

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

    public bool NeedsOriginals { get; private set; }

    public Func<IVaultCollectionInfo, bool>? Predicate { get; private set; }

    IInterceptorBuilder IInterceptorBuilder.NeedsOriginals()
    {
        NeedsOriginals = true;
        return this;
    }

    IInterceptorBuilder IInterceptorBuilder.For(Func<IVaultCollectionInfo, bool> collections)
    {
        ArgumentNullException.ThrowIfNull(collections);

        var previous = Predicate;
        Predicate = previous is null ? collections : collection => previous(collection) && collections(collection);
        return this;
    }

    public InterceptorModel Build(IReadOnlyList<CollectionModel> collections)
    {
        var appliesTo = collections
            .Select(collection => (Collection is null ? Predicate?.Invoke(collection) ?? true : Collection.Model == collection) &&
                                  (Owner is not { } owner || !collection.Without.Contains(owner)))
            .ToArray();

        return new InterceptorModel(Type, Instance, Owner, NeedsOriginals, appliesTo);
    }
}

internal sealed class InterceptorModel(
    Type? type,
    VaultInterceptor? instance,
    FeatureKey? owner,
    bool needsOriginals,
    bool[] appliesTo)
{
    public FeatureKey? Owner { get; } = owner;

    public bool NeedsOriginals { get; } = needsOriginals;

    /// <summary>Whether the interceptor sees operations on a collection, by the collection's index in the vault.</summary>
    public bool AppliesTo(CollectionModel collection) => appliesTo[collection.Index];

    public VaultInterceptor Create(IServiceProvider services) =>
        instance ?? (VaultInterceptor)ActivatorUtilities.CreateInstance(services, type!);
}
