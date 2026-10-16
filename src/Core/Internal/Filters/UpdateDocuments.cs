using MongoDB.Bson;
using MongoDB.Driver;

namespace MongoFlow;

/// <summary>Updates given as BSON: a document of update operators, or an array of pipeline stages.</summary>
internal static class UpdateDocuments
{
    /// <summary><paramref name="update"/> as an update definition of <typeparamref name="TDocument"/>.</summary>
    /// <exception cref="ArgumentException">It's neither update operators nor pipeline stages.</exception>
    public static UpdateDefinition<TDocument> Of<TDocument>(BsonValue update)
    {
        ArgumentNullException.ThrowIfNull(update);

        return update switch
        {
            BsonDocument operators when IsOperators(operators) => new BsonDocumentUpdateDefinition<TDocument>(operators),
            BsonArray stages when IsPipeline(stages) => new PipelineUpdateDefinition<TDocument>(
                new BsonDocumentStagePipelineDefinition<TDocument, TDocument>(stages.Select(stage => stage.AsBsonDocument))),
            _ => throw new ArgumentException(
                $"An update is a document of update operators, such as {{ $set: {{ Total: 20 }} }}, or an array of " +
                $"pipeline stages, such as [{{ $set: {{ Total: 20 }} }}], but it is {update}.",
                nameof(update))
        };
    }

    /// <summary>Whether <paramref name="update"/> is update operators, such as <c>{ $set: { Total: 20 } }</c>.</summary>
    private static bool IsOperators(BsonDocument update)
    {
        if (update.ElementCount == 0)
        {
            return false;
        }

        for (var i = 0; i < update.ElementCount; i++)
        {
            if (!update.GetElement(i).Name.StartsWith('$'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether <paramref name="update"/> is pipeline stages, each one stage such as <c>{ $set: { Total: 20 } }</c>.</summary>
    private static bool IsPipeline(BsonArray update) =>
        update.Count > 0 &&
        update.All(stage => stage is BsonDocument { ElementCount: 1 } document && document.GetElement(0).Name.StartsWith('$'));
}
