using System.Text.Json;
using Microsoft.OpenApi;
using Moongazing.OrionGuard.Attributes;
using Moongazing.OrionGuard.Swagger;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Moongazing.OrionGuard.AspNetCore.Tests.Swagger;

public class OrionGuardSchemaFilterTests
{
    private sealed class SignupRequest
    {
        [NotNull] public string? Name { get; set; }
        [NotEmpty] public string Nickname { get; set; } = "";
        [Length(3, 20)] public string Username { get; set; } = "";
        [Range(0.5, 99.5)] public double Score { get; set; }
        [Email] public string Email { get; set; } = "";
        [Regex("^[A-Z]{2}$")] public string CountryCode { get; set; } = "";
        [Positive] public int Quantity { get; set; }
    }

    private static OpenApiSchema GenerateSchema()
    {
        var options = new SchemaGeneratorOptions();
        options.SchemaFilters.Add(new OrionGuardSchemaFilter());
        var resolver = new JsonSerializerDataContractResolver(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var repository = new SchemaRepository();

        new SchemaGenerator(options, resolver).GenerateSchema(typeof(SignupRequest), repository);

        return Assert.IsType<OpenApiSchema>(repository.Schemas[nameof(SignupRequest)]);
    }

    private static OpenApiSchema Property(OpenApiSchema schema, string name) =>
        Assert.IsType<OpenApiSchema>(schema.Properties![name]);

    [Fact]
    public void NotNull_ShouldMarkRequired_AndDropNullType()
    {
        var schema = GenerateSchema();

        Assert.Contains("name", schema.Required!);
        Assert.False(Property(schema, "name").Type!.Value.HasFlag(JsonSchemaType.Null));
    }

    [Fact]
    public void LengthAndNotEmpty_ShouldSetStringLengthBounds()
    {
        var schema = GenerateSchema();

        Assert.Equal(1, Property(schema, "nickname").MinLength);
        Assert.Equal(3, Property(schema, "username").MinLength);
        Assert.Equal(20, Property(schema, "username").MaxLength);
    }

    [Fact]
    public void Range_ShouldWriteInvariantNumericBounds()
    {
        var score = Property(GenerateSchema(), "score");

        Assert.Equal("0.5", score.Minimum);
        Assert.Equal("99.5", score.Maximum);
    }

    [Fact]
    public void Positive_ShouldSetExclusiveMinimumOfZero()
    {
        Assert.Equal("0", Property(GenerateSchema(), "quantity").ExclusiveMinimum);
    }

    [Fact]
    public void EmailAndRegex_ShouldSetFormatAndPattern()
    {
        var schema = GenerateSchema();

        Assert.Equal("email", Property(schema, "email").Format);
        Assert.Equal("^[A-Z]{2}$", Property(schema, "countryCode").Pattern);
    }
}
