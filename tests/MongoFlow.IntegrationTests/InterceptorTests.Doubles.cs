using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace MongoFlow.IntegrationTests;

// Interceptors that record what they see, change the save, or misuse it.
public partial class InterceptorTests
{
    /// <summary>A recorder created from DI, as interceptors registered by type are.</summary>
    public sealed class ScopedRecorder(HookLog log) : RecordingInterceptor("scoped", log);

    /// <summary>A default configuration that adds a recorder, to show where defaults' interceptors run.</summary>
    public sealed class RecordingDefault<TVault>(HookLog log) : IVaultConfiguration<TVault> where TVault : MongoVault
    {
        public void Configure(IVaultBuilder<TVault> vault) => vault.AddInterceptor(new RecordingInterceptor("default", log));
    }

    public enum Hook
    {
        Saving,
        Saved,
        Committed,
        Failed
    }

    public sealed class InterceptorException(string message) : Exception(message);

    /// <summary>Throws from one hook.</summary>
    public sealed class ThrowingInterceptor(Hook hook) : VaultInterceptor
    {
        public override ValueTask SavingAsync(SaveContext context, CancellationToken cancellationToken) => Throw(Hook.Saving);

        public override ValueTask SavedAsync(SaveContext context, CancellationToken cancellationToken) => Throw(Hook.Saved);

        public override ValueTask CommittedAsync(SaveContext context, CancellationToken cancellationToken) => Throw(Hook.Committed);

        public override ValueTask FailedAsync(SaveContext context, Exception exception, CancellationToken cancellationToken) =>
            Throw(Hook.Failed);

        private ValueTask Throw(Hook current) =>
            current == hook ? throw new InterceptorException($"{hook} failed.") : ValueTask.CompletedTask;
    }

    /// <summary>Saves the vault it intercepts, from inside that vault's save.</summary>
    public sealed class SavingTheVault : VaultInterceptor
    {
        public override async ValueTask SavingAsync(SaveContext context, CancellationToken cancellationToken) =>
            await context.Vault.SaveAsync(cancellationToken);
    }

    /// <summary>A request's identity: a scoped service.</summary>
    public sealed class RequestId;

    /// <summary>The interceptors created so far.</summary>
    public sealed class CreatedInterceptors
    {
        public List<InstanceRecorder> Instances { get; } = [];
    }

    /// <summary>Records its own creation, with the request it was created for.</summary>
    public sealed class InstanceRecorder : VaultInterceptor
    {
        public InstanceRecorder(CreatedInterceptors created,
            RequestId request)
        {
            Request = request;
            created.Instances.Add(this);
        }

        public RequestId Request { get; }
    }

    /// <summary>Puts an item in the save for the interceptors after it.</summary>
    public sealed class ItemWriter : VaultInterceptor
    {
        public const string Key = "writer";

        public override ValueTask SavingAsync(SaveContext context, CancellationToken cancellationToken)
        {
            context.Items[Key] = "written while saving";
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Looks at what the context says, before and after the write.</summary>
    public sealed class ContextProbe : VaultInterceptor
    {
        public IMongoVault? Vault { get; private set; }

        public IServiceProvider? Services { get; private set; }

        public bool InTransaction { get; private set; }

        public bool ResultWhileSaving { get; private set; }

        public SaveResult? ResultOnceSaved { get; private set; }

        public object? Item { get; private set; }

        public override ValueTask SavingAsync(SaveContext context, CancellationToken cancellationToken)
        {
            Vault = context.Vault;
            Services = context.Services;
            InTransaction = context.Session.IsInTransaction;
            ResultWhileSaving = context.Result is not null;
            Item = context.Items[ItemWriter.Key];

            return ValueTask.CompletedTask;
        }

        public override ValueTask SavedAsync(SaveContext context, CancellationToken cancellationToken)
        {
            ResultOnceSaved = context.Result;
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Adds a change of its own to every update of an order, going through them in queue order or in reverse.</summary>
    public sealed class UpdateStamper(string customer, bool reverse = false) : VaultInterceptor
    {
        public override ValueTask SavingAsync(SaveContext context, CancellationToken cancellationToken)
        {
            var updates = context.Operations.OfType<UpdateOperation<Order>>().ToList();
            if (reverse)
            {
                updates.Reverse();
            }

            foreach (var update in updates)
            {
                context.Replace(update, update.WithUpdate(Builders<Order>.Update.Combine(
                    update.Update,
                    Builders<Order>.Update.Set(x => x.Customer, customer))));
            }

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Drops every audit entry from the save.</summary>
    public sealed class AuditRemover : VaultInterceptor
    {
        public override ValueTask SavingAsync(SaveContext context, CancellationToken cancellationToken)
        {
            foreach (var insert in context.Operations.OfType<InsertOperation<AuditEntry>>())
            {
                context.Remove(insert);
            }

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Reads the operations twice, removes the second one, and reads them again.</summary>
    public sealed class SnapshotReader : VaultInterceptor
    {
        public object? Reads { get; private set; }

        public override ValueTask SavingAsync(SaveContext context, CancellationToken cancellationToken)
        {
            var first = context.Operations;
            var again = context.Operations;
            context.Remove(first[1]);
            var afterRemoving = context.Operations;

            Reads = new
            {
                AgainIsTheSame = ReferenceEquals(first, again),
                AfterRemovingIsTheSame = ReferenceEquals(first, afterRemoving),
                ReadOnly = first is ICollection<VaultOperation> { IsReadOnly: true },
                First = first.Count,
                AfterRemoving = afterRemoving.Count
            };

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Queues an audit entry on the vault while it's saved.</summary>
    public sealed class AuditWriter(string message) : VaultInterceptor
    {
        public override ValueTask SavingAsync(SaveContext context, CancellationToken cancellationToken)
        {
            ((ShopVault)context.Vault).Audit.Add(new AuditEntry { Message = message });
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Records the operations it sees, as their kind and document type.</summary>
    public sealed class OperationRecorder : VaultInterceptor
    {
        public List<string> Seen { get; } = [];

        public override ValueTask SavingAsync(SaveContext context, CancellationToken cancellationToken)
        {
            Seen.AddRange(context.Operations.Select(operation => $"{operation.Kind} {operation.Collection.DocumentType.Name}"));
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Describes each operation it sees by what an interceptor can read of it.</summary>
    public sealed class OperationDescriber : VaultInterceptor
    {
        private static readonly RenderArgs<Order> Render = new(BsonSerializer.LookupSerializer<Order>(), BsonSerializer.SerializerRegistry);

        public List<object> Described { get; } = [];

        public override ValueTask SavingAsync(SaveContext context, CancellationToken cancellationToken)
        {
            foreach (var operation in context.Operations)
            {
                Described.Add(operation switch
                {
                    ReplaceOperation<Order> replace => new { replace.Kind, replace.Key, Document = replace.Document?.Id },
                    UpdateOperation<Order> update => new
                    {
                        update.Kind,
                        update.Key,
                        Filter = update.Filter?.ToString(),
                        Document = update.Document?.Id,
                        Update = update.Update.Render(Render)
                    },
                    DeleteOperation<Order> delete => new { delete.Kind, delete.Key, Filter = delete.Filter?.ToString(), Document = delete.Document?.Id },
                    _ => new { operation.Kind, operation.Namespace.CollectionName, Document = ((Order?)operation.Document)?.Id }
                });
            }

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Adds a condition to every update of an order.</summary>
    public sealed class OrderCondition(FilterDefinition<Order> condition) : VaultInterceptor
    {
        public override ValueTask SavingAsync(SaveContext context, CancellationToken cancellationToken)
        {
            foreach (var update in context.Operations.OfType<UpdateOperation<Order>>())
            {
                update.AddCondition(condition);
            }

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Adds a condition to an insert, which can't take one.</summary>
    public sealed class InsertCondition : VaultInterceptor
    {
        public override ValueTask SavingAsync(SaveContext context, CancellationToken cancellationToken)
        {
            context.Operations.OfType<InsertOperation<Order>>().First().AddCondition(Builders<Order>.Filter.Empty);
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Records each order operation's condition as it's written, and its result once written.</summary>
    public sealed class ConditionRecorder : VaultInterceptor
    {
        public List<BsonDocument?> Conditions { get; } = [];

        public List<OperationResult?> Results { get; } = [];

        public override ValueTask SavingAsync(SaveContext context, CancellationToken cancellationToken)
        {
            Conditions.AddRange(context.Operations.OfType<VaultOperation<Order>>().Select(operation => operation.Condition));
            return ValueTask.CompletedTask;
        }

        public override ValueTask SavedAsync(SaveContext context, CancellationToken cancellationToken)
        {
            Results.AddRange(context.Operations.Select(operation => operation.Result));
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Records the exception each failed save hands its failure hooks.</summary>
    public sealed class FailureRecorder : VaultInterceptor
    {
        public List<Exception> Exceptions { get; } = [];

        public override ValueTask FailedAsync(SaveContext context, Exception exception, CancellationToken cancellationToken)
        {
            Exceptions.Add(exception);
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Records its disposal; registered by type, so MongoFlow creates it.</summary>
    public sealed class ScopedDisposable(HookLog log) : VaultInterceptor, IDisposable
    {
        public void Dispose() => log.Add("scoped disposed");
    }

    /// <summary>Records its disposal; registered as an instance, so the app owns it.</summary>
    public sealed class SharedDisposable(HookLog log) : VaultInterceptor, IDisposable
    {
        public void Dispose() => log.Add("shared disposed");
    }

    /// <summary>A feature whose interceptor records what it sees.</summary>
    public sealed class CountingFeature(OperationRecorder recorder) : IVaultFeature
    {
        public static FeatureKey Key { get; } = new("counting");

        public void Configure<TVault>(IVaultBuilder<TVault> vault) where TVault : MongoVault => vault.AddInterceptor(recorder);
    }

    /// <summary>Changes the save in a way it doesn't allow.</summary>
    public sealed class Misuse(Misuse.Kind kind) : VaultInterceptor
    {
        private VaultOperation? _fromTheLastSave;

        public enum Kind
        {
            ReplaceAcrossCollections,
            ReplaceFromAnotherSave,
            ReplaceNullOperation,
            ReplaceWithNull,
            RemoveNull,
            ReplaceOnceSaved,
            RemoveOnceSaved
        }

        public override ValueTask SavingAsync(SaveContext context, CancellationToken cancellationToken)
        {
            var operations = context.Operations;

            switch (kind)
            {
                case Kind.ReplaceAcrossCollections:
                    context.Replace(operations[0], operations[1]);
                    break;

                case Kind.ReplaceFromAnotherSave when _fromTheLastSave is null:
                    _fromTheLastSave = operations[0];
                    break;

                case Kind.ReplaceFromAnotherSave:
                    context.Replace(_fromTheLastSave, operations[0]);
                    break;

                case Kind.ReplaceNullOperation:
                    context.Replace(null!, operations[0]);
                    break;

                case Kind.ReplaceWithNull:
                    context.Replace(operations[0], null!);
                    break;

                case Kind.RemoveNull:
                    context.Remove(null!);
                    break;
            }

            return ValueTask.CompletedTask;
        }

        public override ValueTask SavedAsync(SaveContext context, CancellationToken cancellationToken)
        {
            var operations = context.Operations;

            switch (kind)
            {
                case Kind.ReplaceOnceSaved:
                    context.Replace(operations[0], operations[0]);
                    break;

                case Kind.RemoveOnceSaved:
                    context.Remove(operations[0]);
                    break;
            }

            return ValueTask.CompletedTask;
        }
    }
}
