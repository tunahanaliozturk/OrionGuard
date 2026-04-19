using Moongazing.OrionGuard.Domain.Primitives;

namespace Moongazing.OrionGuard.Demo.Domain;

// ============================================================================
// Style 1: SOURCE GENERATOR — [StronglyTypedId<TValue>] on a readonly partial struct.
// The OrionGuard generator emits: partial struct body (IEquatable, operators,
// Value property, New(), Empty), EF Core ValueConverter, System.Text.Json
// converter, and ASP.NET Core TypeConverter — all as separate generated files.
// These are structs and do NOT inherit from StronglyTypedId<TValue>, so they
// don't participate in the AgainstDefaultStronglyTypedId guard. They're ideal
// when you want maximum perf (struct, no allocation) and no extra serialization
// wiring.
// ============================================================================

[StronglyTypedId<Guid>]
public readonly partial struct ProductId;

[StronglyTypedId<int>]
public readonly partial struct SkuId;

[StronglyTypedId<string>]
public readonly partial struct CountryCode;

// ============================================================================
// Style 2: MANUAL RECORD — inherits StronglyTypedId<TValue> abstract record.
// Reference type. Participates in the AgainstDefaultStronglyTypedId guard via
// the base-class receiver. Use this when you want default-value validation
// via the OrionGuard guard extension, or you prefer record syntax.
// ============================================================================

public sealed record OrderId(Guid Value) : StronglyTypedId<Guid>(Value)
{
    public static OrderId New() => new(Guid.NewGuid());
}

public sealed record CustomerId(Guid Value) : StronglyTypedId<Guid>(Value)
{
    public static CustomerId New() => new(Guid.NewGuid());
}

public sealed record InvoiceId(Guid Value) : StronglyTypedId<Guid>(Value)
{
    public static InvoiceId New() => new(Guid.NewGuid());
}
