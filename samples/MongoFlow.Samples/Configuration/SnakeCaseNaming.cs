using System.Text;

namespace MongoFlow.Samples.Configuration;

/// <summary>A second default, kept separate so a vault can skip the platform rules but keep the naming.</summary>
public sealed class SnakeCaseNaming<TVault> : IVaultConfiguration<TVault> where TVault : MongoVault
{
    public void Configure(IVaultBuilder<TVault> vault) => vault.ForEachCollection(new SnakeCaseCollectionNames());
}

/// <summary>Names every collection after its property: <c>PolicyVersions</c> becomes <c>policy_versions</c>.</summary>
public sealed class SnakeCaseCollectionNames : IVaultCollectionConfiguration
{
    public void Configure<TDocument>(IVaultCollectionBuilder<TDocument> collection) =>
        collection.Name(ToSnakeCase(collection.PropertyName));

    private static string ToSnakeCase(string name)
    {
        var builder = new StringBuilder(name.Length + 4);

        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(name[i]));
        }

        return builder.ToString();
    }
}
