# `[StronglyTypedId]` Soft-Deprecation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Soft-deprecate OrionGuard's `[StronglyTypedId<TValue>]` source generator in favour of the standalone OrionKey package — the injected attribute gets `[Obsolete]` (warning, not error), the generator keeps working, and consumers are pointed to OrionKey.

**Architecture:** A focused, additive change on the `feature/v6.4.0` branch. The only behavioural change is adding `[System.Obsolete]` to the attribute text the generator injects via `RegisterPostInitializationOutput`. OrionGuard's own usages of the attribute (the demo) are wrapped in a scoped `#pragma warning disable CS0618`. No OrionKey dependency is added. No generator logic changes.

**Tech Stack:** .NET 8/9/10, C#, Roslyn incremental source generator, xUnit.

**Spec:** `docs/superpowers/specs/2026-05-21-stronglytypedid-deprecation-design.md`

**Branch:** `feature/v6.4.0` (already checked out; the deprecation folds into the unshipped v6.4.0 release).

---

## Conventions

- **No `Co-Authored-By` trailer in commit messages. No emojis.**
- Commit after each task with the message in the task's final step.
- OrionGuard builds with `TreatWarningsAsErrors=true` — every task must end with a warning-clean `dotnet build`.
- Verification: `dotnet build Moongazing.OrionGuard.sln -c Release` and `dotnet test Moongazing.OrionGuard.sln -c Release` before each commit.

## File Structure

| Path | Change | Responsibility |
|---|---|---|
| `src/Moongazing.OrionGuard.Generators/StronglyTypedIds/StronglyTypedIdAttribute.cs` | Modify | Add `[Obsolete]` + revised XML summary to the injected attribute source text |
| `demo/Moongazing.OrionGuard.Demo/Domain/Ids.cs` | Modify | Wrap the three `[StronglyTypedId]` struct declarations in `#pragma warning disable CS0618` |
| `tests/Moongazing.OrionGuard.Generators.Tests/StronglyTypedIdDeprecationTests.cs` | Create | One test asserting a `[StronglyTypedId]` usage compiles to a CS0618 obsolete warning |
| `CHANGELOG.md` | Modify | Add a `### Deprecated` subsection to the `[6.4.0]` entry |
| `docs/migrations/stronglytypedid-to-orionkey.md` | Create | Migration guide from `[StronglyTypedId]` to OrionKey `[OrionId]` |
| `README.md` | Modify | Annotate `[StronglyTypedId]` mentions with a deprecation note |

## Background for the implementer

- The generator project `Moongazing.OrionGuard.Generators` injects the `[StronglyTypedId]` attribute into every consuming compilation. The text lives in `StronglyTypedIdAttribute.cs` as a verbatim string `StronglyTypedIdAttributeSource.Source`. The injected type is `Moongazing.OrionGuard.Domain.Primitives.StronglyTypedIdAttribute<TValue>`, `internal sealed`.
- The generator matches it via `ForAttributeWithMetadataName(StronglyTypedIdAttributeSource.FullName, ...)`. Adding `[Obsolete]` does not change the metadata name, so the generator keeps matching — no generator logic change is needed.
- `[Obsolete("msg")]` without an `error: true` argument produces a CS0618 **warning** at every usage site. That is the soft-deprecation signal.
- The generator unit tests (`StronglyTypedIdGenerator*.cs`) inspect `GeneratorDriverRunResult.Diagnostics` — the diagnostics the *generator* reports. CS0618 is a *compiler* diagnostic and never appears there, so those tests are unaffected and need no change.
- The demo project compiles with the generator active; its `[StronglyTypedId]` usages WILL raise CS0618 and, under `TreatWarningsAsErrors`, break the build unless suppressed.

---

## Task 1: Add `[Obsolete]` to the injected attribute

**Files:**
- Modify: `src/Moongazing.OrionGuard.Generators/StronglyTypedIds/StronglyTypedIdAttribute.cs`

- [ ] **Step 1: Read the current file**

Run: `sed -n '1,40p' src/Moongazing.OrionGuard.Generators/StronglyTypedIds/StronglyTypedIdAttribute.cs`

The file declares `StronglyTypedIdAttributeSource` with a `FullName` const and a `Source` verbatim-string const. The `Source` string contains, near its end:

```csharp
    /// <summary>
    /// Marks a readonly partial struct as a strongly-typed id backed by the specified primitive type.
    /// The OrionGuard source generator emits equality, comparison, conversion members, as well as
    /// EF Core ValueConverter, System.Text.Json converter, and TypeConverter companion types.
    /// </summary>
    /// <typeparam name=""TValue"">Underlying primitive type. Supported: System.Guid, int, long,
    /// string, System.Ulid (net9.0+).</typeparam>
    [System.AttributeUsage(System.AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
    internal sealed class StronglyTypedIdAttribute<TValue> : System.Attribute { }
```

(The exact wording of the summary may differ slightly — match what is actually there.)

- [ ] **Step 2: Replace the attribute declaration block inside the `Source` string**

Replace the `<summary>` / `<typeparam>` / `[AttributeUsage]` / class-declaration block shown above with this block. Note this text sits inside a C# verbatim string (`@"..."`), so any literal double-quote must be doubled (`""`). The `[Obsolete]` message below contains **no** double-quotes, so it needs no escaping; apostrophes are literal.

```csharp
    /// <summary>
    /// Marks a readonly partial struct as a strongly-typed id backed by the specified primitive type.
    /// </summary>
    /// <remarks>
    /// DEPRECATED. This generator is superseded by the standalone OrionKey package. Install
    /// the OrionKey NuGet package and use [OrionId&lt;TValue&gt;] or [OrionId&lt;TValue, TStrategy&gt;]
    /// instead. The OrionGuard generator still works but will be removed in v7.0.0.
    /// See https://www.nuget.org/packages/OrionKey.
    /// </remarks>
    /// <typeparam name=""TValue"">Underlying primitive type. Supported: System.Guid, int, long,
    /// string, System.Ulid (net9.0+).</typeparam>
    [System.Obsolete(""OrionGuard's [StronglyTypedId] is superseded by the standalone OrionKey package. Install OrionKey and use [OrionId<TValue>] or [OrionId<TValue, TStrategy>] instead - see https://www.nuget.org/packages/OrionKey. OrionGuard's generator will be removed in v7.0.0."")]
    [System.AttributeUsage(System.AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
    internal sealed class StronglyTypedIdAttribute<TValue> : System.Attribute { }
```

Key points:
- The `[System.Obsolete(...)]` line uses doubled double-quotes `""` because the whole thing is inside a verbatim string. The message itself contains no double-quotes.
- `[Obsolete]` has no `error: true` second argument — it is a warning, not an error.
- The `<remarks>` uses `&lt;` / `&gt;` for the angle brackets because they would otherwise be invalid XML inside the doc comment.

- [ ] **Step 3: Build the generator project**

Run: `dotnet build src/Moongazing.OrionGuard.Generators -c Release`
Expected: success, 0 warnings. (The generator project itself has no `[StronglyTypedId]` usage, so it stays clean.)

- [ ] **Step 4: Verify the generator still emits — build the core test project's dependency chain**

Run: `dotnet build src/Moongazing.OrionGuard.Generators -c Release` then confirm the `Source` const compiles by building a project that consumes the generator. Defer the full-solution build to Task 2 (the demo will fail until Task 2 suppresses CS0618 — that is expected and is Task 2's job).

- [ ] **Step 5: Commit**

```
git add src/Moongazing.OrionGuard.Generators/StronglyTypedIds/StronglyTypedIdAttribute.cs
git commit -m "feat(generators): mark [StronglyTypedId] attribute obsolete in favour of OrionKey

The injected StronglyTypedIdAttribute<TValue> now carries [Obsolete] (a
warning, not an error). Consumer usages raise CS0618 with migration guidance
pointing to the standalone OrionKey package. The generator is unchanged and
still emits every companion; it will be removed in v7.0.0."
```

---

## Task 2: Suppress CS0618 at OrionGuard's own `[StronglyTypedId]` usages

**Files:**
- Modify: `demo/Moongazing.OrionGuard.Demo/Domain/Ids.cs`
- Modify: any other file the full-solution build flags with CS0618 (see Step 2)

- [ ] **Step 1: Wrap the demo's generator-style declarations**

In `demo/Moongazing.OrionGuard.Demo/Domain/Ids.cs`, the "Style 1" section declares three structs with the now-obsolete attribute: `ProductId` (`[StronglyTypedId<Guid>]`), `SkuId` (`[StronglyTypedId<int>]`), `CountryCode` (`[StronglyTypedId<string>]`). The "Style 2" section uses the manual `StronglyTypedId<TValue>` record and is **not** affected.

Wrap only the Style-1 block. Place `#pragma warning disable CS0618` immediately before the first `[StronglyTypedId]` declaration and `#pragma warning restore CS0618` immediately after the last one, with an explanatory comment:

```csharp
// OrionGuard's [StronglyTypedId] is obsolete (superseded by OrionKey). The demo
// keeps exercising it because OrionGuard still ships and supports the generator
// through v6.x. Suppression is intentional and scoped to these declarations.
#pragma warning disable CS0618
[StronglyTypedId<Guid>]
public readonly partial struct ProductId;

[StronglyTypedId<int>]
public readonly partial struct SkuId;

[StronglyTypedId<string>]
public readonly partial struct CountryCode;
#pragma warning restore CS0618
```

Leave the Style-2 manual records (`OrderId`, `CustomerId`, `InvoiceId`) and all comments untouched.

- [ ] **Step 2: Build the whole solution and collect every remaining CS0618**

Run: `dotnet build Moongazing.OrionGuard.sln -c Release 2>&1 | grep -i "CS0618"`

Expected: ideally empty after Step 1. If any other file reports CS0618 (a core project, another test project, or a sample), it means OrionGuard has another in-repo `[StronglyTypedId]` usage. For each such file, wrap the offending declaration(s) in the same scoped `#pragma warning disable CS0618` / `restore CS0618` pair with a one-line comment explaining the suppression is deliberate. Do **not** disable the warning globally or in a csproj — keep each suppression local and commented.

The generator unit-test source files (`tests/Moongazing.OrionGuard.Generators.Tests/StronglyTypedIdGenerator*.cs`) contain `[StronglyTypedId]` only inside string literals fed to the generator harness — those are not attribute applications in the test assembly and produce no CS0618. If the build proves otherwise, treat them like any other site.

- [ ] **Step 3: Full solution build — must be warning-clean**

Run: `dotnet build Moongazing.OrionGuard.sln -c Release`
Expected: Build succeeded, 0 warnings, 0 errors.

- [ ] **Step 4: Full solution test**

Run: `dotnet test Moongazing.OrionGuard.sln -c Release`
Expected: all tests green — the generator behaviour is unchanged.

- [ ] **Step 5: Run the demo**

Run: `dotnet run --project demo/Moongazing.OrionGuard.Demo -c Release`
Expected: the demo runs and prints its strongly-typed-id section without error.

- [ ] **Step 6: Commit**

```
git add demo/Moongazing.OrionGuard.Demo/Domain/Ids.cs
git commit -m "chore(demo): scope-suppress CS0618 for the demo's [StronglyTypedId] usages

The demo still exercises the obsolete generator because OrionGuard supports it
through v6.x. Suppression is local to the three Style-1 declarations."
```

(If Step 2 found additional files, `git add` them in this commit too and mention them in the commit body.)

---

## Task 3: Add a deprecation test

**Files:**
- Create: `tests/Moongazing.OrionGuard.Generators.Tests/StronglyTypedIdDeprecationTests.cs`

This test locks in the deprecation: it compiles a `[StronglyTypedId]` usage and asserts the compiler reports CS0618. If a future edit removes `[Obsolete]`, this test fails.

- [ ] **Step 1: Inspect the existing generator-test harness**

Run: `sed -n '1,55p' tests/Moongazing.OrionGuard.Generators.Tests/StronglyTypedIdGeneratorTests.cs`

Note how it builds a `CSharpCompilation` (the `RunGenerator` helper assembles `MetadataReference`s from loaded assemblies, parses the source, creates the compilation, and runs the generator driver). The new test reuses the same compilation-building approach but additionally inspects the **post-generation compilation's** diagnostics for CS0618.

- [ ] **Step 2: Write the test**

Create `tests/Moongazing.OrionGuard.Generators.Tests/StronglyTypedIdDeprecationTests.cs`:

```csharp
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Moongazing.OrionGuard.Generators;
using Xunit;

namespace Moongazing.OrionGuard.Generators.Tests;

public class StronglyTypedIdDeprecationTests
{
    [Fact]
    public void StronglyTypedIdAttribute_ShouldRaiseCS0618_WhenApplied()
    {
        const string source = """
            using Moongazing.OrionGuard.Domain.Primitives;

            namespace App
            {
                [StronglyTypedId<System.Guid>]
                public readonly partial struct OrderId { }
            }
            """;

        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = System.AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location))
            .ToList();

        var compilation = CSharpCompilation.Create(
            "DeprecationTestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver
            .Create(new StronglyTypedIdGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out _);

        var cs0618 = updatedCompilation.GetDiagnostics()
            .Where(d => d.Id == "CS0618")
            .ToArray();

        Assert.NotEmpty(cs0618);
        Assert.Contains(cs0618, d => d.GetMessage().Contains("OrionKey"));
    }
}
```

Notes:
- `RunGeneratorsAndUpdateCompilation` produces `updatedCompilation` — the compilation *with* the generator's injected attribute and emitted companions. `GetDiagnostics()` on it includes the compiler's CS0618 for the obsolete-attribute usage.
- The test asserts both that CS0618 fires and that its message mentions OrionKey, so it cannot pass against a generic unrelated obsolete warning.
- If the existing tests reference `StronglyTypedIdGenerator` under a different namespace or the harness uses a different generator type name, match it — adjust the `using` and `new StronglyTypedIdGenerator()` accordingly. Verify against Step 1's reading of the existing test file.

- [ ] **Step 3: Run the test**

Run: `dotnet test tests/Moongazing.OrionGuard.Generators.Tests --filter StronglyTypedIdDeprecationTests`
Expected: 1 test passes.

- [ ] **Step 4: Full generator-test project run**

Run: `dotnet test tests/Moongazing.OrionGuard.Generators.Tests`
Expected: all generator tests pass (the existing ones plus the new one).

- [ ] **Step 5: Commit**

```
git add tests/Moongazing.OrionGuard.Generators.Tests/StronglyTypedIdDeprecationTests.cs
git commit -m "test(generators): assert [StronglyTypedId] usage raises CS0618

Locks in the OrionKey deprecation — fails if [Obsolete] is ever removed
from the injected attribute."
```

---

## Task 4: CHANGELOG and migration guide

**Files:**
- Modify: `CHANGELOG.md`
- Create: `docs/migrations/stronglytypedid-to-orionkey.md`

- [ ] **Step 1: Add a `### Deprecated` subsection to the `[6.4.0]` CHANGELOG entry**

Open `CHANGELOG.md`. Find the `## [6.4.0]` section. It already has `### Added` / `### Changed` / `### Migration from v6.3.0` subsections. Add a `### Deprecated` subsection immediately after `### Changed` (Keep-a-Changelog orders sections Added / Changed / Deprecated / Removed / Fixed / Security):

```markdown
### Deprecated

- The `[StronglyTypedId<TValue>]` source generator is soft-deprecated in favour of the standalone **OrionKey** package (`[OrionId<TValue>]` / `[OrionId<TValue, TStrategy>]`). Existing usages keep compiling and the generator keeps emitting; each `[StronglyTypedId]` usage now raises a CS0618 warning with migration guidance. The generator will be removed in v7.0.0. The manual `StronglyTypedId<TValue>` record, `IStronglyTypedId<TValue>`, and the related guards are unaffected. See `docs/migrations/stronglytypedid-to-orionkey.md`.
```

- [ ] **Step 2: Create the migration guide**

Create `docs/migrations/stronglytypedid-to-orionkey.md`:

```markdown
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
```

- [ ] **Step 3: Verify the docs build (no build step needed — Markdown only) and commit**

```
git add CHANGELOG.md docs/migrations/stronglytypedid-to-orionkey.md
git commit -m "docs(v6.4.0): changelog Deprecated entry and OrionKey migration guide"
```

---

## Task 5: Annotate `[StronglyTypedId]` mentions in the README

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Find every `[StronglyTypedId]` / strongly-typed-id mention in the README**

Run: `grep -n -i "stronglytypedid\|strongly-typed" README.md`

- [ ] **Step 2: Add a deprecation note**

For each section or table row that documents the `[StronglyTypedId]` **generator** (not the manual record), add a concise inline note. Do not delete the examples — the generator still works. A blockquote near the first mention is enough; individual table rows can get a trailing `(deprecated — see OrionKey)`:

```markdown
> **Deprecated in v6.4.0.** The `[StronglyTypedId]` source generator is superseded by the standalone [OrionKey](https://github.com/tunahanaliozturk/OrionKey) package (`[OrionId]`). It still works through the v6.x line and is removed in v7.0.0. See [the migration guide](docs/migrations/stronglytypedid-to-orionkey.md). The manual `StronglyTypedId<TValue>` record is not affected.
```

Place the blockquote at the strongly-typed-id section. If the README only mentions it in a feature table, add the note as a footnote under the table. Keep it factual and short — no emojis.

- [ ] **Step 3: Build and test the solution one final time**

Run: `dotnet build Moongazing.OrionGuard.sln -c Release` then `dotnet test Moongazing.OrionGuard.sln -c Release`
Expected: build warning-clean, all tests green.

- [ ] **Step 4: Commit**

```
git add README.md
git commit -m "docs(readme): note [StronglyTypedId] deprecation and point to OrionKey"
```

---

## Final verification

- [ ] `dotnet build Moongazing.OrionGuard.sln -c Release` — 0 warnings, 0 errors.
- [ ] `dotnet test Moongazing.OrionGuard.sln -c Release` — all green, including the new `StronglyTypedIdDeprecationTests`.
- [ ] `dotnet run --project demo/Moongazing.OrionGuard.Demo -c Release` — runs and prints the strongly-typed-id section.
- [ ] `git log --oneline` — five task commits on `feature/v6.4.0`.

---

## Self-Review

**Spec coverage:**

| Spec section | Task |
|---|---|
| §3.1 `[Obsolete]` on injected attribute + XML doc | Task 1 |
| §3.2 suppress OrionGuard's own usages | Task 2 |
| §3.3 CHANGELOG `### Deprecated` | Task 4 |
| §3.4 README + docs annotation | Task 4 (migration guide), Task 5 (README) |
| §4 migration guidance | Task 4 (`docs/migrations/stronglytypedid-to-orionkey.md`) |
| §5 testing — deprecation test + green build/test/demo | Task 3 (test), Tasks 2 & 5 (build/test/demo verification) |

Every spec section maps to a task. §3.2's "investigate generator tests" concern is resolved in the background notes (CS0618 is a compiler diagnostic; `GeneratorDriverRunResult.Diagnostics` only carries generator-reported diagnostics) and Task 2 Step 2 still does the empirical full-build sweep as a safety net.

**Placeholder scan:** No `TBD`/`TODO`. Task 2 Step 2 is an empirical "build and collect" step with a concrete fallback action, not a placeholder. Task 5 Step 1/2 depend on the README's actual content, which the engineer greps for — the action (add the given blockquote) is fully specified.

**Type consistency:** The injected attribute name `StronglyTypedIdAttribute<TValue>`, the generator type `StronglyTypedIdGenerator`, the diagnostic id `CS0618`, and the `[OrionId]` target names are used consistently across all tasks.
