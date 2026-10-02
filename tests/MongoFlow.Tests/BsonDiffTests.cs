using MongoDB.Bson;

namespace MongoFlow.Tests;

/// <summary>
/// <see cref="BsonDiff"/> finds what changed between two serializations of a document, in raw BSON:
/// <list type="number">
/// <item>equal bytes, or the same fields in another order, are no change;</item>
/// <item>a changed, added or removed field is set or unset, a change of type included;</item>
/// <item>embedded documents are compared field by field, by dotted path; arrays and anything else change whole;</item>
/// <item>a field inside a document whose name a path can't hold sets that document whole; a top-level one needs a
/// replace;</item>
/// <item>every type the driver writes is walked, and any other fails.</item>
/// </list>
/// </summary>
public class BsonDiffTests
{
    [Test]
    public async Task Compare_SameDocument_FindsNothing()
    {
        // Arrange
        var document = new BsonDocument { { "_id", 1 }, { "Customer", "ada" } };

        // Act
        var changes = Compare(document, document);

        // Assert
        await Assert.That(changes).IsNull();
    }

    [Test]
    public async Task Compare_FieldsInAnotherOrder_FindsNothing()
    {
        // Arrange
        var before = new BsonDocument { { "_id", 1 }, { "Customer", "ada" }, { "Total", 10 } };
        var after = new BsonDocument { { "Total", 10 }, { "_id", 1 }, { "Customer", "ada" } };

        // Act
        var changes = Compare(before, after);

        // Assert
        await Assert.That(changes).IsNull();
    }

    [Test]
    public async Task Compare_FieldsChangedAddedAndRemoved_SetsAndUnsetsThem()
    {
        // Arrange
        var before = new BsonDocument { { "_id", 1 }, { "Customer", "ada" }, { "Note", "rush" } };
        var after = new BsonDocument { { "_id", 1 }, { "Customer", "bob" }, { "Total", 10 } };

        // Act & Assert
        await VerifyUpdate(before, after);
    }

    [Test]
    public async Task Compare_NumberOfAnotherType_SetsIt()
    {
        // Arrange — the same value, stored as a 64-bit integer.
        var before = new BsonDocument { { "_id", 1 }, { "Total", 10 } };
        var after = new BsonDocument { { "_id", 1 }, { "Total", 10L } };

        // Act & Assert
        await VerifyUpdate(before, after);
    }

    [Test]
    public async Task Compare_FieldsOfEmbeddedDocumentsChanged_SetsAndUnsetsTheirPaths()
    {
        // Arrange
        var before = new BsonDocument
        {
            { "_id", 1 },
            { "Address", new BsonDocument { { "City", "Izmir" }, { "Geo", new BsonDocument { { "Lat", 38 }, { "Lng", 27 } } } } }
        };
        var after = new BsonDocument
        {
            { "_id", 1 },
            { "Address", new BsonDocument { { "City", "Izmir" }, { "Geo", new BsonDocument { { "Lat", 41 } } }, { "Zip", "35000" } } }
        };

        // Act & Assert
        await VerifyUpdate(before, after);
    }

    [Test]
    public async Task Compare_EmbeddedDocumentReplacedByAValue_SetsIt()
    {
        // Arrange
        var before = new BsonDocument { { "_id", 1 }, { "Address", new BsonDocument("City", "Izmir") } };
        var after = new BsonDocument { { "_id", 1 }, { "Address", BsonNull.Value } };

        // Act & Assert
        await VerifyUpdate(before, after);
    }

    [Test]
    public async Task Compare_ArrayElementChanged_SetsTheWholeArray()
    {
        // Arrange
        var before = new BsonDocument { { "_id", 1 }, { "Lines", new BsonArray { "a", "b" } } };
        var after = new BsonDocument { { "_id", 1 }, { "Lines", new BsonArray { "a", "c" } } };

        // Act & Assert
        await VerifyUpdate(before, after);
    }

    [Test]
    public async Task Compare_EmbeddedFieldsNamedWithADotOrADollar_SetTheDocumentsHoldingThem()
    {
        // Arrange — "Prices" gains a dotted name and "Labels" loses a $ one, which no path can address.
        var before = new BsonDocument
        {
            { "_id", 1 },
            { "Prices", new BsonDocument { { "base", 1 } } },
            { "Labels", new BsonDocument { { "$tag", "a" }, { "kept", "b" } } }
        };
        var after = new BsonDocument
        {
            { "_id", 1 },
            { "Prices", new BsonDocument { { "base", 1 }, { "v1.2", 2 } } },
            { "Labels", new BsonDocument { { "kept", "b" } } }
        };

        // Act & Assert
        await VerifyUpdate(before, after);
    }

    [Test]
    public async Task Compare_EmbeddedDocumentNamedWithADot_SetsItWhole()
    {
        // Arrange
        var before = new BsonDocument { { "_id", 1 }, { "Prices", new BsonDocument("v1.2", new BsonDocument("base", 1)) } };
        var after = new BsonDocument { { "_id", 1 }, { "Prices", new BsonDocument("v1.2", new BsonDocument("base", 2)) } };

        // Act & Assert
        await VerifyUpdate(before, after);
    }

    [Test]
    [Arguments("a.b")]
    [Arguments("$a")]
    [Arguments("")]
    public async Task Compare_TopLevelFieldNamedSoNoPathCanHoldIt_NeedsAReplace(string name)
    {
        // Arrange
        var before = new BsonDocument { { "_id", 1 }, { name, 1 } };
        var after = new BsonDocument { { "_id", 1 }, { name, 2 } };

        // Act
        var changes = Compare(before, after);

        // Assert
        await Assert.That(changes!.NeedsReplace).IsTrue();
    }

    [Test]
    public async Task Compare_TopLevelFieldNamedSoNoPathCanHoldItRemoved_NeedsAReplace()
    {
        // Arrange
        var before = new BsonDocument { { "_id", 1 }, { "a.b", 1 } };
        var after = new BsonDocument { { "_id", 1 } };

        // Act
        var changes = Compare(before, after);

        // Assert
        await Assert.That(changes!.NeedsReplace).IsTrue();
    }

    [Test]
    public async Task Compare_EveryTypeTheDriverWrites_WalksPastEachToTheChange()
    {
        // Arrange — only the last field changes, so each value before it was measured right.
        var before = EveryType();
        before.Add("Last", 1);
        var after = EveryType();
        after.Add("Last", 2);

        // Act & Assert
        await VerifyUpdate(before, after);
    }

    [Test]
    [Arguments((byte)0x0C)]
    [Arguments((byte)0x20)]
    public async Task Compare_ATypeTheDriverDoesntWrite_ThrowsInvalidOperationException(byte type)
    {
        // Arrange — a DBPointer, which the driver reads but never writes, or no type at all; a pointer's value follows.
        byte[] unknown = [0x19, 0, 0, 0, type, (byte)'p', 0, 1, 0, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 0];
        var other = new BsonDocument("q", 1).ToBson();

        // Act & Assert
        await Assert.That(() => BsonDiff.Compare(other, unknown)).ThrowsExactly<InvalidOperationException>();
    }

    private static BsonChanges? Compare(BsonDocument before,
        BsonDocument after) =>
        BsonDiff.Compare(before.ToBson(), after.ToBson());

    private static Task VerifyUpdate(BsonDocument before,
        BsonDocument after)
    {
        var changes = Compare(before, after)!;

        return Verify(new { Update = changes.ToUpdate(after), changes.NeedsReplace });
    }

    private static BsonDocument EveryType() => new()
    {
        { "Double", 1.5 },
        { "String", "text" },
        { "Document", new BsonDocument("a", 1) },
        { "Array", new BsonArray { 1, 2 } },
        { "Binary", new BsonBinaryData([1, 2, 3]) },
        { "Undefined", BsonUndefined.Value },
        { "ObjectId", ObjectId.Parse("652f1c2e9b1d4a3f8c7e6d5b") },
        { "Boolean", true },
        { "DateTime", new BsonDateTime(new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc)) },
        { "Null", BsonNull.Value },
        { "RegularExpression", new BsonRegularExpression("^a", "i") },
        { "JavaScript", new BsonJavaScript("x") },
        { "Symbol", BsonSymbolTable.Lookup("s") },
        { "JavaScriptWithScope", new BsonJavaScriptWithScope("y", new BsonDocument("z", 1)) },
        { "Int32", 1 },
        { "Timestamp", new BsonTimestamp(1, 2) },
        { "Int64", 1L },
        { "Decimal128", new BsonDecimal128(1.5m) },
        { "MinKey", BsonMinKey.Value },
        { "MaxKey", BsonMaxKey.Value }
    };
}
