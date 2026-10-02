using System.Buffers;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace MongoFlow;

/// <summary>
/// A document serialized to BSON, in an array rented from <see cref="ArrayPool{T}.Shared"/>. Whoever holds it gives the
/// array back, once.
/// </summary>
internal readonly struct BsonSnapshot(byte[] bytes,
    int length)
{
    public ReadOnlySpan<byte> Span => bytes.AsSpan(0, length);

    /// <summary>Serializes <paramref name="document"/> with <paramref name="serializer"/>, the collection's.</summary>
    public static BsonSnapshot Of<TDocument>(IBsonSerializer<TDocument> serializer,
        TDocument document)
    {
        // A document's length is written last, so the BSON is written to the driver's pooled chunks first, then copied.
        using var stream = new ByteBufferStream(new MultiChunkBuffer(BsonChunkPool.Default), ownsBuffer: true);
        using (var writer = new BsonBinaryWriter(stream))
        {
            serializer.Serialize(BsonSerializationContext.CreateRoot(writer), document);
        }

        var length = (int)stream.Length;
        var bytes = ArrayPool<byte>.Shared.Rent(length);
        stream.Buffer.GetBytes(0, bytes, 0, length);

        return new BsonSnapshot(bytes, length);
    }

    public BsonDocument ToBsonDocument()
    {
        using var stream = new ByteBufferStream(new ByteArrayBuffer(bytes, length, isReadOnly: true), ownsBuffer: true);
        using var reader = new BsonBinaryReader(stream);

        return BsonDocumentSerializer.Instance.Deserialize(BsonDeserializationContext.CreateRoot(reader));
    }

    /// <summary>Gives the array back. A <see langword="default"/> snapshot holds none.</summary>
    public void Return()
    {
        if (bytes is not null)
        {
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }
}
