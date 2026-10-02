using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow;

/// <summary>
/// An update that increments one more field, such as the concurrency token: <c>{ $inc: { Version: 1 } }</c> added to what
/// the update renders, or a last stage for a pipeline. <c>Builders&lt;T&gt;.Update.Combine</c> would allocate a list and
/// two documents for every write, merge the increment into the update's own document, which a
/// <see cref="BsonDocumentUpdateDefinition{TDocument}"/> shares with every write it's used for, and reject a pipeline.
/// </summary>
internal sealed class IncrementedUpdateDefinition<TDocument>(UpdateDefinition<TDocument> update,
    string field,
    BsonValue amount) : UpdateDefinition<TDocument>
{
    private const string Increment = "$inc";

    // Neither is changed in place: what the update renders may be its own.
    public override BsonValue Render(RenderArgs<TDocument> args) =>
        update.Render(args) switch
        {
            BsonArray pipeline => WithIncrementStage(pipeline),
            var operators => WithIncrement(operators.AsBsonDocument)
        };

    /// <summary>
    /// The pipeline with <c>{ $set: { Version: { $add: [{ $ifNull: ["$Version", 0] }, 1] } } }</c> last: a pipeline has no
    /// <c>$inc</c>, so the stage adds to the field, from 0 when it's missing, as <c>$inc</c> does.
    /// </summary>
    private BsonArray WithIncrementStage(BsonArray pipeline)
    {
        var stages = new BsonArray(pipeline.Count + 1);
        for (var i = 0; i < pipeline.Count; i++)
        {
            stages.Add(pipeline[i]);
        }

        var current = new BsonDocument("$ifNull", new BsonArray { "$" + field, 0 });
        stages.Add(new BsonDocument("$set", new BsonDocument(field, new BsonDocument("$add", new BsonArray { current, amount }))));

        return stages;
    }

    // Only $inc changes, so only it is copied below the top.
    private BsonDocument WithIncrement(BsonDocument rendered)
    {
        var incremented = new BsonDocument();
        BsonDocument? increments = null;

        for (var i = 0; i < rendered.ElementCount; i++)
        {
            var element = rendered.GetElement(i);
            if (element.Name == Increment)
            {
                increments = Copy(element.Value.AsBsonDocument);
                incremented.Add(Increment, increments);
            }
            else
            {
                incremented.Add(element);
            }
        }

        if (increments is null)
        {
            incremented.Add(Increment, new BsonDocument(field, amount));
        }
        else
        {
            increments[field] = amount;
        }

        return incremented;
    }

    private static BsonDocument Copy(BsonDocument document)
    {
        var copy = new BsonDocument();
        for (var i = 0; i < document.ElementCount; i++)
        {
            copy.Add(document.GetElement(i));
        }

        return copy;
    }
}
