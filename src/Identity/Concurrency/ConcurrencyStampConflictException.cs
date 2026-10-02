namespace MongoFlow.Identity;

/// <summary>
/// A user or role was written with a <c>ConcurrencyStamp</c> the stored one no longer has: another request changed or
/// deleted it since it was read. The stores turn it into Identity's <c>ConcurrencyFailure</c>.
/// </summary>
internal sealed class ConcurrencyStampConflictException(Type documentType)
    : Exception($"The {documentType.Name} was changed or deleted since it was read.");
