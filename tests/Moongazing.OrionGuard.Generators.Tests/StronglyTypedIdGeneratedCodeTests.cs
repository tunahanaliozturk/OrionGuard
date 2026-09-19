namespace Moongazing.OrionGuard.Generators.Tests;

using Moongazing.OrionGuard.Generators.StronglyTypedIds;
using static GeneratorTestHarness;

/// <summary>
/// Compiles and runs the code the deprecated <c>[StronglyTypedId]</c> generator emits.
/// </summary>
public class StronglyTypedIdGeneratedCodeTests
{
    private static T Call<T>(System.Reflection.Assembly assembly, string method) =>
        (T)assembly.GetType("App.Probe")!.GetMethod(method)!.Invoke(null, null)!;

    [Fact]
    public void StringBackedId_ShouldTolerateDefault_AndUseTheGeneratedConverters()
    {
        // default(UserId) holds a null string, on which Value.Equals / GetHashCode / ToString threw
        // NullReferenceException. The converters were generated but never attached, so JSON wrote
        // {"Value":"abc"} and TypeDescriptor fell back to a converter that cannot parse a string.
        const string source = """
            using Moongazing.OrionGuard.Domain.Primitives;

            namespace App
            {
                [StronglyTypedId<string>] public readonly partial struct UserId { }

                public static class Probe
                {
                    public static bool DefaultsEqual() => default(UserId) == default(UserId);
                    public static bool DefaultEqualsEmpty() => default(UserId).Equals(new UserId(""));
                    public static int DefaultHash() => default(UserId).GetHashCode();
                    public static string DefaultText() => default(UserId).ToString();
                    // The JSON converter is not attached, so the default output shape is unchanged; registering
                    // the generated converter gives a bare-value round trip.
                    private static readonly System.Text.Json.JsonSerializerOptions WithConverter =
                        new() { Converters = { new UserIdJsonConverter() } };
                    public static string Json() => System.Text.Json.JsonSerializer.Serialize(new UserId("abc"));
                    public static string JsonWithConverter() => System.Text.Json.JsonSerializer.Serialize(new UserId("abc"), WithConverter);
                    public static UserId FromJsonWithConverter() => System.Text.Json.JsonSerializer.Deserialize<UserId>("\"abc\"", WithConverter);
                    public static object? FromString() =>
                        System.ComponentModel.TypeDescriptor.GetConverter(typeof(UserId)).ConvertFromInvariantString("abc");
                }
            }
            """;

        var assembly = Compile(source, new StronglyTypedIdGenerator());

        Assert.True(Call<bool>(assembly, "DefaultsEqual"));
        Assert.False(Call<bool>(assembly, "DefaultEqualsEmpty"));
        Assert.Equal(0, Call<int>(assembly, "DefaultHash"));
        Assert.Equal(string.Empty, Call<string>(assembly, "DefaultText"));
        Assert.Equal("{\"Value\":\"abc\"}", Call<string>(assembly, "Json"));
        Assert.Equal("\"abc\"", Call<string>(assembly, "JsonWithConverter"));
        Assert.Equal("abc", Call<object>(assembly, "FromJsonWithConverter").ToString());
        Assert.Equal("abc", Call<object>(assembly, "FromString").ToString());
    }

    [Fact]
    public void Generator_ShouldNotDuplicateConverterAttributes_TheUserAlreadyApplied()
    {
        // Emitting a second [JsonConverter] or [TypeConverter] would be CS0579 for anyone who wired the
        // converters by hand to work around them being unattached.
        const string source = """
            using System.ComponentModel;
            using System.Text.Json.Serialization;
            using Moongazing.OrionGuard.Domain.Primitives;

            namespace App
            {
                [StronglyTypedId<int>]
                [JsonConverter(typeof(TicketIdJsonConverter))]
                [TypeConverter(typeof(TicketIdTypeConverter))]
                public readonly partial struct TicketId { }
            }
            """;

        Compile(source, new StronglyTypedIdGenerator());
    }

    [Fact]
    public void Generator_ShouldCompile_ForIdsWithTheSameNameInTwoNamespaces_AndInTheGlobalNamespace()
    {
        // Same-named ids produced duplicate hint names (CS8785, all output dropped); a global-namespace
        // id was emitted inside "namespace <global namespace>" (CS1001).
        const string source = """
            using Moongazing.OrionGuard.Domain.Primitives;

            namespace Billing { [StronglyTypedId<System.Guid>] public readonly partial struct CustomerId { } }
            namespace Crm { [StronglyTypedId<System.Guid>] public readonly partial struct CustomerId { } }

            [StronglyTypedId<System.Guid>] public readonly partial struct OrderId { }

            namespace App
            {
                public static class Probe
                {
                    public static bool Distinct() =>
                        OrderId.New() != OrderId.Empty
                        && Billing.CustomerId.New() != Billing.CustomerId.Empty
                        && Crm.CustomerId.New() != Crm.CustomerId.Empty;
                }
            }
            """;

        var assembly = Compile(source, new StronglyTypedIdGenerator());

        Assert.True(Call<bool>(assembly, "Distinct"));
    }
}
