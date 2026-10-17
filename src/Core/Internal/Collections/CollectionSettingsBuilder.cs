using System.Linq.Expressions;
using MongoDB.Driver;

namespace MongoFlow;

/// <summary>
/// A collection's declared settings, each layered: a vault's own setting wins over a default configuration's, whichever
/// runs last.
/// </summary>
internal sealed class CollectionSettingsBuilder<TDocument>(VaultModelBuilderBase vault) : ICollectionSettingsBuilder<TDocument>
{
    private readonly Layered<Collation> _collation = new();
    private readonly Layered<(FilterDefinition<TDocument> Rule, DocumentValidationLevel Level, DocumentValidationAction Action)>
        _validator = new();
    private readonly Layered<bool> _changeStreamImages = new();
    private readonly Layered<TimeSpan?> _clustered = new();

    public ICollectionSettingsBuilder<TDocument> Collation(Collation collation)
    {
        ArgumentNullException.ThrowIfNull(collation);

        _collation.Set(vault.Layer, collation);
        return this;
    }

    public ICollectionSettingsBuilder<TDocument> Validator(Expression<Func<TDocument, bool>> rule,
        DocumentValidationLevel level = DocumentValidationLevel.Strict,
        DocumentValidationAction action = DocumentValidationAction.Error)
    {
        ArgumentNullException.ThrowIfNull(rule);

        return Validator(Builders<TDocument>.Filter.Where(rule), level, action);
    }

    public ICollectionSettingsBuilder<TDocument> Validator(FilterDefinition<TDocument> rule,
        DocumentValidationLevel level = DocumentValidationLevel.Strict,
        DocumentValidationAction action = DocumentValidationAction.Error)
    {
        ArgumentNullException.ThrowIfNull(rule);

        _validator.Set(vault.Layer, (rule, level, action));
        return this;
    }

    public ICollectionSettingsBuilder<TDocument> ChangeStreamPreAndPostImages(bool enabled = true)
    {
        _changeStreamImages.Set(vault.Layer, enabled);
        return this;
    }

    public ICollectionSettingsBuilder<TDocument> Clustered(TimeSpan? expireAfter = null)
    {
        if (expireAfter is { } expiry && (expiry < TimeSpan.Zero || expiry.Ticks % TimeSpan.TicksPerSecond != 0))
        {
            throw new ArgumentOutOfRangeException(nameof(expireAfter), expireAfter, "An expiry is whole seconds, zero or more.");
        }

        _clustered.Set(vault.Layer, expireAfter);
        return this;
    }

    /// <summary>The settings rendered with <paramref name="args"/>, or <see langword="null"/> when none is declared.</summary>
    public CollectionSettings? Build(RenderArgs<TDocument> args)
    {
        var hasCollation = _collation.TryGet(out var collation);
        var hasValidator = _validator.TryGet(out var validator);
        var hasImages = _changeStreamImages.TryGet(out var images);
        var clustered = _clustered.TryGet(out var expireAfter);

        if (!hasCollation && !hasValidator && !hasImages && !clustered)
        {
            return null;
        }

        return new CollectionSettings(collation,
            hasValidator ? validator.Rule.Render(args) : null,
            validator.Level,
            validator.Action,
            hasImages ? images : null,
            clustered,
            expireAfter);
    }
}
