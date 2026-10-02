namespace MongoFlow;

/// <summary>What a <see cref="VaultOperation"/> writes.</summary>
public enum OperationKind
{
    /// <summary>A new document.</summary>
    Insert,

    /// <summary>A whole document in place of the stored one.</summary>
    Replace,

    /// <summary>An update of a document, or of every document a filter matches.</summary>
    Update,

    /// <summary>A delete of a document, or of every document a filter matches.</summary>
    Delete
}
