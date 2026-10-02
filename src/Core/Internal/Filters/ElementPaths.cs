namespace MongoFlow;

/// <summary>Dotted element paths, as updates and filters name fields, such as <c>Address.City</c>.</summary>
internal static class ElementPaths
{
    /// <summary>
    /// Whether a write to <paramref name="path"/> reaches <paramref name="field"/>: the field itself, a field inside it, or
    /// a document that holds it.
    /// </summary>
    public static bool Reaches(string path,
        string field) =>
        path == field || IsInside(path, field) || IsInside(field, path);

    private static bool IsInside(string path,
        string document) =>
        path.Length > document.Length && path[document.Length] == '.' && path.StartsWith(document, StringComparison.Ordinal);
}
