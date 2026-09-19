using System.Text;
using System.Text.Json;

namespace Moongazing.OrionGuard.SchemaExport.Tests;

public sealed class JsonSchemaExporterTests
{
    [Fact]
    public void Export_PlainType_WritesADraft2020DocumentDescribingOnlyTheShape()
    {
        var expected = """
            {
              "$schema": "https://json-schema.org/draft/2020-12/schema",
              "title": "PlainProfile",
              "type": "object",
              "properties": {
                "displayName": {
                  "type": "string"
                },
                "visits": {
                  "type": "integer"
                }
              }
            }
            """;

        Assert.Equal(Normalize(expected), Normalize(JsonSchemaExporter.Export<PlainProfile>()));
    }

    [Fact]
    public void Export_NotNullAndNotEmpty_ListBothMembersAsRequired()
    {
        using var schema = Schema<ConstrainedRequest>();

        Assert.Equal(["id", "nickname"], Strings(schema.RootElement.GetProperty("required")));
    }

    [Fact]
    public void Export_NotEmpty_SetsMinLengthOfOne()
    {
        using var schema = Schema<ConstrainedRequest>();

        Assert.Equal(1, Member(schema, "nickname").GetProperty("minLength").GetInt32());
    }

    [Fact]
    public void Export_Length_SetsBothStringBounds()
    {
        using var schema = Schema<ConstrainedRequest>();
        var slug = Member(schema, "slug");

        Assert.Equal(3, slug.GetProperty("minLength").GetInt32());
        Assert.Equal(50, slug.GetProperty("maxLength").GetInt32());
    }

    [Fact]
    public void Export_Regex_SetsThePattern()
    {
        using var schema = Schema<ConstrainedRequest>();

        Assert.Equal("^[a-z0-9-]+$", Member(schema, "handle").GetProperty("pattern").GetString());
    }

    [Fact]
    public void Export_Email_SetsTheEmailFormat()
    {
        using var schema = Schema<ConstrainedRequest>();

        Assert.Equal("email", Member(schema, "email").GetProperty("format").GetString());
    }

    [Fact]
    public void Export_Range_SetsBothNumericBounds()
    {
        using var schema = Schema<ConstrainedRequest>();
        var age = Member(schema, "age");

        Assert.Equal(13, age.GetProperty("minimum").GetDouble());
        Assert.Equal(120, age.GetProperty("maximum").GetDouble());
    }

    [Fact]
    public void Export_Positive_SetsExclusiveMinimumZeroRatherThanAFlag()
    {
        using var schema = Schema<ConstrainedRequest>();
        var budget = Member(schema, "budget");

        Assert.Equal("number", budget.GetProperty("type").GetString());
        Assert.Equal(0, budget.GetProperty("exclusiveMinimum").GetDouble());
    }

    [Fact]
    public void Export_UriMember_CarriesTheUriFormat()
    {
        using var schema = Schema<ConstrainedRequest>();

        Assert.Equal("uri", Member(schema, "website").GetProperty("format").GetString());
    }

    [Fact]
    public void Export_NestedType_ReferencesADefinitionInsteadOfInliningIt()
    {
        using var schema = Schema<Order>();

        Assert.Equal("#/$defs/Address", Member(schema, "shipTo").GetProperty("$ref").GetString());

        var address = schema.RootElement.GetProperty("$defs").GetProperty("Address");
        Assert.Equal(1, address.GetProperty("properties").GetProperty("city").GetProperty("minLength").GetInt32());
        Assert.Equal("city", address.GetProperty("required")[0].GetString());
    }

    [Fact]
    public void Export_NullableNestedType_OffersTheNullBranchBesideTheReference()
    {
        using var schema = Schema<Order>();

        var branches = Member(schema, "billTo").GetProperty("anyOf");

        Assert.Equal("#/$defs/Address", branches[0].GetProperty("$ref").GetString());
        Assert.Equal("null", branches[1].GetProperty("type").GetString());
    }

    [Fact]
    public void Export_CollectionOfNestedType_DescribesTheElementSchema()
    {
        using var schema = Schema<Basket>();
        var lines = Member(schema, "lines");

        Assert.Equal("array", lines.GetProperty("type").GetString());
        Assert.Equal("#/$defs/OrderLine", lines.GetProperty("items").GetProperty("$ref").GetString());
    }

    [Fact]
    public void Export_NotEmptyOnACollection_BoundsTheItemCountNotTheCharacterCount()
    {
        using var schema = Schema<Basket>();
        var lines = Member(schema, "lines");

        Assert.Equal(1, lines.GetProperty("minItems").GetInt32());
        Assert.False(lines.TryGetProperty("minLength", out _));
    }

    [Fact]
    public void Export_ArrayOfScalars_DescribesTheElementType()
    {
        using var schema = Schema<Basket>();
        var tags = Member(schema, "tags");

        Assert.Equal("array", tags.GetProperty("type").GetString());
        Assert.Equal("string", tags.GetProperty("items").GetProperty("type").GetString());
    }

    [Fact]
    public void Export_NullableMembers_AddNullToTheTypeAndLeaveThemOutOfRequired()
    {
        using var schema = Schema<Contact>();

        Assert.Equal(["string", "null"], Types(Member(schema, "nickname")));
        Assert.Equal(["integer", "null"], Types(Member(schema, "age")));
        Assert.Equal("string", Member(schema, "email").GetProperty("type").GetString());
        Assert.Equal(["email"], Strings(schema.RootElement.GetProperty("required")));
    }

    [Fact]
    public void Export_Enum_ListsTheMemberNames()
    {
        using var schema = Schema<Shipment>();
        var status = Member(schema, "status");

        Assert.Equal("string", status.GetProperty("type").GetString());
        Assert.Equal(["Pending", "Shipped", "Delivered"], Strings(status.GetProperty("enum")));
    }

    [Fact]
    public void Export_NullableEnum_AllowsNullInBothTheTypeAndTheValueList()
    {
        using var schema = Schema<Shipment>();
        var previous = Member(schema, "previousStatus");

        Assert.Equal(["string", "null"], Types(previous));

        var values = previous.GetProperty("enum").EnumerateArray().ToArray();
        Assert.Equal(["Pending", "Shipped", "Delivered"], values[..^1].Select(value => value.GetString()!).ToArray());
        Assert.Equal(JsonValueKind.Null, values[^1].ValueKind);
    }

    [Fact]
    public void Export_RuleTheSchemaCannotExpress_ReportsItInsteadOfDroppingIt()
    {
        using var schema = Schema<PasswordChange>();

        var unsupported = schema.RootElement.GetProperty("x-orionguard-unsupported")
            .EnumerateArray()
            .Select(entry => (entry.GetProperty("member").GetString()!, entry.GetProperty("rule").GetString()!))
            .ToArray();

        Assert.Equal(
            [("confirmation", "MatchesPasswordAttribute"), ("slots", "EvenNumberAttribute")],
            unsupported);
    }

    [Fact]
    public void Export_WhenEveryRuleMaps_OmitsTheUnsupportedList()
    {
        using var schema = Schema<ConstrainedRequest>();

        Assert.False(schema.RootElement.TryGetProperty("x-orionguard-unsupported", out _));
    }

    [Fact]
    public void Export_ToAWriter_ProducesTheSameDocumentAsTheStringOverload()
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            JsonSchemaExporter.Export<ConstrainedRequest>(writer);
        }

        Assert.Equal(
            JsonSchemaExporter.Export<ConstrainedRequest>(),
            Encoding.UTF8.GetString(buffer.ToArray()));
    }

    [Fact]
    public void Export_ToANullWriter_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => JsonSchemaExporter.Export<PlainProfile>(null!));
    }

    [Fact]
    public void Export_NotEmptyOnAString_RequiresANonWhitespaceCharacterNotJustALength()
    {
        using var schema = Schema<ConstrainedRequest>();

        // [NotEmpty] is IsNullOrWhiteSpace for a string; minLength alone would accept "   ".
        Assert.Equal(@"\S", Member(schema, "nickname").GetProperty("pattern").GetString());
    }

    [Fact]
    public void Export_NotEmptyOnACollection_DoesNotAddTheNonWhitespacePattern()
    {
        using var schema = Schema<Basket>();

        Assert.False(Member(schema, "lines").TryGetProperty("pattern", out _));
    }

    [Fact]
    public void Export_MemberWithSeveralPatterns_AndsThemInsteadOfKeepingOne()
    {
        using var schema = Schema<PatternRules>();

        var patterns = Member(schema, "notBlankAndPattern").GetProperty("allOf")
            .EnumerateArray().Select(branch => branch.GetProperty("pattern").GetString()!).ToArray();

        Assert.Equal([@"\S", "^[a-z]+$"], patterns);
    }

    [Fact]
    public void Export_PortablePattern_IsCopiedIntoTheSchema()
    {
        using var schema = Schema<PatternRules>();

        Assert.Equal("^[a-z]+$", Member(schema, "portable").GetProperty("pattern").GetString());
        Assert.Equal("(?=.*[0-9])^.{4,}$", Member(schema, "lookahead").GetProperty("pattern").GetString());
    }

    [Theory]
    [InlineData("inlineOptions")]
    [InlineData("dotNetAnchors")]
    [InlineData("namedGroup")]
    [InlineData("unicodeCategory")]
    [InlineData("classSubtraction")]
    public void Export_PatternThatIsNotEcmaScript_IsReportedRatherThanCopied(string member)
    {
        using var schema = Schema<PatternRules>();

        Assert.False(Member(schema, member).TryGetProperty("pattern", out _));

        var reported = schema.RootElement.GetProperty("x-orionguard-unsupported")
            .EnumerateArray()
            .Any(entry => entry.GetProperty("member").GetString() == member
                && entry.GetProperty("rule").GetString() == "RegexAttribute");

        Assert.True(reported);
    }

    [Fact]
    public void Export_NullableCollectionElement_AllowsNullInTheItemSchema()
    {
        using var schema = Schema<Roster>();

        Assert.Equal(["string", "null"], Types(Member(schema, "nicknames").GetProperty("items")));
        Assert.Equal(["string", "null"], Types(Member(schema, "aliases").GetProperty("items")));
        Assert.Equal(["integer", "null"], Types(Member(schema, "scores").GetProperty("items")));
    }

    [Fact]
    public void Export_NonNullableCollectionElement_KeepsNullOutOfTheItemSchema()
    {
        using var schema = Schema<Roster>();

        Assert.Equal("string", Member(schema, "names").GetProperty("items").GetProperty("type").GetString());
    }

    [Fact]
    public void Export_NestedCollection_DefinesTheObjectAtTheBottomOfIt()
    {
        using var schema = Schema<Warehouse>();
        var shelves = Member(schema, "shelves");

        Assert.Equal("array", shelves.GetProperty("type").GetString());

        var inner = shelves.GetProperty("items");
        Assert.Equal("array", inner.GetProperty("type").GetString());
        Assert.Equal("#/$defs/Address", inner.GetProperty("items").GetProperty("$ref").GetString());
        Assert.True(schema.RootElement.GetProperty("$defs").TryGetProperty("Address", out _));
    }

    [Fact]
    public void Export_ByteArray_IsABase64StringNotAnArrayOfIntegers()
    {
        using var schema = Schema<Upload>();
        var content = Member(schema, "content");

        Assert.Equal("string", content.GetProperty("type").GetString());
        Assert.Equal("base64", content.GetProperty("contentEncoding").GetString());
        Assert.False(content.TryGetProperty("items", out _));
        Assert.Equal(["string", "null"], Types(Member(schema, "thumbnail")));
    }

    private static JsonDocument Schema<T>() => JsonDocument.Parse(JsonSchemaExporter.Export<T>());

    private static JsonElement Member(JsonDocument schema, string name) =>
        schema.RootElement.GetProperty("properties").GetProperty(name);

    private static string[] Types(JsonElement member) => Strings(member.GetProperty("type"));

    private static string[] Strings(JsonElement array) =>
        array.EnumerateArray().Select(value => value.GetString()!).ToArray();

    // Utf8JsonWriter indents with the platform newline; the expected documents use "\n".
    private static string Normalize(string schema) => schema.Replace("\r\n", "\n");
}
