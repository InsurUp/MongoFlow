using MongoFlow.Samples.Domain;

namespace MongoFlow.Samples.Services;

public sealed record PremiumByStatus(PolicyStatus Status, decimal Total);
