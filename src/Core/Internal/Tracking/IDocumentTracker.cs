namespace MongoFlow;

/// <summary>Tracks the documents a read returns, for the wrappers around the driver's queries and cursors.</summary>
internal interface IDocumentTracker<in TDocument>
{
    void Track(TDocument document);
}
