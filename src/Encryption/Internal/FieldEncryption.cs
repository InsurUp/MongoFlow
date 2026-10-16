using MongoDB.Bson;

namespace MongoFlow.Encryption;

/// <summary>
/// Encrypts a collection's encrypted members in what it sends: documents, the values filters compare them with, and the
/// values updates set them to, with each field's data key.
/// </summary>
/// <remarks>
/// Filters and updates are copied before they change, and returned as they are when they compare or set no encrypted
/// member. A document is changed in place: it's serialized for the one write. Null can't be encrypted, so an encrypted
/// member that is null is stored as absent: left out of documents, unset by updates, and matched with
/// <c>$exists: false</c>.
/// </remarks>
internal sealed class FieldEncryption(string collection,
    IReadOnlyList<FieldSchema> fields,
    IReadOnlyDictionary<string, Guid> keys,
    IValueEncryptor encryptor)
{
    /// <summary>Encrypts the encrypted members' values in <paramref name="document"/>, in place.</summary>
    public async ValueTask<BsonDocument> DocumentAsync(BsonDocument document,
        CancellationToken cancellationToken)
    {
        foreach (var field in fields)
        {
            await StoreAtAsync(document, field, field.Segments, 0, cancellationToken);
        }

        return document;
    }

    /// <summary><paramref name="filter"/>, with the values it compares encrypted members with encrypted.</summary>
    /// <exception cref="NotSupportedException">It compares an encrypted member in a way the server can't.</exception>
    public async ValueTask<BsonDocument> FilterAsync(BsonDocument filter,
        CancellationToken cancellationToken)
    {
        List<BsonElement>? elements = null;
        List<BsonDocument>? clauses = null;

        for (var i = 0; i < filter.ElementCount; i++)
        {
            var element = filter.GetElement(i);
            BsonElement? replacement = element;
            BsonDocument? clause = null;

            switch (element.Name)
            {
                case "$and" or "$or" or "$nor" when element.Value is BsonArray list:
                    var encrypted = await ClausesAsync(list, cancellationToken);
                    replacement = ReferenceEquals(encrypted, list) ? element : new BsonElement(element.Name, encrypted);
                    break;

                case "$expr" when RefersToField(element.Value) is { } referred:
                    throw new NotSupportedException(
                        $"{referred.Path} in {collection} is encrypted, so a filter can't compare it in $expr: MongoFlow " +
                        "encrypts the values filters compare encrypted members with only in query operators.");

                case var name when name.StartsWith('$'):
                    break;

                case var name when FieldAt(name) is { } field:
                    (replacement, clause) = await ConditionAsync(field, element.Value, cancellationToken);
                    break;

                case var name when FieldReached(name) is { } reached:
                    throw new NotSupportedException(
                        $"A filter of {collection} compares {name}, which holds or is inside {reached.Path}, which is " +
                        "encrypted: compare the encrypted member itself.");
            }

            if (elements is null && (replacement != element || clause is not null))
            {
                elements = [.. Enumerable.Range(0, i).Select(filter.GetElement)];
            }

            if (replacement is { } kept)
            {
                elements?.Add(kept);
            }

            if (clause is not null)
            {
                (clauses ??= []).Add(clause);
            }
        }

        if (elements is null)
        {
            return filter;
        }

        var rest = new BsonDocument(elements);
        if (clauses is null)
        {
            return rest;
        }

        // A range compares through a clause of its own, which can't be a field of the filter beside an $and it may hold.
        var all = new BsonArray();
        if (rest.ElementCount > 0)
        {
            all.Add(rest);
        }

        all.AddRange(clauses);
        return all.Count == 1 ? (BsonDocument)all[0] : new BsonDocument("$and", all);
    }

    /// <summary><paramref name="update"/>, with the values it sets encrypted members to encrypted.</summary>
    /// <exception cref="NotSupportedException">It changes an encrypted member in a way the server can't.</exception>
    public async ValueTask<BsonValue> UpdateAsync(BsonValue update,
        CancellationToken cancellationToken)
    {
        if (update is not BsonDocument operators)
        {
            throw new NotSupportedException(
                $"{collection} has encrypted members, so it can't be updated with a pipeline: the server refuses pipeline " +
                "updates of an encrypted collection. Use update operators, such as $set.");
        }

        var changed = false;
        var encrypted = new BsonDocument();
        BsonDocument? unset = null;

        foreach (var element in operators)
        {
            var (name, value) = (element.Name, element.Value);
            switch (name)
            {
                case "$set" or "$setOnInsert" when value is BsonDocument set:
                    var (sets, unsets) = await SetAsync(set, cancellationToken);
                    changed |= !ReferenceEquals(sets, set) || unsets is not null;

                    if (sets.ElementCount > 0)
                    {
                        encrypted.Add(name, sets);
                    }

                    foreach (var path in unsets ?? [])
                    {
                        (unset ??= new BsonDocument()).Set(path, "");
                    }

                    break;

                case "$unset" when value is BsonDocument unsetting:
                    foreach (var each in unsetting)
                    {
                        (unset ??= new BsonDocument()).Set(each.Name, each.Value);
                    }

                    break;

                default:
                    foreach (var path in (value as BsonDocument)?.Names ?? [])
                    {
                        if ((FieldAt(path) ?? FieldReached(path)) is { } field)
                        {
                            var reaching = path == field.Path ? "" : $", which reaches {field.Path},";
                            throw new NotSupportedException(
                                $"An update of {collection} uses {name} on {path}{reaching} but {field.Path} is encrypted: the " +
                                "server changes an encrypted member only with $set and $unset.");
                        }
                    }

                    encrypted.Add(name, value);
                    break;
            }
        }

        if (unset is not null)
        {
            encrypted.Set("$unset", unset);
        }

        return changed ? encrypted : update;
    }

    private async ValueTask<BsonArray> ClausesAsync(BsonArray clauses,
        CancellationToken cancellationToken)
    {
        BsonArray? encrypted = null;

        for (var i = 0; i < clauses.Count; i++)
        {
            var clause = clauses[i];
            var each = clause is BsonDocument document ? await FilterAsync(document, cancellationToken) : clause;

            if (encrypted is null && !ReferenceEquals(each, clause))
            {
                encrypted = [.. clauses.Take(i)];
            }

            encrypted?.Add(each);
        }

        return encrypted ?? clauses;
    }

    /// <summary>
    /// How a filter compares <paramref name="field"/> with <paramref name="condition"/>: as an element of the filter, and as
    /// a clause joined with it, which a range needs.
    /// </summary>
    private async ValueTask<(BsonElement? Element, BsonDocument? Clause)> ConditionAsync(FieldSchema field,
        BsonValue condition,
        CancellationToken cancellationToken)
    {
        var operators = condition is BsonDocument { ElementCount: > 0 } document && document.GetElement(0).Name.StartsWith('$')
            ? document
            : new BsonDocument("$eq", condition);

        // Null is stored as absent.
        if (operators.Any(element => element.Name is "$eq" or "$ne" && element.Value.IsBsonNull))
        {
            operators = new BsonDocument(operators.Select(element => element.Name is "$eq" or "$ne" && element.Value.IsBsonNull
                ? new BsonElement("$exists", element.Name == "$ne")
                : element));
        }

        if (operators.Names.All(name => name == "$exists"))
        {
            return (new BsonElement(field.Path, operators), null);
        }

        if (field.Query is null)
        {
            throw new NotSupportedException(
                $"{field.Path} in {collection} is encrypted without queries, so a filter can't compare it: declare it with " +
                "an EncryptedQuery to query it.");
        }

        return field.IsRange
            ? await RangeAsync(field, operators, cancellationToken)
            : (new BsonElement(field.Path, await EqualityAsync(field, operators, cancellationToken)), null);
    }

    private async ValueTask<BsonDocument> EqualityAsync(FieldSchema field,
        BsonDocument operators,
        CancellationToken cancellationToken)
    {
        var encrypted = new BsonDocument();

        foreach (var element in operators)
        {
            var (name, operand) = (element.Name, element.Value);
            switch (name)
            {
                case "$eq" or "$ne":
                    encrypted.Add(name, await FindAsync(field, operand, cancellationToken));
                    break;

                case "$in" or "$nin" when operand is BsonArray values:
                    var each = new BsonArray(values.Count);
                    foreach (var value in values)
                    {
                        each.Add(value.IsBsonNull
                            ? throw new NotSupportedException(
                                $"A filter of {collection} looks for null among values of {field.Path}, which is encrypted " +
                                "and stored as absent when null: compare it with null on its own.")
                            : await FindAsync(field, value, cancellationToken));
                    }

                    encrypted.Add(name, each);
                    break;

                case "$exists":
                    encrypted.Add(name, operand);
                    break;

                default:
                    throw Unsupported(field, name, "$eq, $ne, $in, $nin and $exists");
            }
        }

        return encrypted;
    }

    private async ValueTask<(BsonElement? Element, BsonDocument? Clause)> RangeAsync(FieldSchema field,
        BsonDocument operators,
        CancellationToken cancellationToken)
    {
        var bounds = new BsonArray();
        var clauses = new List<BsonDocument>();
        BsonDocument? exists = null;

        foreach (var element in operators)
        {
            var (name, operand) = (element.Name, element.Value);
            switch (name)
            {
                case "$gt" or "$gte" or "$lt" or "$lte":
                    bounds.Add(new BsonDocument(field.Path, new BsonDocument(name, operand)));
                    break;

                case "$eq":
                    clauses.Add(await PointAsync(field, operand, cancellationToken));
                    break;

                case "$ne":
                    clauses.Add(new BsonDocument("$nor", new BsonArray { await PointAsync(field, operand, cancellationToken) }));
                    break;

                case "$in" or "$nin" when operand is BsonArray values:
                    var points = new BsonArray();
                    foreach (var value in values)
                    {
                        points.Add(await PointAsync(field, value, cancellationToken));
                    }

                    clauses.Add(new BsonDocument(name == "$in" ? "$or" : "$nor", points));
                    break;

                case "$exists":
                    (exists ??= new BsonDocument()).Set(name, operand);
                    break;

                default:
                    throw Unsupported(field, name, "$eq, $ne, $gt, $gte, $lt, $lte, $in, $nin and $exists");
            }
        }

        if (bounds.Count > 0)
        {
            clauses.Add(await encryptor.EncryptExpressionAsync(new BsonDocument("$and", bounds),
                field.FindOptions(keys[field.Path]), cancellationToken));
        }

        // A condition of nothing but $exists never gets here, so there's a clause.
        var clause = clauses.Count == 1 ? clauses[0] : new BsonDocument("$and", new BsonArray(clauses));

        return (exists is null ? null : new BsonElement(field.Path, exists), clause);
    }

    // Equality with a value of a range field is the range from it to it.
    private ValueTask<BsonDocument> PointAsync(FieldSchema field,
        BsonValue value,
        CancellationToken cancellationToken) =>
        encryptor.EncryptExpressionAsync(
            new BsonDocument("$and", new BsonArray
            {
                new BsonDocument(field.Path, new BsonDocument("$gte", value)),
                new BsonDocument(field.Path, new BsonDocument("$lte", value))
            }),
            field.FindOptions(keys[field.Path]),
            cancellationToken);

    private ValueTask<BsonValue> FindAsync(FieldSchema field,
        BsonValue value,
        CancellationToken cancellationToken)
    {
        if (value is BsonRegularExpression)
        {
            throw new NotSupportedException(
                $"A filter of {collection} matches {field.Path}, which is encrypted, with a regular expression: the server " +
                "compares encrypted values only whole.");
        }

        return encryptor.EncryptAsync(value, field.FindOptions(keys[field.Path]), cancellationToken);
    }

    /// <summary>
    /// The values <paramref name="set"/> sets, with those of encrypted members encrypted, and the encrypted members it
    /// sets to null, which are unset instead.
    /// </summary>
    private async ValueTask<(BsonDocument Set, List<string>? Unset)> SetAsync(BsonDocument set,
        CancellationToken cancellationToken)
    {
        BsonDocument? encrypted = null;
        List<string>? unset = null;

        for (var i = 0; i < set.ElementCount; i++)
        {
            var (path, value) = (set.GetElement(i).Name, set.GetElement(i).Value);
            BsonValue? replacement = value;

            if (FieldAt(path) is { } field)
            {
                if (value.IsBsonNull)
                {
                    (unset ??= []).Add(path);
                    replacement = null;
                }
                else
                {
                    replacement = await StoreAsync(field, value, cancellationToken);
                }
            }
            else if (FieldReached(path) is { } reached)
            {
                if (!reached.Path.StartsWith(path + ".", StringComparison.Ordinal))
                {
                    throw new NotSupportedException(
                        $"An update of {collection} sets {path}, inside {reached.Path}, which is encrypted: set the encrypted " +
                        "member whole.");
                }

                // A document holding encrypted members, set whole: they're encrypted inside it.
                if (value is BsonDocument holder)
                {
                    var copy = (BsonDocument)holder.DeepClone();
                    var prefix = path.Split('.').Length;

                    foreach (var inside in fields.Where(each => each.Path.StartsWith(path + ".", StringComparison.Ordinal)))
                    {
                        await StoreAtAsync(copy, inside, inside.Segments, prefix, cancellationToken);
                    }

                    replacement = copy;
                }
            }

            if (encrypted is null && !ReferenceEquals(replacement, value))
            {
                encrypted = [];
                for (var j = 0; j < i; j++)
                {
                    encrypted.Add(set.GetElement(j));
                }
            }

            if (encrypted is not null && replacement is not null)
            {
                encrypted.Add(path, replacement);
            }
        }

        return (encrypted ?? set, unset);
    }

    /// <summary>Encrypts <paramref name="field"/>'s value in <paramref name="document"/>, at its names from <paramref name="index"/>.</summary>
    private async ValueTask StoreAtAsync(BsonDocument document,
        FieldSchema field,
        string[] segments,
        int index,
        CancellationToken cancellationToken)
    {
        var name = segments[index];
        if (!document.TryGetValue(name, out var value))
        {
            return;
        }

        if (index < segments.Length - 1)
        {
            switch (value)
            {
                case BsonDocument inner:
                    await StoreAtAsync(inner, field, segments, index + 1, cancellationToken);
                    break;

                case BsonArray:
                    throw new NotSupportedException(
                        $"{field.Path} in {collection} is encrypted, but a document holds an array along its path: an encrypted " +
                        "member can't be inside an array.");
            }

            return;
        }

        if (value.IsBsonNull)
        {
            document.Remove(name);
        }
        else
        {
            document[name] = await StoreAsync(field, value, cancellationToken);
        }
    }

    private ValueTask<BsonValue> StoreAsync(FieldSchema field,
        BsonValue value,
        CancellationToken cancellationToken) =>
        encryptor.EncryptAsync(value, field.StoreOptions(keys[field.Path]), cancellationToken);

    private FieldSchema? FieldAt(string path)
    {
        foreach (var field in fields)
        {
            if (field.Path == path)
            {
                return field;
            }
        }

        return null;
    }

    // The encrypted field a path holds or is inside, other than one it is.
    private FieldSchema? FieldReached(string path)
    {
        foreach (var field in fields)
        {
            if (field.Path != path && ElementPaths.Reaches(path, field.Path))
            {
                return field;
            }
        }

        return null;
    }

    // The encrypted field an aggregation expression refers to, as "$path".
    private FieldSchema? RefersToField(BsonValue expression) =>
        expression switch
        {
            BsonString { Value: ['$', not '$', ..] path } => FieldAt(path[1..]) ?? FieldReached(path[1..]),
            BsonDocument document => document.Values.Select(RefersToField).FirstOrDefault(field => field is not null),
            BsonArray array => array.Select(RefersToField).FirstOrDefault(field => field is not null),
            _ => null
        };

    private NotSupportedException Unsupported(FieldSchema field,
        string name,
        string supported) =>
        new($"A filter of {collection} compares {field.Path}, which is encrypted for {field.Query!.Type}, with {name}: it " +
            $"supports {supported}.");
}
