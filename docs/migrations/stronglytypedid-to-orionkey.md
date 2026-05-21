# Migrating from OrionGuard `[StronglyTypedId]` to OrionKey

OrionGuard's `[StronglyTypedId<TValue>]` source generator is soft-deprecated as of
v6.4.0. The feature now lives in the standalone **OrionKey** package and will be
removed from OrionGuard in v7.0.0. The generator still works in the v6.x line —
this migration is not urgent, but new code should use OrionKey.

This applies only to the **source generator** (`[StronglyTypedId]` on a `readonly
partial struct`). The manual `StronglyTypedId<TValue>` abstract record,
`IStronglyTypedId<TValue>`, and the `AgainstDefaultStronglyTypedId` guard are not
deprecated and have no OrionKey equivalent — keep using them as-is.

## Steps

1. Install OrionKey:

   ```bash
   dotnet add package OrionKey
   ```

2. Swap the attribute and the `using`:

   | OrionGuard | OrionKey |
   |---|---|
   | `using Moongazing.OrionGuard.Domain.Primitives;` | `using Moongazing.OrionKey;` |
   | `[StronglyTypedId<Guid>]` | `[OrionId<Guid>]` |
   | `[StronglyTypedId<int>]` | `[OrionId<int>]` (externally assigned) |
   | `[StronglyTypedId<long>]` | `[OrionId<long>]` (externally assigned) or `[OrionId<long, Snowflake>]` (generated) |
   | `[StronglyTypedId<string>]` | `[OrionId<string, Ulid>]` or `[OrionId<string, NanoId>]` |

3. The struct stays a `readonly partial struct` — no other change to your type.

## What you get

The emitted surface is equivalent: `Value`, a constructor, `Empty`, `New()` where
applicable, `IEquatable`, equality operators, an EF Core `ValueConverter` (emitted
when the project references EF Core), a `System.Text.Json` converter, a
`TypeConverter`, and `IParsable`/`ISpanParsable`.

OrionKey additionally provides:

- `IComparable` and ordering operators for sortable strategies (`Snowflake`,
  `Ulid`, `GuidV7`).
- Explicit ID strategies: `Snowflake` (64-bit, sortable), `Ulid`, `NanoId`,
  `GuidV7`.
- `OrionKey.Testing` — deterministic ID generators for tests.

See the OrionKey README at https://github.com/tunahanaliozturk/OrionKey.
