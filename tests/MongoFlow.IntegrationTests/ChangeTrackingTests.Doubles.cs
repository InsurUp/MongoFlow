using System.Buffers;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace MongoFlow.IntegrationTests;

// A customer with an embedded address, a field left out when null, a list, and fields stored but not mapped; an account
// with a concurrency token; a soft-deletable lead; a price with an element name no path can hold; a coupon keyed by a
// code it may lack; an invoice holding events it doesn't store, and an outbox for them; interceptors that record writes,
// originals and tracked documents, fail once, dispose or reset the vault mid-save, stamp updates, reuse pooled memory,
// and store or queue events; and a feature that runs saves for events.
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

    public interface IRaisesEvents
    {
        List<string> Events { get; }
    }

    public sealed class Invoice : IRaisesEvents
    {
        public int Id { get; set; }

        public string Status { get; set; } = "";

        [BsonIgnore]
        public List<string> Events { get; } = [];
    }

    public sealed class OutboxEntry
    {
        public string Id { get; set; } = "";
    }

    public sealed class CrmVault : MongoVault
    {
        public IVaultCollection<Customer, int> Customers { get; init; } = null!;

        public IVaultCollection<Account, int> Accounts { get; init; } = null!;

        public IVaultCollection<Lead, int> Leads { get; init; } = null!;

        public IVaultCollection<Price, int> Prices { get; init; } = null!;

        public IVaultCollection<Coupon, string> Coupons { get; init; } = null!;

        public IVaultCollection<Invoice, int> Invoices { get; init; } = null!;

        public IVaultCollection<OutboxEntry> Outbox { get; init; } = null!;
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

    /// <summary>Records, in each hook, the documents tracked, and the operations and the result the save has.</summary>
    public sealed class TrackedRecorder : VaultInterceptor
    {
        public List<object> Seen { get; } = [];

        public override ValueTask SavingAsync(SaveContext context, CancellationToken cancellationToken) =>
            Record("Saving", context);

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
            Seen.Add(new
            {
                Hook = hook,
                Tracked = context.Tracked.Select(Describe).ToList(),
                Operations = context.Operations
                    .Select(operation => new { operation.Kind, Collection = operation.Namespace.CollectionName, operation.Key })
                    .ToList(),
                context.Result
            });

            return ValueTask.CompletedTask;
        }

        private static string Describe(object document) => document switch
        {
            Customer customer => $"Customer {customer.Id}",
            Invoice invoice => $"Invoice {invoice.Id}",
            _ => document.GetType().Name
        };
    }

    /// <summary>
    /// Stores the events of the documents a save writes or tracks in the outbox, through the save's session after the
    /// write, and clears them: a transactional outbox.
    /// </summary>
    public sealed class OutboxWriter : VaultInterceptor
    {
        public override async ValueTask SavedAsync(SaveContext context, CancellationToken cancellationToken)
        {
            var outbox = ((CrmVault)context.Vault).Outbox.MongoCollection;

            foreach (var document in RaisingEvents(context))
            {
                foreach (var @event in document.Events)
                {
                    await outbox.InsertOneAsync(context.Session, new OutboxEntry { Id = @event }, cancellationToken: cancellationToken);
                }

                document.Events.Clear();
            }
        }
    }

    /// <summary>Queues the events of the documents a save writes or tracks on the vault's outbox, and clears them.</summary>
    public sealed class OutboxQueuer : VaultInterceptor
    {
        public override ValueTask SavingAsync(SaveContext context, CancellationToken cancellationToken)
        {
            var outbox = ((CrmVault)context.Vault).Outbox;

            foreach (var document in RaisingEvents(context))
            {
                outbox.AddRange(document.Events.Select(@event => new OutboxEntry { Id = @event }));
                document.Events.Clear();
            }

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Runs saves for documents holding events, for its interceptor to see; switched off with its key.</summary>
    public sealed class EventsFeature(VaultInterceptor interceptor) : IVaultFeature
    {
        public static FeatureKey Key { get; } = new("events");

        public void Configure<TVault>(IVaultBuilder<TVault> vault) where TVault : MongoVault => vault
            .RunSaveWhen((IRaisesEvents x) => x.Events.Count > 0)
            .AddInterceptor(interceptor);
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

    /// <summary>Resets the vault while its save runs, as code starting the unit of work over too early would.</summary>
    public sealed class ResettingTheVault : VaultInterceptor
    {
        public override ValueTask SavingAsync(SaveContext context, CancellationToken cancellationToken)
        {
            context.Vault.Reset();
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>The documents a save writes or tracks that hold events, each once.</summary>
    private static IEnumerable<IRaisesEvents> RaisingEvents(SaveContext context) =>
        context.Operations
            .Select(operation => operation.Document)
            .Concat(context.Tracked)
            .OfType<IRaisesEvents>()
            .Distinct<IRaisesEvents>(ReferenceEqualityComparer.Instance);
}
