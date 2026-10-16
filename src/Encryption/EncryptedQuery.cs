using System.Globalization;

namespace MongoFlow.Encryption;

/// <summary>
/// The queries an encrypted member allows, given to <c>Encrypt</c>. A member encrypted without one is encrypted, and
/// can't be queried. Set when the encrypted collection is created, and fixed from then on: MongoDB can't change it.
/// </summary>
public sealed class EncryptedQuery
{
    /// <summary>The contention factor MongoDB uses unless told otherwise.</summary>
    public const long DefaultContention = 8;

    private EncryptedQuery(string type,
        long contention)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(contention);

        Type = type;
        Contention = contention;
    }

    /// <summary>
    /// Equality: <c>==</c>, <c>!=</c>, <c>Contains</c> on a list of values, and <c>null</c> (an encrypted member that is null
    /// is stored as absent), with MongoDB's default contention factor, 8.
    /// </summary>
    public static EncryptedQuery Equality { get; } = new("equality", DefaultContention);

    /// <summary>What the server is told to index: <c>equality</c> or <c>range</c>.</summary>
    internal string Type { get; }

    /// <summary>
    /// The contention factor: how many index entries one value spreads over. A higher one guards frequent values
    /// better, and makes queries slower.
    /// </summary>
    internal long Contention { get; }

    internal object? Min { get; private init; }

    internal object? Max { get; private init; }

    internal int? Precision { get; private init; }

    internal int Sparsity { get; private init; }

    internal int TrimFactor { get; private init; }

    /// <summary>Equality, as <see cref="Equality"/>, with a contention factor of its own.</summary>
    /// <param name="contention">How many index entries one value spreads over; MongoDB's default is 8.</param>
    public static EncryptedQuery EqualityWith(long contention) => new("equality", contention);

    /// <summary>
    /// Ranges: <c>&lt;</c>, <c>&lt;=</c>, <c>&gt;</c>, <c>&gt;=</c> and equality, between <paramref name="min"/> and
    /// <paramref name="max"/>, for an <c>int</c>, <c>long</c>, <c>DateTime</c>, <c>double</c> or <c>decimal</c> member. A
    /// value outside the bounds can't be written or compared.
    /// </summary>
    /// <param name="min">The smallest value, of the member's type.</param>
    /// <param name="max">The largest value, of the member's type.</param>
    /// <param name="precision">The digits after the point kept, which a <c>double</c> or <c>decimal</c> member needs.</param>
    /// <param name="sparsity">How sparsely the server indexes, from 1 to 4: higher is smaller, and slower to query.</param>
    /// <param name="trimFactor">How many of the index's top levels are left out; higher is faster to write.</param>
    /// <param name="contention">How many index entries one value spreads over; MongoDB's default is 8.</param>
    public static EncryptedQuery Range<T>(T min,
        T max,
        int? precision = null,
        int sparsity = 2,
        int trimFactor = 6,
        long contention = DefaultContention)
        where T : struct, IComparable<T>
    {
        if (min.CompareTo(max) >= 0)
        {
            throw new ArgumentException($"A range's min, {min}, has to be below its max, {max}.", nameof(min));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(precision ?? 0, nameof(precision));
        ArgumentOutOfRangeException.ThrowIfLessThan(sparsity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sparsity, 4);
        ArgumentOutOfRangeException.ThrowIfNegative(trimFactor);

        return new EncryptedQuery("range", contention)
        {
            Min = min,
            Max = max,
            Precision = precision,
            Sparsity = sparsity,
            TrimFactor = trimFactor
        };
    }

    /// <inheritdoc/>
    public override string ToString() =>
        Type == "equality"
            ? $"equality (contention {Contention})"
            : $"range from {Format(Min)} to {Format(Max)} (contention {Contention})";

    // The same on every machine, whatever its culture.
    private static string? Format(object? bound) =>
        bound switch
        {
            DateTime date => date.ToString("O", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => bound?.ToString()
        };
}
