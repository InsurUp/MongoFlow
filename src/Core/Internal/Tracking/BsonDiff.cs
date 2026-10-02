using System.Buffers.Binary;
using System.Text;
using Prest;

namespace MongoFlow;

/// <summary>
/// Compares two serializations of a document in raw BSON, element by element, for the paths an update has to set or
/// unset. Embedded documents are compared field by field; any other value, an array included, changes as a whole.
/// </summary>
internal static class BsonDiff
{
    private const byte EmbeddedDocument = 0x03;

    /// <summary>
    /// What changed from <paramref name="before"/> to <paramref name="after"/>, or <see langword="null"/> when nothing did.
    /// Fields that only moved don't count.
    /// </summary>
    public static BsonChanges? Compare(ReadOnlySpan<byte> before,
        ReadOnlySpan<byte> after)
    {
        if (before.SequenceEqual(after))
        {
            return null;
        }

        var changes = new BsonChanges();
        changes.NeedsReplace = !CompareDocuments(before, after, prefix: null, changes);

        return changes.Count == 0 ? null : changes;
    }

    /// <summary>Adds the paths that differ between two documents, under <paramref name="prefix"/>.</summary>
    /// <returns>Whether every element that changed has a name a path can hold.</returns>
    private static bool CompareDocuments(ReadOnlySpan<byte> before,
        ReadOnlySpan<byte> after,
        string? prefix,
        BsonChanges changes)
    {
        using var old = Elements(before);
        var addressable = true;
        var index = 0;

        // Past the document's length, up to the null byte that ends it.
        for (var position = 4; after[position] != 0; index++)
        {
            var element = ElementAt(after, position);
            position = element.End;

            var name = element.Name(after);
            var match = Find(old, before, name, index);

            if (match < 0)
            {
                changes.Set(Path(prefix, name));
                addressable &= IsPathName(name);
                continue;
            }

            ref var previous = ref old[match];
            previous.Matched = true;

            var previousValue = previous.Value(before);
            var value = element.Value(after);
            if (previous.Type == element.Type && previousValue.SequenceEqual(value))
            {
                continue;
            }

            var path = Path(prefix, name);
            if (previous.Type == EmbeddedDocument && element.Type == EmbeddedDocument && IsPathName(name))
            {
                var found = changes.Count;
                if (CompareDocuments(previousValue, value, path, changes))
                {
                    continue;
                }

                // A field inside has a name a path can't hold, so the document is set whole.
                changes.Truncate(found);
            }

            changes.Set(path);
            addressable &= IsPathName(name);
        }

        foreach (ref readonly var removed in old.Span)
        {
            if (!removed.Matched)
            {
                var name = removed.Name(before);
                changes.Unset(Path(prefix, name));
                addressable &= IsPathName(name);
            }
        }

        return addressable;
    }

    private static PooledList<BsonElementRange> Elements(ReadOnlySpan<byte> document)
    {
        var elements = new PooledList<BsonElementRange>();
        for (var position = 4; document[position] != 0;)
        {
            var element = ElementAt(document, position);
            elements.Add(element);
            position = element.End;
        }

        return elements;
    }

    /// <summary>
    /// The unmatched element of <paramref name="before"/> named <paramref name="name"/>, looked for first at
    /// <paramref name="index"/>, since a document's fields are mostly serialized in the same order; or -1.
    /// </summary>
    private static int Find(PooledList<BsonElementRange> elements,
        ReadOnlySpan<byte> before,
        ReadOnlySpan<byte> name,
        int index)
    {
        if (index < elements.Count && !elements[index].Matched && elements[index].Name(before).SequenceEqual(name))
        {
            return index;
        }

        for (var i = 0; i < elements.Count; i++)
        {
            if (!elements[i].Matched && elements[i].Name(before).SequenceEqual(name))
            {
                return i;
            }
        }

        return -1;
    }

    private static BsonElementRange ElementAt(ReadOnlySpan<byte> document,
        int position)
    {
        var type = document[position];
        var nameLength = document[(position + 1)..].IndexOf((byte)0);
        var valueStart = position + 1 + nameLength + 1;

        return new BsonElementRange(position + 1, nameLength, type, valueStart,
            valueStart + ValueLength(type, document[valueStart..]));
    }

    private static int ValueLength(byte type,
        ReadOnlySpan<byte> value) =>
        type switch
        {
            // Double, UTC date and time, timestamp, 64-bit integer.
            0x01 or 0x09 or 0x11 or 0x12 => 8,

            // String, JavaScript, symbol: a length, then that many bytes.
            0x02 or 0x0D or 0x0E => 4 + BinaryPrimitives.ReadInt32LittleEndian(value),

            // Document, array, JavaScript with scope: a length that counts itself.
            0x03 or 0x04 or 0x0F => BinaryPrimitives.ReadInt32LittleEndian(value),

            // Binary: a length, a subtype, then that many bytes.
            0x05 => 5 + BinaryPrimitives.ReadInt32LittleEndian(value),

            // Undefined, null, max key, min key.
            0x06 or 0x0A or 0x7F or 0xFF => 0,

            0x07 => 12, // ObjectId
            0x08 => 1, // Boolean
            0x0B => RegularExpressionLength(value),
            0x10 => 4, // 32-bit integer
            0x13 => 16, // Decimal128
            _ => throw new InvalidOperationException($"BSON type 0x{type:X2} isn't one the driver serializes documents with.")
        };

    // A pattern and options, each ending with a null byte.
    private static int RegularExpressionLength(ReadOnlySpan<byte> value)
    {
        var pattern = value.IndexOf((byte)0) + 1;

        return pattern + value[pattern..].IndexOf((byte)0) + 1;
    }

    /// <summary>Whether an update can address an element by <paramref name="name"/> in a dotted path.</summary>
    private static bool IsPathName(ReadOnlySpan<byte> name) =>
        name.Length > 0 && name[0] != (byte)'$' && name.IndexOf((byte)'.') < 0;

    private static string Path(string? prefix,
        ReadOnlySpan<byte> name)
    {
        var decoded = Encoding.UTF8.GetString(name);

        return prefix is null ? decoded : $"{prefix}.{decoded}";
    }
}
