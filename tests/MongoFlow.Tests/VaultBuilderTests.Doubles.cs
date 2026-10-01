namespace MongoFlow.Tests;

// Vaults, configurations and features of the shapes the builder accepts, and of those it rejects.
public partial class VaultBuilderTests
{
    /// <summary>Declares its collection without a setter, so MongoFlow can't fill it.</summary>
    public sealed class NoSetterVault : MongoVault
    {
        public IVaultCollection<Order, int> Orders => null!;
    }

    /// <summary>Declares two collections of one document type.</summary>
    public sealed class DuplicateVault : MongoVault
    {
        public IVaultCollection<Order, int> Orders { get; init; } = null!;

        public IVaultCollection<Order> MoreOrders { get; init; } = null!;
    }

    /// <summary>A keyless document that neither has an Id nor ignores extra elements, so it can't read back the server's <c>_id</c>.</summary>
    public sealed class StrictNote
    {
        public string Text { get; set; } = "";
    }

    public sealed class StrictVault : MongoVault
    {
        public IVaultCollection<StrictNote> Notes { get; init; } = null!;
    }

    /// <summary>A document type that's an interface, which has no class map to check.</summary>
    public interface IEvent
    {
        string Name { get; }
    }

    /// <summary>Keyless collections whose documents can be read back: one with an Id, one of an interface.</summary>
    public sealed class LooseVault : MongoVault
    {
        public IVaultCollection<Order> Orders { get; init; } = null!;

        public IVaultCollection<IEvent> Events { get; init; } = null!;
    }

    /// <summary>Has members that look like collections but aren't declared ones, next to one that is.</summary>
    public sealed class MixedVault : MongoVault
    {
        public IVaultCollection<AuditEntry>? AuditField;

        public IVaultCollection<Order, int> Orders { get; init; } = null!;

        public string Name { get; set; } = "";

        public List<int> Numbers { get; set; } = [];

        internal IVaultCollection<AuditEntry> Hidden { get; set; } = null!;
    }

    /// <summary>Implemented by vaults with an audit trail, so a default configuration can select their audit collection.</summary>
    public interface IAudited
    {
        IVaultCollection<AuditEntry> Audit { get; }
    }

    public sealed class AuditedVault : MongoVault, IAudited
    {
        public IVaultCollection<Order, int> Orders { get; init; } = null!;

        public IVaultCollection<AuditEntry> Audit { get; init; } = null!;
    }

    /// <summary>Names the audit collection of every vault that has one.</summary>
    public sealed class AuditNaming<TVault> : IVaultConfiguration<TVault> where TVault : MongoVault, IAudited
    {
        public const string Name = "audit_log";

        public void Configure(IVaultBuilder<TVault> vault) => vault.Collection(x => x.Audit, audit => audit.Name(Name));
    }

    /// <summary>Configures itself: it names its orders and filters them.</summary>
    public sealed class ConfiguredVault : MongoVault, IConfigurableVault<ConfiguredVault>
    {
        public const string OrdersName = "from-vault";

        public IVaultCollection<Order, int> Orders { get; init; } = null!;

        public static void Configure(IVaultBuilder<ConfiguredVault> vault) => vault
            .Collection(x => x.Orders, orders => orders
                .Name(OrdersName)
                .QueryFilter(x => x.Customer != ""));
    }

    /// <summary>A vault base class that configures every vault derived from it, the way an identity package does.</summary>
    public abstract class ConfiguringBase<TSelf> : MongoVault, IConfigurableVault<TSelf> where TSelf : ConfiguringBase<TSelf>
    {
        public const string OrdersName = "from-base";

        public IVaultCollection<Order, int> Orders { get; init; } = null!;

        public static void Configure(IVaultBuilder<TSelf> vault) => vault.Collection(x => x.Orders, orders => orders.Name(OrdersName));
    }

    public sealed class DerivedVault : ConfiguringBase<DerivedVault>;

    /// <summary>
    /// Says it configures <see cref="ConfiguredVault"/>, not itself, so its own <c>Configure</c> isn't called. It also
    /// implements an unrelated generic interface.
    /// </summary>
    public sealed class MisdirectedVault : MongoVault, IConfigurableVault<ConfiguredVault>, ITagged<string>
    {
        public IVaultCollection<Order, int> Orders { get; init; } = null!;

        public static void Configure(IVaultBuilder<ConfiguredVault> vault) =>
            throw new InvalidOperationException("Not called: the vault configures another vault.");
    }

    /// <summary>A generic interface that has nothing to do with configuration.</summary>
    public interface ITagged<T>;

    /// <summary>Names every collection with a prefix.</summary>
    public sealed class Prefix(string prefix) : IVaultCollectionConfiguration
    {
        public void Configure<TDocument>(IVaultCollectionBuilder<TDocument> collection) =>
            collection.Name(prefix + collection.PropertyName);
    }

    /// <summary>A default configuration that prefixes every collection's name.</summary>
    public sealed class PrefixDefault<TVault> : IVaultConfiguration<TVault> where TVault : MongoVault
    {
        public const string Value = "default_";

        public void Configure(IVaultBuilder<TVault> vault) => vault.ForEachCollection(new Prefix(Value));
    }

    /// <summary>A default configuration that sets the database.</summary>
    public sealed class DatabaseDefault<TVault> : IVaultConfiguration<TVault> where TVault : MongoVault
    {
        public const string Name = "defaults";

        public void Configure(IVaultBuilder<TVault> vault) => vault.UseDatabase(Name);
    }

    /// <summary>A default configuration that counts how often it's applied.</summary>
    public sealed class CountingDefault<TVault>(ApplyCounter counter) : IVaultConfiguration<TVault> where TVault : MongoVault
    {
        public void Configure(IVaultBuilder<TVault> vault) => counter.Count++;
    }

    public sealed class ApplyCounter
    {
        public int Count { get; set; }
    }

    /// <summary>A default configuration that tries to skip the defaults, which only a vault's own configuration may.</summary>
    public sealed class SkipAllDefault<TVault> : IVaultConfiguration<TVault> where TVault : MongoVault
    {
        public void Configure(IVaultBuilder<TVault> vault) => vault.SkipAllDefaultConfigurations();
    }

    /// <summary>A default configuration that tries to skip another default.</summary>
    public sealed class SkipOneDefault<TVault> : IVaultConfiguration<TVault> where TVault : MongoVault
    {
        public void Configure(IVaultBuilder<TVault> vault) => vault.SkipDefaultConfiguration<PrefixDefault<TVault>>();
    }

    /// <summary>A default that filters orders, to show where default filters go.</summary>
    public sealed class TotalFilterDefault<TVault> : IVaultConfiguration<TVault> where TVault : MongoVault
    {
        public void Configure(IVaultBuilder<TVault> vault) => vault.QueryFilter<Order>(x => x.Total > 1);
    }

    public sealed class ClosedConfiguration : IVaultConfiguration<ShopVault>
    {
        public void Configure(IVaultBuilder<ShopVault> vault)
        {
        }
    }

    public abstract class AbstractConfiguration<TVault> : IVaultConfiguration<TVault> where TVault : MongoVault
    {
        public void Configure(IVaultBuilder<TVault> vault)
        {
        }
    }

    public sealed class TwoParameterConfiguration<TVault, TOther> : IVaultConfiguration<TVault> where TVault : MongoVault
    {
        public void Configure(IVaultBuilder<TVault> vault)
        {
        }
    }

    /// <summary>Implements interfaces, but not <see cref="IVaultConfiguration{TVault}"/>.</summary>
    public sealed class NotAConfiguration<TVault> : IEquatable<TVault>, IDisposable
    {
        public bool Equals(TVault? other) => false;

        public void Dispose()
        {
        }
    }

    /// <summary>Generic, but configures one fixed vault rather than its type parameter.</summary>
    public sealed class FixedVaultConfiguration<TOther> : IVaultConfiguration<ShopVault>
    {
        public void Configure(IVaultBuilder<ShopVault> vault)
        {
        }
    }

    /// <summary>Names the orders collection.</summary>
    public sealed class OrdersName(string name) : IVaultConfiguration<ShopVault>
    {
        public void Configure(IVaultBuilder<ShopVault> vault) => vault.Collection(x => x.Orders, orders => orders.Name(name));
    }

    public sealed record NamingOptions(string Prefix);

    /// <summary>A configuration created from DI, so it can depend on options.</summary>
    public sealed class NamingConfiguration(NamingOptions options) : IVaultConfiguration<ShopVault>
    {
        public void Configure(IVaultBuilder<ShopVault> vault) => vault.ForEachCollection(new Prefix(options.Prefix));
    }

    /// <summary>A configuration that applies another.</summary>
    public sealed class NestingConfiguration : IVaultConfiguration<ShopVault>
    {
        public void Configure(IVaultBuilder<ShopVault> vault) => vault.UseConfiguration<NamingConfiguration>();
    }

    /// <summary>The customer of the request: a scoped service a per-query filter reads.</summary>
    public sealed class CurrentCustomer
    {
        public string Name { get; set; } = "";
    }

    /// <summary>Hides deleted documents; switched off with its key.</summary>
    public sealed class ActiveOnlyFeature : IVaultFeature
    {
        public static FeatureKey Key { get; } = new("active-only");

        public void Configure<TVault>(IVaultBuilder<TVault> vault) where TVault : MongoVault =>
            vault.QueryFilter<ISoftDeletable>(x => !x.IsDeleted);
    }

    public sealed record TenantOptions(string Tenant);

    /// <summary>A feature created from DI, limiting reads to the configured tenant.</summary>
    public sealed class TenantFeature(TenantOptions options) : IVaultFeature
    {
        public static FeatureKey Key { get; } = new("tenant");

        public void Configure<TVault>(IVaultBuilder<TVault> vault) where TVault : MongoVault =>
            vault.QueryFilter<ITenantOwned>(x => x.TenantId == options.Tenant);
    }

    /// <summary>A feature that adds another feature, whose filters belong to the inner one.</summary>
    public sealed class OuterFeature : IVaultFeature
    {
        public static FeatureKey Key { get; } = new("outer");

        public void Configure<TVault>(IVaultBuilder<TVault> vault) where TVault : MongoVault => vault
            .AddFeature(new ActiveOnlyFeature())
            .QueryFilter<Order>(x => x.Total > 100);
    }

    /// <summary>A feature with the default key, which is rejected.</summary>
    public sealed class UnnamedFeature : IVaultFeature
    {
        public static FeatureKey Key => default;

        public void Configure<TVault>(IVaultBuilder<TVault> vault) where TVault : MongoVault
        {
        }
    }

    /// <summary>A feature whose configuration fails.</summary>
    public sealed class FailingFeature : IVaultFeature
    {
        public static FeatureKey Key { get; } = new("failing");

        public void Configure<TVault>(IVaultBuilder<TVault> vault) where TVault : MongoVault =>
            throw new InvalidOperationException("The feature can't be configured.");
    }

    /// <summary>Does nothing; for registering an interceptor.</summary>
    public sealed class NoopInterceptor : VaultInterceptor;
}
