# OrionGuard `[StronglyTypedId]` soft-deprecation in favour of OrionKey

**Date:** 2026-05-21
**Status:** Approved (design); pending implementation plan
**Branch:** `feature/v6.4.0` (folds into the in-progress v6.4.0 release)
**Related:** OrionKey v0.1.0 (the standalone successor package, published to NuGet)

## 1. Goal

OrionKey — a standalone Orion-family package — now ships the source-generated strongly-typed-id feature that previously lived only inside OrionGuard's `[StronglyTypedId<TValue>]` generator. OrionGuard's generator is therefore superseded.

This change **soft-deprecates** OrionGuard's `[StronglyTypedId]` source generator and its injected attribute: existing consumers keep compiling and the generator keeps emitting, but each `[StronglyTypedId]` usage now raises a compiler warning that points to OrionKey. The generator will be removed in OrionGuard v7.0.0.

## 2. Scope

**In scope** — only the source generator and its attribute:

- The injected `StronglyTypedIdAttribute<TValue>` (emitted by `Moongazing.OrionGuard.Generators` via `RegisterPostInitializationOutput`) gets `[Obsolete]`.
- The generator (`StronglyTypedIdGenerator` and its emitters) is **unchanged functionally** — it still emits every companion. Only the attribute carries the deprecation.

**Explicitly out of scope** — these are not superseded by OrionKey and stay untouched:

- The manual `StronglyTypedId<TValue>` abstract record (`Domain/Primitives/StronglyTypedId.cs`). OrionKey has no manual-base equivalent — it is entirely source-generated — so deprecating the manual record would strand its users.
- `IStronglyTypedId<TValue>` marker interface.
- `StronglyTypedIdGuards` (`AgainstDefaultStronglyTypedId`) and `StronglyTypedIdServiceExtensions`.

**Non-goals:**

- OrionGuard takes **no** `PackageReference` or `ProjectReference` on OrionKey. The Orion family rule — no library depends on another Orion library — is preserved. The deprecation is a documentation pointer, not a code dependency.
- No type-forwarding or re-export of OrionKey types from OrionGuard.
- No removal of the generator in this release; removal is a v7.0.0 concern.
- No new analyzer diagnostic — the `[Obsolete]` attribute's own CS0618 warning already carries the migration message.

## 3. The change

### 3.1 `[Obsolete]` on the injected attribute

`src/Moongazing.OrionGuard.Generators/StronglyTypedIds/StronglyTypedIdAttribute.cs` holds `StronglyTypedIdAttributeSource.Source` — the C# text injected into every consuming compilation. The injected `StronglyTypedIdAttribute<TValue>` declaration gains:

```csharp
[System.Obsolete("OrionGuard's [StronglyTypedId] is superseded by the standalone OrionKey package. Install OrionKey and use [OrionId<TValue>] / [OrionId<TValue, TStrategy>] instead — see https://www.nuget.org/packages/OrionKey. OrionGuard's generator will be removed in v7.0.0.")]
```

`[Obsolete]` is applied **without** the `error: true` argument, so consumer `[StronglyTypedId<...>]` usages produce a CS0618 *warning*, not an error — this is a soft deprecation. The attribute's XML `<summary>` is updated to state the same migration guidance in prose.

Applying `[Obsolete]` to the generic attribute type definition means every construction (`[StronglyTypedId<Guid>]`, `[StronglyTypedId<long>]`, etc.) reports CS0618 at the application site.

### 3.2 OrionGuard's own `[StronglyTypedId]` usages

OrionGuard builds with `TreatWarningsAsErrors=true`, so its own usages of the now-obsolete attribute would break the build. These must be located and suppressed with a scoped `#pragma warning disable CS0618` plus a short comment explaining the suppression is deliberate (the generator's own demo/tests must exercise the attribute they ship).

Known usage to suppress:

- `demo/Moongazing.OrionGuard.Demo/Domain/Ids.cs` — the demo declares strongly-typed ids with `[StronglyTypedId<...>]`. Wrap the declarations in `#pragma warning disable CS0618` / `restore`.

To investigate during planning:

- `tests/Moongazing.OrionGuard.Generators.Tests/StronglyTypedIdGenerator*.cs` (three files) — these feed C# source *text* containing `[StronglyTypedId<...>]` into an in-memory generator harness. The `[StronglyTypedId]` tokens are string literals, not attribute applications in the test assembly, so the test assembly itself raises no CS0618. However, the harness's in-memory `CSharpCompilation` will now contain an obsolete-attribute usage; any test asserting the generated/compiled output is *diagnostic-free* (or filtering diagnostics) may observe CS0618. The plan must check each generator test and, where a test asserts "no diagnostics", either filter CS0618 out of the assertion or add `#pragma warning disable CS0618` to the test's source-text fixture.

The implementation plan must produce the exhaustive list by building the solution after 3.1 and collecting every CS0618 site.

### 3.3 CHANGELOG

The existing `## [6.4.0]` entry in `CHANGELOG.md` gains a `### Deprecated` subsection:

```markdown
### Deprecated

- The `[StronglyTypedId<TValue>]` source generator is soft-deprecated in favour of the standalone **OrionKey** package (`[OrionId<TValue>]` / `[OrionId<TValue, TStrategy>]`). Existing usages keep compiling and the generator keeps emitting; each `[StronglyTypedId]` usage now raises a CS0618 warning with migration guidance. The generator will be removed in v7.0.0. The manual `StronglyTypedId<TValue>` record, `IStronglyTypedId<TValue>`, and the related guards are unaffected.
```

### 3.4 Documentation

- `README.md` — wherever `[StronglyTypedId]` appears in an example or feature table, add a one-line deprecation note pointing to OrionKey. Do not delete the examples (the generator still works); annotate them.
- `docs/` feature guides that document `[StronglyTypedId]` (e.g. the v6.1/v6.2 feature docs) get a short deprecation banner at the relevant section, pointing to OrionKey. Long-form historical docs need only a banner, not a rewrite.

## 4. Migration guidance (for the CHANGELOG / docs)

A consumer migrating from OrionGuard `[StronglyTypedId]` to OrionKey `[OrionId]`:

1. `dotnet add package OrionKey`.
2. Replace `[StronglyTypedId<Guid>]` with `[OrionId<Guid>]`; `[StronglyTypedId<long>]` with `[OrionId<long>]` (externally-assigned) or `[OrionId<long, Snowflake>]` (generated); `[StronglyTypedId<string>]` with `[OrionId<string, Ulid>]` or `[OrionId<string, NanoId>]`.
3. Change the `using` from `Moongazing.OrionGuard.Domain.Primitives` to `Moongazing.OrionKey`.
4. The emitted surface (`Value`, `New()`, equality, EF Core `ValueConverter`, JSON converter, `TypeConverter`, `IParsable`) is equivalent; OrionKey additionally provides `IComparable` for sortable strategies and the `GuidV7`/`Snowflake`/`NanoId` strategies.

This guidance lives in the CHANGELOG `### Deprecated` note and, in fuller form, in a short `docs/migrations/stronglytypedid-to-orionkey.md` page.

## 5. Testing

- After 3.1, `dotnet build` the whole solution and confirm the *only* new diagnostics are CS0618 at known sites; suppress each per 3.2.
- `dotnet test` the full solution must stay green. The generator tests must still pass — the generator's behaviour is unchanged, so generated-output assertions hold; only diagnostic-free assertions (if any) may need a CS0618 filter.
- Add one focused generator test asserting that a `[StronglyTypedId]` usage is reported as obsolete — i.e. the harness compilation contains a CS0618 diagnostic. This locks in the deprecation so a future edit cannot silently un-deprecate the attribute.
- The demo must still run (`dotnet run --project demo/Moongazing.OrionGuard.Demo`) and print its strongly-typed-id section.

## 6. Out-of-scope confirmations

- No OrionKey dependency added to OrionGuard.
- No change to the manual `StronglyTypedId<TValue>` record, `IStronglyTypedId`, or the guards.
- No generator removal (v7.0.0).
- No new analyzer diagnostic ID.
- No version bump — this folds into the unshipped v6.4.0.
