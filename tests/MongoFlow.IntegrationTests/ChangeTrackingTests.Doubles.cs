using System.Buffers;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace MongoFlow.IntegrationTests;

// A customer with an embedded address, a field left out when null, a list, and fields stored but not mapped; an account
// with a concurrency token; a soft-deletable lead; a price with an element name no path can hold; a coupon keyed by a
// code it may lack; and interceptors that record writes and originals, fail once, dispose the vault mid-save, stamp
// updates, and reuse pooled memory.
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

    /// <summary>
    /// Records each operation's original in the hooks it's told to read in, or why it couldn't be read, and keeps the
    /// operations of each save.
    /// </summary>
    public sealed class OriginalRecorder(params string[] readIn) : VaultInterceptor
    {
        public List<object> Seen { get; } = [];

        public List<VaultOperation> Operations { get; } = [];

        public override ValueTask SavingAsync(SaveContext context, CancellationToken cancellationToken)
        {
            Operations.AddRange(context.Operations);
            return Record("Saving", context);
        }

        public override ValueTask SavedAsync(SaveContext context, CancellationToken cancellationToken) =>
            Record("Saved", context);

        public override ValueTask CommittedAsync(SaveContext context, CancellationToken cancellationToken) =>
            Record("Committed", context);

        public override ValueTask FailedAsync(SaveContext context,
            Exception exception,
            CancellationToken cancellationToken) =>
            Record("Failed", context);

        private ValueTask Record(string hook,
            SaveContext context)
        {
            if (readIn.Contains(hook))
            {
                foreach (var operation in context.Operations)
                {
                    Seen.Add(new { Hook = hook, operation.Kind, Original = Read(operation) });
                }
            }

            return ValueTask.CompletedTask;
        }

        private static object Read(VaultOperation operation)
        {
            try
            {
                return (object?)operation.Original ?? "none";
            }
            catch (InvalidOperationException exception)
            {
                return exception.Message;
            }
        }
    }

    /// <summary>
    /// Rents pooled arrays of every size when a save commits and writes over them, as other requests would with arrays
    /// given back to the pool: a few of each, since the pool hands back the latest one given back on the thread first.
    /// </summary>
    public sealed class PoolReuser : VaultInterceptor
    {
        public override ValueTask CommittedAsync(SaveContext context, CancellationToken cancellationToken)
        {
            for (var size = 16; size <= 1 << 16; size *= 2)
            {
                var arrays = new byte[8][];
                for (var i = 0; i < arrays.Length; i++)
                {
                    arrays[i] = ArrayPool<byte>.Shared.Rent(size);
                    arrays[i].AsSpan().Fill(0xFF);
                }

                foreach (var array in arrays)
                {
                    ArrayPool<byte>.Shared.Return(array);
                }
            }

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// Notes on each customer update that it was audited, through <see cref="UpdateOperation{TDocument}.WithUpdate"/>.
    /// </summary>
    public sealed class AuditStamper : VaultInterceptor
    {
        public override ValueTask SavingAsync(SaveContext context, CancellationToken cancellationToken)
        {
            foreach (var operation in context.Operations)
            {
                if (operation is UpdateOperation<Customer> update)
                {
                    context.Replace(update, update.WithUpdate(Builders<Customer>.Update.Combine(update.Update,
                        Builders<Customer>.Update.Set(x => x.Note, "audited"))));
                }
            }

            return ValueTask.CompletedTask;
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
