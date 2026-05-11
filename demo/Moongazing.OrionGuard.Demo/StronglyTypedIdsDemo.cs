using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.Demo.Domain;
using Moongazing.OrionGuard.DependencyInjection;
using Moongazing.OrionGuard.Exceptions;
using Moongazing.OrionGuard.Extensions;

namespace Moongazing.OrionGuard.Demo;

/// <summary>
/// Shows both flavours of strongly-typed IDs (source-generated struct and
/// manual record), the <c>AgainstDefaultStronglyTypedId</c> guard, and the
/// <c>AddOrionGuardStronglyTypedIds</c> DI helper.
/// </summary>
public static class StronglyTypedIdsDemo
{
    public static void Run()
    {
        Console.WriteLine("\n== Strongly-Typed IDs ==");

        // Source-generated struct style (zero allocation). The generator emits
        // IEquatable, operators, New(), Empty, Value plus EF Core / JSON /
        // TypeConverter companions as separate .g.cs files.
        ProductId p1 = ProductId.New();
        ProductId p1copy = new(p1.Value);
        ProductId p2 = ProductId.New();

        Console.WriteLine($"  ProductId.New() = {p1}");
        Console.WriteLine($"  Value equality: p1 == p1copy ? {p1 == p1copy}");
        Console.WriteLine($"  Different ids unequal: p1 != p2 ? {p1 != p2}");

        SkuId sku = new(42);
        CountryCode tr = new("TR");
        Console.WriteLine($"  SkuId (int-backed): {sku}");
        Console.WriteLine($"  CountryCode (string): {tr}");

        var skuConverter = new SkuIdTypeConverter();
        var skuFromString = (SkuId)skuConverter.ConvertFrom("123")!;
        var skuToString = skuConverter.ConvertTo(sku, typeof(string));
        Console.WriteLine($"  Generated TypeConverter: \"123\" -> {skuFromString}, {sku} -> \"{skuToString}\"");

        // Manual record style. Inherits StronglyTypedId<TValue> abstract record
        // (reference type) and participates in the AgainstDefaultStronglyTypedId
        // guard via the base-class receiver.
        OrderId orderId = OrderId.New();
        CustomerId customerId = CustomerId.New();
        InvoiceId invoiceId = InvoiceId.New();

        Console.WriteLine($"  OrderId (manual record): {orderId.Value}");
        Console.WriteLine("  CustomerId and OrderId are distinct record types even though both wrap Guid");

        Console.WriteLine("\n== AgainstDefaultStronglyTypedId Guard ==");

        var validated = invoiceId.AgainstDefaultStronglyTypedId(nameof(invoiceId));
        Console.WriteLine($"  Valid id passed guard and returned: {validated.Value}");

        try
        {
            InvoiceId? nullId = null;
            nullId!.AgainstDefaultStronglyTypedId(nameof(nullId));
        }
        catch (NullValueException)
        {
            Console.WriteLine("  Null id rejected (NullValueException)");
        }

        try
        {
            var emptyId = new InvoiceId(Guid.Empty);
            emptyId.AgainstDefaultStronglyTypedId(nameof(emptyId));
        }
        catch (ZeroValueException)
        {
            Console.WriteLine("  Guid.Empty id rejected (ZeroValueException)");
        }

        Console.WriteLine("\n== AddOrionGuardStronglyTypedIds (DI) ==");

        var services = new ServiceCollection();
        services.AddOrionGuardStronglyTypedIds(typeof(StronglyTypedIdsDemo).Assembly);

        var registrationCount = services.Count(d => d.ServiceType.Name.EndsWith("EfCoreValueConverter", StringComparison.Ordinal));
        Console.WriteLine($"  Registered {registrationCount} generated EF Core ValueConverter(s) as singletons");
        Console.WriteLine("  The generator skips EF Core ValueConverter companions when the consumer project does not reference Microsoft.EntityFrameworkCore (v6.2)");
        Console.WriteLine("  Add Microsoft.EntityFrameworkCore to the project to resume emitting them; JSON and TypeConverter companions emit unconditionally");
    }
}
