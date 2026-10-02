using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace MongoFlow.IntegrationTests;

// A customer with an embedded address, a field left out when null, a list, and fields stored but not mapped; an account
// with a concurrency token; a soft-deletable lead; a price with an element name no path can hold; a coupon keyed by a
// code it may lack; and interceptors that record writes, fail once, and dispose the vault mid-save.
public partial class ChangeTrackingTests
{
    public interface IVersioned
    {
        int Version { get; set; }
    }

    public interface ISoftDeletable
    {
        bool IsDeleted { get; set; }
    }

    [BsonIgnoreExtraElements]
    public sealed class Customer
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";

        public Address Address { get; set; } = new();

        [BsonIgnoreIfNull]
        public string? Note { get; set; }

        public List<string> Tags { get; set; } = [];
    }

    public sealed class Address
    {
        public string City { get; set; } = "";

        public string Zip { get; set; } = "";
    }

    public sealed class Account : IVersioned
    {
        public int Id { get; set; }

        public decimal Balance { get; set; }

        public int Version { get; set; }
    }

    public sealed class Lead : ISoftDeletable
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";

        public bool IsDeleted { get; set; }
    }

    public sealed class Price
    {
        public int Id { get; set; }

        [BsonElement("v1.2")]
        public decimal Amount { get; set; }
    }

    public sealed class Coupon
    {
        public ObjectId Id { get; set; }

        public string? Code { get; set; }

        public int Percent { get; set; }
    }

    public sealed class CrmVault : MongoVault
    {
        public IVaultCollection<Customer, int> Customers { get; init; } = null!;

        public IVaultCollection<Account, int> Accounts { get; init; } = null!;

        public IVaultCollection<Lead, int> Leads { get; init; } = null!;

        public IVaultCollection<Price, int> Prices { get; init; } = null!;

        public IVaultCollection<Coupon, string> Coupons { get; init; } = null!;
    }

    /// <summary>Records each write a save sends, as the interceptors before the concurrency token see it.</summary>
    public sealed class WriteRecorder : VaultInterceptor
    {
        public List<object> Writes { get; } = [];

        public override ValueTask SavingAsync(SaveContext context, CancellationToken cancellationToken)
        {
            foreach (var operation in context.Operations)
            {
                Writes.Add(operation switch
                {
                    UpdateOperation<Customer> update => new { update.Kind, update.Key, Update = Render(update.Update) },
                    UpdateOperation<Account> update => new { update.Kind, update.Key, Update = Render(update.Update) },
                    UpdateOperation<Lead> update => new { update.Kind, update.Key, Update = Render(update.Update) },
                    ReplaceOperation<Customer> replace => new { replace.Kind, replace.Key },
                    ReplaceOperation<Price> replace => new { replace.Kind, replace.Key },
                    ReplaceOperation<Account> replace => new { replace.Kind, replace.Key },
                    DeleteOperation<Customer> delete => new { delete.Kind, delete.Key },
                    DeleteOperation<Account> delete => new { delete.Kind, delete.Key },
                    _ => new { operation.Kind }
                });
            }

            return ValueTask.CompletedTask;
        }

        private static BsonValue Render<TDocument>(UpdateDefinition<TDocument> update) =>
            update.Render(new RenderArgs<TDocument>(BsonSerializer.LookupSerializer<TDocument>(), BsonSerializer.SerializerRegistry));
    }

    /// <summary>Fails the first save after its write, and lets the ones after it through.</summary>
    public sealed class FailingOnce : VaultInterceptor
    {
        private bool _failed;

        public override ValueTask SavedAsync(SaveContext context, CancellationToken cancellationToken)
        {
            if (_failed)
            {
                return ValueTask.CompletedTask;
            }

            _failed = true;
            throw new InvalidOperationException("The first save failed after its write.");
        }
    }

    /// <summary>Disposes the vault while its save runs, as a scope ending under a save would.</summary>
    public sealed class DisposingTheVault : VaultInterceptor
    {
        public override ValueTask SavedAsync(SaveContext context, CancellationToken cancellationToken)
        {
            ((MongoVault)context.Vault).Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
