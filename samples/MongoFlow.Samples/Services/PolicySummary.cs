using MongoFlow.Samples.Domain;

namespace MongoFlow.Samples.Services;

public sealed record PolicySummary(string PolicyNumber, PolicyStatus Status, decimal Premium);
