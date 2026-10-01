namespace MongoFlow.IntegrationTests;

public partial class SaveTests
{
    /// <summary>Records each operation's kind and result, once the bulk write ran.</summary>
    public sealed class ResultRecorder : VaultInterceptor
    {
        public List<object> Results { get; } = [];

        public override ValueTask SavedAsync(SaveContext context, CancellationToken cancellationToken)
        {
            foreach (var operation in context.Operations)
            {
                Results.Add(new { operation.Kind, operation.IsSetBased, operation.Result });
            }

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Holds a save in its interceptor until the test lets it go.</summary>
    public sealed class Gate : VaultInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask SavingAsync(SaveContext context, CancellationToken cancellationToken)
        {
            Entered.SetResult();
            await Release.Task.WaitAsync(cancellationToken);
        }
    }
}
