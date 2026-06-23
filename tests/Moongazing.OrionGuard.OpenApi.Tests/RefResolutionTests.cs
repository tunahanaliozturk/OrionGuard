using System.Reflection;
using Moongazing.OrionGuard.Core;

namespace Moongazing.OrionGuard.OpenApi.Tests;

/// <summary>
/// Tests intra-document <c>$ref</c> resolution: a property whose schema is a <c>$ref</c> to another
/// schema in the same document must have the referenced schema's constraints enforced, and the root
/// pointer itself must be followable through a <c>$ref</c>.
/// </summary>
public class RefResolutionTests
{
    private const string ConsumerSource = """
        using Moongazing.OrionGuard.DependencyInjection;

        namespace Sample
        {
            public sealed class Order
            {
                public string OrderNumber { get; set; } = "";
                public string Sku { get; set; } = "";
            }

            [Moongazing.OrionGuard.OpenApi.OpenApiValidator("order.json", "#/components/schemas/Order")]
            public partial class OrderValidator : IValidator<Order> { }
        }
        """;

    // The 'sku' property is a $ref to a reusable Sku schema that carries the pattern constraint.
    private const string OrderDocument = """
        {
          "openapi": "3.0.3",
          "info": { "title": "Orders", "version": "1.0.0" },
          "components": {
            "schemas": {
              "Sku": {
                "type": "string",
                "pattern": "^SKU-[0-9]{4}$"
              },
              "Order": {
                "type": "object",
                "required": ["orderNumber"],
                "properties": {
                  "orderNumber": { "type": "string", "minLength": 3 },
                  "sku":         { "$ref": "#/components/schemas/Sku" }
                }
              }
            }
          }
        }
        """;

    private static readonly Assembly CompiledAssembly =
        GeneratorTestHarness.Compile(ConsumerSource, "order.json", OrderDocument);

    private static object NewOrder(string orderNumber, string sku)
    {
        var orderType = CompiledAssembly.GetType("Sample.Order")!;
        dynamic order = Activator.CreateInstance(orderType)!;
        order.OrderNumber = orderNumber;
        order.Sku = sku;
        return order;
    }

    private static GuardResult Validate(object order) =>
        GeneratorTestHarness.Validate(CompiledAssembly, "Sample.OrderValidator", order);

    [Fact]
    public void RefProperty_ValidValue_Passes()
    {
        var result = Validate(NewOrder("A-100", "SKU-1234"));

        Assert.True(
            result.IsValid,
            "Expected valid order to pass, got: "
            + string.Join("; ", result.Errors.Select(e => $"{e.ParameterName}:{e.ErrorCode}")));
    }

    [Fact]
    public void RefProperty_ConstraintFromReferencedSchema_IsEnforced()
    {
        // 'sku' violates the pattern declared on the referenced Sku schema.
        var result = Validate(NewOrder("A-100", "bad-sku"));

        Assert.Contains(result.Errors, e => e.ParameterName == "sku" && e.ErrorCode == "PATTERN");
    }

    [Fact]
    public void RootPointer_ThroughRef_ResolvesAndEnforces()
    {
        // The validator's root pointer targets an alias schema that is itself a $ref to Order.
        const string aliasConsumer = """
            using Moongazing.OrionGuard.DependencyInjection;

            namespace Sample
            {
                public sealed class OrderAlias
                {
                    public string OrderNumber { get; set; } = "";
                }

                [Moongazing.OrionGuard.OpenApi.OpenApiValidator("alias.json", "#/components/schemas/OrderRef")]
                public partial class OrderAliasValidator : IValidator<OrderAlias> { }
            }
            """;

        const string aliasDocument = """
            {
              "openapi": "3.0.3",
              "info": { "title": "Orders", "version": "1.0.0" },
              "components": {
                "schemas": {
                  "OrderRef": { "$ref": "#/components/schemas/Order" },
                  "Order": {
                    "type": "object",
                    "required": ["orderNumber"],
                    "properties": {
                      "orderNumber": { "type": "string", "minLength": 3 }
                    }
                  }
                }
              }
            }
            """;

        var assembly = GeneratorTestHarness.Compile(aliasConsumer, "alias.json", aliasDocument);

        var aliasType = assembly.GetType("Sample.OrderAlias")!;
        dynamic shortNumber = Activator.CreateInstance(aliasType)!;
        shortNumber.OrderNumber = "A"; // below minLength 3
        var failing = GeneratorTestHarness.Validate(assembly, "Sample.OrderAliasValidator", (object)shortNumber);
        Assert.Contains(failing.Errors, e => e.ParameterName == "orderNumber" && e.ErrorCode == "MIN_LENGTH");

        dynamic okNumber = Activator.CreateInstance(aliasType)!;
        okNumber.OrderNumber = "A-100";
        var passing = GeneratorTestHarness.Validate(assembly, "Sample.OrderAliasValidator", (object)okNumber);
        Assert.True(passing.IsValid);
    }
}
