# OrionGuard v6.2.0 — API Polish Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship v6.2.0 of OrionGuard — four coordinated API-polish features that close the ergonomic gaps surfaced by building the v6.1.0 demo end-to-end: conditional EF Core converter emission, an `IStronglyTypedId<TValue>` marker interface unifying source-gen structs and manual records, a `DomainEventBase` record, and `IParsable<TSelf>` / `ISpanParsable<TSelf>` on generated IDs for ASP.NET Core minimal API binding.

**Architecture:** All changes land in existing packages (`Moongazing.OrionGuard` core + `Moongazing.OrionGuard.Generators`). No new NuGet packages. Zero breaking changes for v6.1.0 source consumers — the guard extension's receiver widens from `StronglyTypedId<TValue>` (abstract record) to `IStronglyTypedId<TValue>` (new interface the record now implements). A new `ParsableEmitter` produces a secondary partial struct file per target; an EF Core detection hop in the generator's pipeline gates the existing `EfCoreConverterEmitter`. Ecosystem version bumps to 6.2.0 in lockstep.

**Tech Stack:** .NET 8 / 9 / 10 multi-targeting (core), netstandard2.0 (Roslyn generator), xUnit (tests), BenchmarkDotNet (benchmarks — unchanged). Uses existing `Compilation.GetTypeByMetadataName` API and `IncrementalGeneratorInitializationContext.CompilationProvider`.

**Spec Reference:** [`docs/superpowers/specs/2026-04-19-orionguard-v6.2.0-api-polish-design.md`](../specs/2026-04-19-orionguard-v6.2.0-api-polish-design.md).

**Branch:** `feature/v6.2.0-api-polish` (already created off `feature/v6.0`).

---

## File Structure

**Core package — `src/Moongazing.OrionGuard/`:**

```
Domain/
├── Primitives/
│   ├── IStronglyTypedId.cs            [NEW — marker interface]
│   └── StronglyTypedId.cs             [MODIFY — declaration adds : IStronglyTypedId<TValue>]
├── Events/
│   └── DomainEventBase.cs             [NEW — abstract record implementing IDomainEvent]
└── (Entity.cs, AggregateRoot.cs, IAggregateRoot.cs, Rules/, Exceptions/, Events/IDomainEvent.cs — unchanged)

Extensions/
└── StronglyTypedIdGuards.cs           [MODIFY — receiver widens to IStronglyTypedId<TValue>]
```

**Generators package — `src/Moongazing.OrionGuard.Generators/StronglyTypedIds/`:**

```
StronglyTypedIdGenerator.cs            [MODIFY — CompilationProvider detects EF Core; wires ParsableEmitter]
StronglyTypedIdEmitter.cs              [MODIFY — primary struct interface list grows]
ParsableEmitter.cs                     [NEW — emits IParsable + ISpanParsable secondary partial]
EfCoreConverterEmitter.cs              [UNCHANGED]
JsonConverterEmitter.cs                [UNCHANGED]
TypeConverterEmitter.cs                [UNCHANGED]
SupportedValueType.cs                  [UNCHANGED]
StronglyTypedIdAttribute.cs            [UNCHANGED]
```

**Core test project — `tests/Moongazing.OrionGuard.Tests/`:**

```
IStronglyTypedIdInterfaceTests.cs     [NEW]
DomainEventBaseTests.cs                [NEW]
StronglyTypedIdGuardTests.cs           [MODIFY — add interface-typed-receiver test]
```

**Generator test project — `tests/Moongazing.OrionGuard.Generators.Tests/`:**

```
StronglyTypedIdGeneratorConditionalEfCoreTests.cs  [NEW]
StronglyTypedIdGeneratorParsableTests.cs           [NEW]
StronglyTypedIdGeneratorTests.cs                   [MODIFY — asserts IStronglyTypedId in interface list]
```

**Demo — `demo/Moongazing.OrionGuard.Demo/`:**

```
Moongazing.OrionGuard.Demo.csproj      [MODIFY — drop Microsoft.EntityFrameworkCore PackageReference]
Program.cs                              [MODIFY — section 17 message reflects conditional emission]
```

**Docs + release — repo root and `docs/`:**

```
CHANGELOG.md                            [MODIFY — v6.2.0 entry]
README.md                               [MODIFY — short v6.2 note under v6.1 section]
docs/FEATURES-v6.1.md                   [MODIFY — append "v6.2 Additions" section]
docs/ROADMAP.md                         [MODIFY — shift v6.2 → v6.3, v6.3 → v6.4]
All 9 *.csproj                          [MODIFY — Version 6.1.0 → 6.2.0]
src/Moongazing.OrionGuard/Moongazing.OrionGuard.csproj  [MODIFY — PackageReleaseNotes gains v6.2.0 block]
```

---

## Conventions (apply to every task)

- **Test name pattern:** `<Method>_Should<ExpectedOutcome>_When<Condition>` matching existing suite style.
- **xUnit global using** is already configured; do NOT add `using Xunit;`.
- **Build command:** `dotnet build <csproj> -c Release` — all new public members need XML docs because the core and Generators projects both set `TreatWarningsAsErrors=true` + `GenerateDocumentationFile=true`.
- **CA1510 enforced in core project** — always use `ArgumentNullException.ThrowIfNull(x)` (not manual `if (x is null) throw`).
- **Commit messages** follow Conventional Commits (`feat`, `fix`, `test`, `docs`, `refactor`, `chore`).
- **Bash env note:** On this Windows machine, bare `git` / `dotnet` may not resolve — use `"/c/Program Files/Git/cmd/git.exe"` and `"/c/Program Files/dotnet/dotnet.exe"` if you hit `command not found`.
- **Generator project targets `netstandard2.0`** — no file-scoped namespaces, `LangVersion=latest` is set (records + switch expressions are available), but stay conservative.

---

## Task 1: `IStronglyTypedId<TValue>` marker interface

**Files:**
- Create: `src/Moongazing.OrionGuard/Domain/Primitives/IStronglyTypedId.cs`
- Create: `tests/Moongazing.OrionGuard.Tests/IStronglyTypedIdInterfaceTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/Moongazing.OrionGuard.Tests/IStronglyTypedIdInterfaceTests.cs`:

```csharp
using Moongazing.OrionGuard.Domain.Primitives;

namespace Moongazing.OrionGuard.Tests;

public class IStronglyTypedIdInterfaceTests
{
    private sealed record InvoiceId(Guid Value) : StronglyTypedId<Guid>(Value);

    [Fact]
    public void StronglyTypedId_ShouldBeAssignableToIStronglyTypedId_WhenConstructed()
    {
        var id = new InvoiceId(Guid.NewGuid());

        IStronglyTypedId<Guid> asInterface = id;

        Assert.NotNull(asInterface);
        Assert.Equal(id.Value, asInterface.Value);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Moongazing.OrionGuard.Tests/Moongazing.OrionGuard.Tests.csproj --filter FullyQualifiedName~IStronglyTypedIdInterfaceTests`
Expected: FAIL — `error CS0246: The type or namespace name 'IStronglyTypedId<>' could not be found`.

- [ ] **Step 3: Create `IStronglyTypedId.cs`**

Create `src/Moongazing.OrionGuard/Domain/Primitives/IStronglyTypedId.cs`:

```csharp
using System;

namespace Moongazing.OrionGuard.Domain.Primitives;

/// <summary>
/// Marker interface implemented by both the <see cref="StronglyTypedId{TValue}"/> abstract
/// record (manual-use style) and by source-generated readonly partial struct identifiers
/// produced by the <c>[StronglyTypedId&lt;TValue&gt;]</c> generator. Provides a single
/// abstraction that guard extensions and other consumers can target regardless of which
/// declaration style the user chose.
/// </summary>
/// <typeparam name="TValue">Underlying primitive type (e.g. <see cref="Guid"/>, <see cref="int"/>,
/// <see cref="long"/>, <see cref="string"/>).</typeparam>
public interface IStronglyTypedId<TValue>
    where TValue : notnull, IEquatable<TValue>
{
    /// <summary>Gets the wrapped primitive value.</summary>
    TValue Value { get; }
}
```

- [ ] **Step 4: Run test (still fails — InvoiceId doesn't implement the interface yet)**

Run: `dotnet test tests/Moongazing.OrionGuard.Tests/Moongazing.OrionGuard.Tests.csproj --filter FullyQualifiedName~IStronglyTypedIdInterfaceTests`
Expected: FAIL — `error CS0266: Cannot implicitly convert type 'InvoiceId' to 'IStronglyTypedId<Guid>'`. This is expected — `StronglyTypedId<TValue>` doesn't implement the new interface yet; Task 2 fixes that.

- [ ] **Step 5: Commit (interface only)**

```bash
git add src/Moongazing.OrionGuard/Domain/Primitives/IStronglyTypedId.cs \
        tests/Moongazing.OrionGuard.Tests/IStronglyTypedIdInterfaceTests.cs
git commit -m "feat(domain): add IStronglyTypedId<TValue> marker interface"
```

---

## Task 2: `StronglyTypedId<TValue>` implements `IStronglyTypedId<TValue>`

**Files:**
- Modify: `src/Moongazing.OrionGuard/Domain/Primitives/StronglyTypedId.cs`

- [ ] **Step 1: Modify the declaration**

Open `src/Moongazing.OrionGuard/Domain/Primitives/StronglyTypedId.cs`. Change the declaration line:

```csharp
public abstract record StronglyTypedId<TValue>(TValue Value)
    where TValue : notnull, IEquatable<TValue>;
```

to:

```csharp
public abstract record StronglyTypedId<TValue>(TValue Value) : IStronglyTypedId<TValue>
    where TValue : notnull, IEquatable<TValue>;
```

The `Value` property is already synthesised by the positional-record primary constructor, so no body change is required.

- [ ] **Step 2: Run tests — Task 1 test + existing StronglyTypedIdTests must pass**

Run: `dotnet test tests/Moongazing.OrionGuard.Tests/Moongazing.OrionGuard.Tests.csproj --filter "FullyQualifiedName~StronglyTypedId"`
Expected: PASS — the 1 new test from Task 1 + the 4 existing `StronglyTypedIdTests` + the 6 existing `StronglyTypedIdGuardTests` = 11 tests passing.

- [ ] **Step 3: Run full test suite as regression guard**

Run: `dotnet test tests/Moongazing.OrionGuard.Tests/Moongazing.OrionGuard.Tests.csproj --verbosity quiet`
Expected: PASS — all 502+ tests still green.

- [ ] **Step 4: Commit**

```bash
git add src/Moongazing.OrionGuard/Domain/Primitives/StronglyTypedId.cs
git commit -m "feat(domain): StronglyTypedId<TValue> implements IStronglyTypedId<TValue>"
```

---

## Task 3: `AgainstDefaultStronglyTypedId` receiver widens to interface

**Files:**
- Modify: `src/Moongazing.OrionGuard/Extensions/StronglyTypedIdGuards.cs`
- Modify: `tests/Moongazing.OrionGuard.Tests/StronglyTypedIdGuardTests.cs`

- [ ] **Step 1: Append the failing test**

Append inside the `StronglyTypedIdGuardTests` class (before the closing brace), after the existing 6 `[Fact]` methods:

```csharp
    [Fact]
    public void DefaultStronglyTypedId_ShouldWork_WhenReceiverIsTypedAsInterface()
    {
        IStronglyTypedId<Guid> id = new OrderId(Guid.NewGuid());

        var returned = id.AgainstDefaultStronglyTypedId(nameof(id));

        Assert.Same(id, returned);
    }
```

Also add `using Moongazing.OrionGuard.Domain.Primitives;` to the top of the file if not already present (it should already be — `OrderId` already references it).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Moongazing.OrionGuard.Tests/Moongazing.OrionGuard.Tests.csproj --filter FullyQualifiedName~DefaultStronglyTypedId_ShouldWork_WhenReceiverIsTypedAsInterface`
Expected: FAIL — extension method's receiver is `StronglyTypedId<TValue>`, so it cannot be called on `IStronglyTypedId<TValue>` directly. Compiler error `CS1061` or `CS0411`.

- [ ] **Step 3: Update `StronglyTypedIdGuards.cs`**

Open `src/Moongazing.OrionGuard/Extensions/StronglyTypedIdGuards.cs`. The full file after change is:

```csharp
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Moongazing.OrionGuard.Domain.Primitives;
using Moongazing.OrionGuard.Exceptions;

namespace Moongazing.OrionGuard.Extensions;

/// <summary>
/// Guard extensions for <see cref="IStronglyTypedId{TValue}"/>-implementing types — covers
/// both the <see cref="StronglyTypedId{TValue}"/> abstract record (manual style) and the
/// source-generated readonly partial struct style.
/// </summary>
public static class StronglyTypedIdGuards
{
    /// <summary>
    /// Throws when <paramref name="id"/> is null or its wrapped value equals the default of its underlying type
    /// (<see cref="Guid.Empty"/>, <c>0</c>, <c>""</c>).
    /// </summary>
    /// <typeparam name="TValue">Underlying primitive type of the strongly-typed identifier.</typeparam>
    /// <param name="id">The strongly-typed identifier to validate.</param>
    /// <param name="parameterName">The parameter name (for error messages).</param>
    /// <returns>The validated <paramref name="id"/> for chaining.</returns>
    /// <exception cref="NullValueException">When <paramref name="id"/> is <see langword="null"/>.</exception>
    /// <exception cref="ZeroValueException">When the wrapped value is the default of <typeparamref name="TValue"/>
    /// (including empty string).</exception>
    public static IStronglyTypedId<TValue> AgainstDefaultStronglyTypedId<TValue>(
        this IStronglyTypedId<TValue> id,
        [CallerArgumentExpression(nameof(id))] string? parameterName = null)
        where TValue : notnull, IEquatable<TValue>
    {
        if (id is null)
        {
            throw new NullValueException(parameterName ?? nameof(id));
        }

        if (EqualityComparer<TValue>.Default.Equals(id.Value, default!) ||
            (id.Value is string s && string.IsNullOrEmpty(s)))
        {
            throw new ZeroValueException(parameterName ?? nameof(id));
        }

        return id;
    }
}
```

The only behavioural changes vs v6.1.0 are the receiver type (`StronglyTypedId<TValue>` → `IStronglyTypedId<TValue>`) and return type (same widening). Method body is unchanged.

- [ ] **Step 4: Run tests to verify all 7 pass**

Run: `dotnet test tests/Moongazing.OrionGuard.Tests/Moongazing.OrionGuard.Tests.csproj --filter FullyQualifiedName~StronglyTypedIdGuardTests`
Expected: PASS — 7 tests (6 existing + 1 new).

- [ ] **Step 5: Commit**

```bash
git add src/Moongazing.OrionGuard/Extensions/StronglyTypedIdGuards.cs \
        tests/Moongazing.OrionGuard.Tests/StronglyTypedIdGuardTests.cs
git commit -m "feat(guard): widen AgainstDefaultStronglyTypedId receiver to IStronglyTypedId<TValue>"
```

---

## Task 4: Generator emits `IStronglyTypedId<TValue>` on generated struct

**Files:**
- Modify: `src/Moongazing.OrionGuard.Generators/StronglyTypedIds/StronglyTypedIdEmitter.cs`
- Modify: `tests/Moongazing.OrionGuard.Generators.Tests/StronglyTypedIdGeneratorTests.cs`

- [ ] **Step 1: Append the failing test**

Append to `tests/Moongazing.OrionGuard.Generators.Tests/StronglyTypedIdGeneratorTests.cs` inside the class:

```csharp
    [Fact]
    public void Generator_ShouldEmitIStronglyTypedIdInterface_OnGeneratedStruct()
    {
        const string source = """
            using Moongazing.OrionGuard.Domain.Primitives;

            namespace App
            {
                [StronglyTypedId<System.Guid>]
                public readonly partial struct WarehouseId { }
            }
            """;

        var result = RunGenerator(source);

        var partialSource = result.Results
            .SelectMany(r => r.GeneratedSources)
            .FirstOrDefault(s => s.HintName.Contains("WarehouseId.StronglyTypedId"));

        Assert.NotEqual(default, partialSource);
        var text = partialSource.SourceText.ToString();
        Assert.Contains("global::Moongazing.OrionGuard.Domain.Primitives.IStronglyTypedId<global::System.Guid>", text);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Moongazing.OrionGuard.Generators.Tests/Moongazing.OrionGuard.Generators.Tests.csproj --filter FullyQualifiedName~Generator_ShouldEmitIStronglyTypedIdInterface_OnGeneratedStruct`
Expected: FAIL — current emitter doesn't include the interface.

- [ ] **Step 3: Update `StronglyTypedIdEmitter.cs`**

Open `src/Moongazing.OrionGuard.Generators/StronglyTypedIds/StronglyTypedIdEmitter.cs`. Find the line that emits the struct declaration (currently `: global::System.IEquatable<{typeName}>`):

```csharp
sb.Append("    public readonly partial struct ").Append(typeName)
  .Append(" : global::System.IEquatable<").Append(typeName).AppendLine(">")
  .AppendLine("    {");
```

Replace with a two-interface list that includes `IStronglyTypedId<TValue>`:

```csharp
sb.Append("    public readonly partial struct ").Append(typeName)
  .Append(" : global::System.IEquatable<").Append(typeName).Append(">")
  .Append(", global::Moongazing.OrionGuard.Domain.Primitives.IStronglyTypedId<").Append(valueCsKeyword).AppendLine(">")
  .AppendLine("    {");
```

Do NOT touch any other line — the rest of the emitter (Value property, ctor, equality members, New()/Empty) already uses `valueCsKeyword` and stays as-is.

- [ ] **Step 4: Run tests (interface assertion + existing generator tests)**

Run: `dotnet test tests/Moongazing.OrionGuard.Generators.Tests/Moongazing.OrionGuard.Generators.Tests.csproj --verbosity quiet`
Expected: PASS — 6 tests (5 existing + 1 new).

- [ ] **Step 5: Commit**

```bash
git add src/Moongazing.OrionGuard.Generators/StronglyTypedIds/StronglyTypedIdEmitter.cs \
        tests/Moongazing.OrionGuard.Generators.Tests/StronglyTypedIdGeneratorTests.cs
git commit -m "feat(generators): generated struct implements IStronglyTypedId<TValue>"
```

---

## Task 5: `DomainEventBase` abstract record

**Files:**
- Create: `src/Moongazing.OrionGuard/Domain/Events/DomainEventBase.cs`
- Create: `tests/Moongazing.OrionGuard.Tests/DomainEventBaseTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/Moongazing.OrionGuard.Tests/DomainEventBaseTests.cs`:

```csharp
using Moongazing.OrionGuard.Domain.Events;

namespace Moongazing.OrionGuard.Tests;

public class DomainEventBaseTests
{
    private sealed record OrderPlaced(int OrderNumber) : DomainEventBase;

    [Fact]
    public void DomainEventBase_ShouldAssignNonEmptyEventId_WhenConstructed()
    {
        var evt = new OrderPlaced(42);

        Assert.NotEqual(Guid.Empty, evt.EventId);
    }

    [Fact]
    public void DomainEventBase_ShouldAssignUtcTimestamp_WhenConstructed()
    {
        var before = DateTime.UtcNow;
        var evt = new OrderPlaced(42);
        var after = DateTime.UtcNow;

        Assert.InRange(evt.OccurredOnUtc, before, after);
        Assert.Equal(DateTimeKind.Utc, evt.OccurredOnUtc.Kind);
    }

    [Fact]
    public void DomainEventBase_ShouldAllowTestOverrides_ViaWithExpression()
    {
        var fixedId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var fixedTimestamp = new DateTime(2026, 4, 19, 10, 0, 0, DateTimeKind.Utc);

        var evt = new OrderPlaced(42) with { EventId = fixedId, OccurredOnUtc = fixedTimestamp };

        Assert.Equal(fixedId, evt.EventId);
        Assert.Equal(fixedTimestamp, evt.OccurredOnUtc);
    }

    [Fact]
    public void DomainEventBase_ShouldImplementIDomainEvent_WhenUpcast()
    {
        IDomainEvent evt = new OrderPlaced(42);

        Assert.NotEqual(Guid.Empty, evt.EventId);
        Assert.NotEqual(default, evt.OccurredOnUtc);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Moongazing.OrionGuard.Tests/Moongazing.OrionGuard.Tests.csproj --filter FullyQualifiedName~DomainEventBaseTests`
Expected: FAIL — `error CS0246: The type or namespace name 'DomainEventBase' could not be found`.

- [ ] **Step 3: Create `DomainEventBase.cs`**

Create `src/Moongazing.OrionGuard/Domain/Events/DomainEventBase.cs`:

```csharp
using System;

namespace Moongazing.OrionGuard.Domain.Events;

/// <summary>
/// Base record for domain events. Assigns a fresh <see cref="EventId"/> and a UTC
/// <see cref="OccurredOnUtc"/> timestamp at construction time; both members use
/// <see langword="init"/> accessors so tests can pin them via <c>with</c> expressions.
/// </summary>
/// <remarks>
/// Consumers write <c>public sealed record OrderPlaced(OrderId Id) : DomainEventBase;</c>
/// and the canonical <see cref="IDomainEvent"/> members are populated automatically.
/// The event dispatcher (<c>IDomainEventDispatcher</c>, MediatR bridge, EF Core
/// interceptor) arrives in v6.3.0 and operates on the <see cref="IDomainEvent"/>
/// abstraction — this record is orthogonal to dispatch wiring.
/// </remarks>
public abstract record DomainEventBase : IDomainEvent
{
    /// <summary>Globally unique identifier for this event instance.</summary>
    public Guid EventId { get; init; } = Guid.NewGuid();

    /// <summary>Timestamp in UTC at which the event was raised.</summary>
    public DateTime OccurredOnUtc { get; init; } = DateTime.UtcNow;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Moongazing.OrionGuard.Tests/Moongazing.OrionGuard.Tests.csproj --filter FullyQualifiedName~DomainEventBaseTests`
Expected: PASS — 4 tests passed.

- [ ] **Step 5: Commit**

```bash
git add src/Moongazing.OrionGuard/Domain/Events/DomainEventBase.cs \
        tests/Moongazing.OrionGuard.Tests/DomainEventBaseTests.cs
git commit -m "feat(domain): add DomainEventBase abstract record with init EventId/OccurredOnUtc"
```

---

## Task 6: Conditional EF Core converter emission

**Files:**
- Modify: `src/Moongazing.OrionGuard.Generators/StronglyTypedIds/StronglyTypedIdGenerator.cs`
- Create: `tests/Moongazing.OrionGuard.Generators.Tests/StronglyTypedIdGeneratorConditionalEfCoreTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/Moongazing.OrionGuard.Generators.Tests/StronglyTypedIdGeneratorConditionalEfCoreTests.cs`:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Moongazing.OrionGuard.Generators.StronglyTypedIds;

namespace Moongazing.OrionGuard.Generators.Tests;

public class StronglyTypedIdGeneratorConditionalEfCoreTests
{
    private static GeneratorDriverRunResult RunGenerator(string source, bool includeEfCoreReference)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location));

        if (!includeEfCoreReference)
        {
            references = references.Where(a =>
                !a.GetName().Name!.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
        }

        var metadataRefs = references
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToArray();

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            metadataRefs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new StronglyTypedIdGenerator().AsSourceGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGenerators(compilation);
        return driver.GetRunResult();
    }

    [Fact]
    public void Generator_ShouldSkipEfCoreConverter_WhenEfCoreNotReferenced()
    {
        const string source = """
            using Moongazing.OrionGuard.Domain.Primitives;

            namespace App
            {
                [StronglyTypedId<System.Guid>]
                public readonly partial struct TenantId { }
            }
            """;

        var result = RunGenerator(source, includeEfCoreReference: false);

        var efCoreConverter = result.Results
            .SelectMany(r => r.GeneratedSources)
            .FirstOrDefault(s => s.HintName.Contains("TenantIdEfCoreValueConverter"));

        Assert.Equal(default, efCoreConverter);

        // Partial body, JSON, TypeConverter must still be emitted.
        var partialBody = result.Results.SelectMany(r => r.GeneratedSources)
            .FirstOrDefault(s => s.HintName.Contains("TenantId.StronglyTypedId"));
        Assert.NotEqual(default, partialBody);

        var jsonConverter = result.Results.SelectMany(r => r.GeneratedSources)
            .FirstOrDefault(s => s.HintName.Contains("TenantIdJsonConverter"));
        Assert.NotEqual(default, jsonConverter);

        var typeConverter = result.Results.SelectMany(r => r.GeneratedSources)
            .FirstOrDefault(s => s.HintName.Contains("TenantIdTypeConverter"));
        Assert.NotEqual(default, typeConverter);
    }

    [Fact]
    public void Generator_ShouldEmitEfCoreConverter_WhenEfCoreReferenced()
    {
        const string source = """
            using Moongazing.OrionGuard.Domain.Primitives;

            namespace App
            {
                [StronglyTypedId<System.Guid>]
                public readonly partial struct TenantId { }
            }
            """;

        var result = RunGenerator(source, includeEfCoreReference: true);

        var efCoreConverter = result.Results
            .SelectMany(r => r.GeneratedSources)
            .FirstOrDefault(s => s.HintName.Contains("TenantIdEfCoreValueConverter"));

        Assert.NotEqual(default, efCoreConverter);
    }
}
```

- [ ] **Step 2: Run first test to verify it fails (EF Core converter IS emitted today)**

Run: `dotnet test tests/Moongazing.OrionGuard.Generators.Tests/Moongazing.OrionGuard.Generators.Tests.csproj --filter FullyQualifiedName~Generator_ShouldSkipEfCoreConverter_WhenEfCoreNotReferenced`
Expected: FAIL — today the generator emits the EF Core converter unconditionally.

- [ ] **Step 3: Update `StronglyTypedIdGenerator.cs`**

Open `src/Moongazing.OrionGuard.Generators/StronglyTypedIds/StronglyTypedIdGenerator.cs`. Replace the entire body of `Initialize` with:

```csharp
public void Initialize(IncrementalGeneratorInitializationContext context)
{
    context.RegisterPostInitializationOutput(ctx =>
        ctx.AddSource(
            "StronglyTypedIdAttribute.g.cs",
            SourceText.From(StronglyTypedIdAttributeSource.Source, Encoding.UTF8)));

    var targets = context.SyntaxProvider
        .ForAttributeWithMetadataName(
            StronglyTypedIdAttributeSource.FullName + "`1",
            predicate: static (node, _) => node is StructDeclarationSyntax sds
                && sds.Modifiers.Any(m => m.ValueText == "partial")
                && sds.Modifiers.Any(m => m.ValueText == "readonly"),
            transform: static (ctx, _) => Transform(ctx))
        .Where(static t => t is not null);

    var hasEfCore = context.CompilationProvider
        .Select(static (compilation, _) =>
            compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter`2") is not null);

    context.RegisterSourceOutput(targets.Combine(hasEfCore), static (spc, pair) =>
    {
        var target = pair.Left;
        var efCoreAvailable = pair.Right;
        if (target is null) return;

        spc.AddSource(
            $"{target.TypeName}.StronglyTypedId.g.cs",
            SourceText.From(
                StronglyTypedIdEmitter.EmitPartial(target.Namespace, target.TypeName, target.ValueType),
                Encoding.UTF8));

        if (efCoreAvailable)
        {
            spc.AddSource(
                EfCoreConverterEmitter.HintName(target.TypeName),
                SourceText.From(
                    EfCoreConverterEmitter.Emit(target.Namespace, target.TypeName, target.ValueType),
                    Encoding.UTF8));
        }

        spc.AddSource(
            JsonConverterEmitter.HintName(target.TypeName),
            SourceText.From(
                JsonConverterEmitter.Emit(target.Namespace, target.TypeName, target.ValueType),
                Encoding.UTF8));

        spc.AddSource(
            TypeConverterEmitter.HintName(target.TypeName),
            SourceText.From(
                TypeConverterEmitter.Emit(target.Namespace, target.TypeName, target.ValueType),
                Encoding.UTF8));
    });
}
```

The only diff vs the v6.1 implementation: (a) the `hasEfCore` `CompilationProvider`-based `IncrementalValueProvider<bool>` is introduced, (b) `targets.Combine(hasEfCore)` passes the flag, (c) `EfCoreConverterEmitter` emission is gated on `efCoreAvailable`. JSON and TypeConverter emissions stay unconditional.

- [ ] **Step 4: Run all generator tests**

Run: `dotnet test tests/Moongazing.OrionGuard.Generators.Tests/Moongazing.OrionGuard.Generators.Tests.csproj`
Expected: PASS — 8 tests (6 existing + 2 new conditional-EF-Core tests).

- [ ] **Step 5: Commit**

```bash
git add src/Moongazing.OrionGuard.Generators/StronglyTypedIds/StronglyTypedIdGenerator.cs \
        tests/Moongazing.OrionGuard.Generators.Tests/StronglyTypedIdGeneratorConditionalEfCoreTests.cs
git commit -m "feat(generators): skip EF Core ValueConverter emission when EF Core not referenced"
```

---

## Task 7: `ParsableEmitter` — emit `IParsable<TSelf>` / `ISpanParsable<TSelf>` implementations

**Files:**
- Create: `src/Moongazing.OrionGuard.Generators/StronglyTypedIds/ParsableEmitter.cs`
- Create: `tests/Moongazing.OrionGuard.Generators.Tests/StronglyTypedIdGeneratorParsableTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/Moongazing.OrionGuard.Generators.Tests/StronglyTypedIdGeneratorParsableTests.cs`:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Moongazing.OrionGuard.Generators.StronglyTypedIds;

namespace Moongazing.OrionGuard.Generators.Tests;

public class StronglyTypedIdGeneratorParsableTests
{
    private static GeneratorDriverRunResult RunGenerator(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToArray();
        var compilation = CSharpCompilation.Create("TestAssembly", new[] { syntaxTree }, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var driver = CSharpGeneratorDriver.Create(new StronglyTypedIdGenerator().AsSourceGenerator());
        return driver.RunGenerators(compilation).GetRunResult();
    }

    [Fact]
    public void Generator_ShouldEmitParsableMembers_ForGuidBackedId()
    {
        const string source = """
            using Moongazing.OrionGuard.Domain.Primitives;

            namespace App
            {
                [StronglyTypedId<System.Guid>]
                public readonly partial struct ShipmentId { }
            }
            """;

        var result = RunGenerator(source);

        var parsable = result.Results.SelectMany(r => r.GeneratedSources)
            .FirstOrDefault(s => s.HintName.Contains("ShipmentId.Parsable"));

        Assert.NotEqual(default, parsable);
        var text = parsable.SourceText.ToString();

        Assert.Contains("public static ShipmentId Parse(string s, global::System.IFormatProvider? provider)", text);
        Assert.Contains("public static bool TryParse(string? s, global::System.IFormatProvider? provider, out ShipmentId result)", text);
        Assert.Contains("public static ShipmentId Parse(global::System.ReadOnlySpan<char> s, global::System.IFormatProvider? provider)", text);
        Assert.Contains("public static bool TryParse(global::System.ReadOnlySpan<char> s, global::System.IFormatProvider? provider, out ShipmentId result)", text);
        Assert.Contains("global::System.Guid.Parse", text);
        Assert.Contains("global::System.Guid.TryParse", text);
    }

    [Fact]
    public void Generator_ShouldEmitParsableMembers_ForIntBackedId()
    {
        const string source = """
            using Moongazing.OrionGuard.Domain.Primitives;

            namespace App
            {
                [StronglyTypedId<int>]
                public readonly partial struct BucketId { }
            }
            """;

        var result = RunGenerator(source);

        var parsable = result.Results.SelectMany(r => r.GeneratedSources)
            .FirstOrDefault(s => s.HintName.Contains("BucketId.Parsable"));

        Assert.NotEqual(default, parsable);
        var text = parsable.SourceText.ToString();
        Assert.Contains("int.Parse(s, global::System.Globalization.NumberStyles.Integer, provider)", text);
        Assert.Contains("int.TryParse(s, global::System.Globalization.NumberStyles.Integer, provider, out var v)", text);
    }

    [Fact]
    public void Generator_ShouldEmitParsableMembers_ForStringBackedId()
    {
        const string source = """
            using Moongazing.OrionGuard.Domain.Primitives;

            namespace App
            {
                [StronglyTypedId<string>]
                public readonly partial struct AccountCode { }
            }
            """;

        var result = RunGenerator(source);

        var parsable = result.Results.SelectMany(r => r.GeneratedSources)
            .FirstOrDefault(s => s.HintName.Contains("AccountCode.Parsable"));

        Assert.NotEqual(default, parsable);
        var text = parsable.SourceText.ToString();
        // String Parse never fails on non-null input — the null check pattern suffices.
        Assert.Contains("public static AccountCode Parse(string s, global::System.IFormatProvider? provider)", text);
        Assert.Contains("public static bool TryParse(string? s, global::System.IFormatProvider? provider, out AccountCode result)", text);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Moongazing.OrionGuard.Generators.Tests/Moongazing.OrionGuard.Generators.Tests.csproj --filter FullyQualifiedName~StronglyTypedIdGeneratorParsableTests`
Expected: FAIL — `ParsableEmitter` does not exist; no `*.Parsable.g.cs` file is produced.

- [ ] **Step 3: Create `ParsableEmitter.cs`**

Create `src/Moongazing.OrionGuard.Generators/StronglyTypedIds/ParsableEmitter.cs`:

```csharp
#nullable enable

using System.Text;

namespace Moongazing.OrionGuard.Generators.StronglyTypedIds
{
    /// <summary>
    /// Emits the <c>IParsable&lt;TSelf&gt;</c> and <c>ISpanParsable&lt;TSelf&gt;</c> implementations
    /// into a secondary partial struct file, so the generated strongly-typed id plays nicely with
    /// ASP.NET Core minimal API parameter binding and other framework parseable consumers.
    /// </summary>
    internal static class ParsableEmitter
    {
        public static string HintName(string typeName) => typeName + ".Parsable.g.cs";

        public static string Emit(string @namespace, string typeName, SupportedValueType valueType)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();
            sb.Append("namespace ").AppendLine(@namespace);
            sb.AppendLine("{");
            sb.Append("    public readonly partial struct ").Append(typeName)
              .Append(" : global::System.IParsable<").Append(typeName).Append(">")
              .Append(", global::System.ISpanParsable<").Append(typeName).AppendLine(">")
              .AppendLine("    {");

            EmitParseString(sb, typeName, valueType);
            sb.AppendLine();
            EmitTryParseString(sb, typeName, valueType);
            sb.AppendLine();
            EmitParseSpan(sb, typeName, valueType);
            sb.AppendLine();
            EmitTryParseSpan(sb, typeName, valueType);

            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static void EmitParseString(StringBuilder sb, string typeName, SupportedValueType valueType)
        {
            sb.Append("        public static ").Append(typeName).AppendLine(" Parse(string s, global::System.IFormatProvider? provider)");
            sb.AppendLine("        {");
            switch (valueType)
            {
                case SupportedValueType.Guid:
                    sb.Append("            return new ").Append(typeName).AppendLine("(global::System.Guid.Parse(s, provider));");
                    break;
                case SupportedValueType.Int32:
                    sb.Append("            return new ").Append(typeName).AppendLine("(int.Parse(s, global::System.Globalization.NumberStyles.Integer, provider));");
                    break;
                case SupportedValueType.Int64:
                    sb.Append("            return new ").Append(typeName).AppendLine("(long.Parse(s, global::System.Globalization.NumberStyles.Integer, provider));");
                    break;
                case SupportedValueType.String:
                    sb.AppendLine("            if (s is null) throw new global::System.ArgumentNullException(nameof(s));");
                    sb.Append("            return new ").Append(typeName).AppendLine("(s);");
                    break;
                case SupportedValueType.Ulid:
                    sb.AppendLine("#if NET9_0_OR_GREATER");
                    sb.Append("            return new ").Append(typeName).AppendLine("(global::System.Ulid.Parse(s, provider));");
                    sb.AppendLine("#else");
                    sb.AppendLine("            throw new global::System.PlatformNotSupportedException(\"Ulid requires .NET 9 or later.\");");
                    sb.AppendLine("#endif");
                    break;
            }
            sb.AppendLine("        }");
        }

        private static void EmitTryParseString(StringBuilder sb, string typeName, SupportedValueType valueType)
        {
            sb.Append("        public static bool TryParse(string? s, global::System.IFormatProvider? provider, out ").Append(typeName).AppendLine(" result)");
            sb.AppendLine("        {");
            switch (valueType)
            {
                case SupportedValueType.Guid:
                    sb.AppendLine("            if (global::System.Guid.TryParse(s, provider, out var v)) { result = new " + typeName + "(v); return true; }");
                    sb.AppendLine("            result = default; return false;");
                    break;
                case SupportedValueType.Int32:
                    sb.AppendLine("            if (int.TryParse(s, global::System.Globalization.NumberStyles.Integer, provider, out var v)) { result = new " + typeName + "(v); return true; }");
                    sb.AppendLine("            result = default; return false;");
                    break;
                case SupportedValueType.Int64:
                    sb.AppendLine("            if (long.TryParse(s, global::System.Globalization.NumberStyles.Integer, provider, out var v)) { result = new " + typeName + "(v); return true; }");
                    sb.AppendLine("            result = default; return false;");
                    break;
                case SupportedValueType.String:
                    sb.AppendLine("            if (s is null) { result = default; return false; }");
                    sb.Append("            result = new ").Append(typeName).AppendLine("(s); return true;");
                    break;
                case SupportedValueType.Ulid:
                    sb.AppendLine("#if NET9_0_OR_GREATER");
                    sb.AppendLine("            if (global::System.Ulid.TryParse(s, provider, out var v)) { result = new " + typeName + "(v); return true; }");
                    sb.AppendLine("            result = default; return false;");
                    sb.AppendLine("#else");
                    sb.AppendLine("            result = default; return false;");
                    sb.AppendLine("#endif");
                    break;
            }
            sb.AppendLine("        }");
        }

        private static void EmitParseSpan(StringBuilder sb, string typeName, SupportedValueType valueType)
        {
            sb.Append("        public static ").Append(typeName).AppendLine(" Parse(global::System.ReadOnlySpan<char> s, global::System.IFormatProvider? provider)");
            sb.AppendLine("        {");
            switch (valueType)
            {
                case SupportedValueType.Guid:
                    sb.Append("            return new ").Append(typeName).AppendLine("(global::System.Guid.Parse(s, provider));");
                    break;
                case SupportedValueType.Int32:
                    sb.Append("            return new ").Append(typeName).AppendLine("(int.Parse(s, global::System.Globalization.NumberStyles.Integer, provider));");
                    break;
                case SupportedValueType.Int64:
                    sb.Append("            return new ").Append(typeName).AppendLine("(long.Parse(s, global::System.Globalization.NumberStyles.Integer, provider));");
                    break;
                case SupportedValueType.String:
                    sb.Append("            return new ").Append(typeName).AppendLine("(s.ToString());");
                    break;
                case SupportedValueType.Ulid:
                    sb.AppendLine("#if NET9_0_OR_GREATER");
                    sb.Append("            return new ").Append(typeName).AppendLine("(global::System.Ulid.Parse(s, provider));");
                    sb.AppendLine("#else");
                    sb.AppendLine("            throw new global::System.PlatformNotSupportedException(\"Ulid requires .NET 9 or later.\");");
                    sb.AppendLine("#endif");
                    break;
            }
            sb.AppendLine("        }");
        }

        private static void EmitTryParseSpan(StringBuilder sb, string typeName, SupportedValueType valueType)
        {
            sb.Append("        public static bool TryParse(global::System.ReadOnlySpan<char> s, global::System.IFormatProvider? provider, out ").Append(typeName).AppendLine(" result)");
            sb.AppendLine("        {");
            switch (valueType)
            {
                case SupportedValueType.Guid:
                    sb.AppendLine("            if (global::System.Guid.TryParse(s, provider, out var v)) { result = new " + typeName + "(v); return true; }");
                    sb.AppendLine("            result = default; return false;");
                    break;
                case SupportedValueType.Int32:
                    sb.AppendLine("            if (int.TryParse(s, global::System.Globalization.NumberStyles.Integer, provider, out var v)) { result = new " + typeName + "(v); return true; }");
                    sb.AppendLine("            result = default; return false;");
                    break;
                case SupportedValueType.Int64:
                    sb.AppendLine("            if (long.TryParse(s, global::System.Globalization.NumberStyles.Integer, provider, out var v)) { result = new " + typeName + "(v); return true; }");
                    sb.AppendLine("            result = default; return false;");
                    break;
                case SupportedValueType.String:
                    sb.Append("            result = new ").Append(typeName).AppendLine("(s.ToString()); return true;");
                    break;
                case SupportedValueType.Ulid:
                    sb.AppendLine("#if NET9_0_OR_GREATER");
                    sb.AppendLine("            if (global::System.Ulid.TryParse(s, provider, out var v)) { result = new " + typeName + "(v); return true; }");
                    sb.AppendLine("            result = default; return false;");
                    sb.AppendLine("#else");
                    sb.AppendLine("            result = default; return false;");
                    sb.AppendLine("#endif");
                    break;
            }
            sb.AppendLine("        }");
        }
    }
}
```

- [ ] **Step 4: Verify the emitter file compiles (no wiring yet)**

Run: `dotnet build src/Moongazing.OrionGuard.Generators/Moongazing.OrionGuard.Generators.csproj -c Release`
Expected: 0 errors.

- [ ] **Step 5: Commit (emitter only — tests still fail until Task 8 wires it in)**

```bash
git add src/Moongazing.OrionGuard.Generators/StronglyTypedIds/ParsableEmitter.cs \
        tests/Moongazing.OrionGuard.Generators.Tests/StronglyTypedIdGeneratorParsableTests.cs
git commit -m "feat(generators): add ParsableEmitter for IParsable/ISpanParsable on strongly-typed ids"
```

---

## Task 8: Wire `ParsableEmitter` into the generator

**Files:**
- Modify: `src/Moongazing.OrionGuard.Generators/StronglyTypedIds/StronglyTypedIdGenerator.cs`

- [ ] **Step 1: Add the emit call inside `RegisterSourceOutput` callback**

Open `src/Moongazing.OrionGuard.Generators/StronglyTypedIds/StronglyTypedIdGenerator.cs`. Locate the `RegisterSourceOutput` callback updated in Task 6. Add a new `spc.AddSource` call **after** the `TypeConverterEmitter` call:

```csharp
spc.AddSource(
    ParsableEmitter.HintName(target.TypeName),
    SourceText.From(
        ParsableEmitter.Emit(target.Namespace, target.TypeName, target.ValueType),
        Encoding.UTF8));
```

Nothing else in the file changes.

- [ ] **Step 2: Run the Parsable tests**

Run: `dotnet test tests/Moongazing.OrionGuard.Generators.Tests/Moongazing.OrionGuard.Generators.Tests.csproj --filter FullyQualifiedName~StronglyTypedIdGeneratorParsableTests`
Expected: PASS — 3 tests.

- [ ] **Step 3: Run full generator test suite as regression guard**

Run: `dotnet test tests/Moongazing.OrionGuard.Generators.Tests/Moongazing.OrionGuard.Generators.Tests.csproj`
Expected: PASS — 11 tests total (5 original + 1 interface + 2 conditional EF Core + 3 Parsable).

- [ ] **Step 4: Commit**

```bash
git add src/Moongazing.OrionGuard.Generators/StronglyTypedIds/StronglyTypedIdGenerator.cs
git commit -m "feat(generators): emit IParsable/ISpanParsable partial alongside StronglyTypedId"
```

---

## Task 9: Demo cleanup — drop EF Core dependency, refresh section 17 message

**Files:**
- Modify: `demo/Moongazing.OrionGuard.Demo/Moongazing.OrionGuard.Demo.csproj`
- Modify: `demo/Moongazing.OrionGuard.Demo/Program.cs`

- [ ] **Step 1: Remove EF Core package reference**

Open `demo/Moongazing.OrionGuard.Demo/Moongazing.OrionGuard.Demo.csproj`. Delete these three lines:

```xml
    <!-- Required so the StronglyTypedId-generated EF Core ValueConverter companions compile.
         A real consumer using [StronglyTypedId] with EF Core would already have this. -->
    <PackageReference Include="Microsoft.EntityFrameworkCore" Version="9.0.0" />
```

The remaining package references (`Microsoft.VisualStudio.Azure.Containers.Tools.Targets`, `Microsoft.Extensions.DependencyInjection`) stay.

- [ ] **Step 2: Update Program.cs section 17 output**

In `demo/Moongazing.OrionGuard.Demo/Program.cs`, find section 17 (the `AddOrionGuardStronglyTypedIds` demo). Replace the two `ℹ` informational lines:

```csharp
Console.WriteLine("   ℹ These are the ProductIdEfCoreValueConverter / SkuIdEfCoreValueConverter / CountryCodeEfCoreValueConverter types emitted by the generator.");
Console.WriteLine("   ℹ EF Core's DbContext can now resolve and register these converters via DI.");
```

with:

```csharp
Console.WriteLine("   ℹ The generator skips emitting EF Core ValueConverter companions when the consumer project does not reference Microsoft.EntityFrameworkCore (NEW in v6.2).");
Console.WriteLine("   ℹ Add `<PackageReference Include=\"Microsoft.EntityFrameworkCore\" />` to resume emitting them. JSON + TypeConverter companions emit unconditionally.");
```

- [ ] **Step 3: Build and run the demo**

Run:
```bash
dotnet build demo/Moongazing.OrionGuard.Demo/Moongazing.OrionGuard.Demo.csproj -c Release
dotnet run --project demo/Moongazing.OrionGuard.Demo/Moongazing.OrionGuard.Demo.csproj -c Release --no-build
```
Expected: Build succeeds (0 errors). Demo runs end-to-end. Section 17 prints `Registered 0 generated EF Core ValueConverter(s)` because EF Core is now absent.

- [ ] **Step 4: Commit**

```bash
git add demo/Moongazing.OrionGuard.Demo/Moongazing.OrionGuard.Demo.csproj \
        demo/Moongazing.OrionGuard.Demo/Program.cs
git commit -m "docs(demo): drop EF Core dependency now that generator skips its converter when unavailable"
```

---

## Task 10: Full solution build + test verification

**Files:** none modified — verification only.

- [ ] **Step 1: Release build the whole solution**

Run: `dotnet build Moongazing.OrionGuard.sln -c Release --no-incremental`
Expected: Build succeeded, 0 errors. Warnings should be only `NU1900` (network-related package vulnerability fetch failures).

- [ ] **Step 2: Run the full test suite**

Run: `dotnet test Moongazing.OrionGuard.sln -c Release --no-build --verbosity quiet`
Expected: PASS — approx 512 tests total (506 from v6.1.0 + ~6 new tests across Tasks 1, 3, 4, 5, 6, 7). Actual new-test tally: 1 (Task 1) + 1 (Task 3) + 1 (Task 4) + 4 (Task 5) + 2 (Task 6) + 3 (Task 7) = 12 new tests. Expected total ≈ 518.

- [ ] **Step 3: If anything fails, fix inline and re-run**

Common failure modes to check:
- A missing XML doc on a new public member (`CS1591` — suppressed on Generators but enforced on core).
- A typo in the `HintName` suffix breaking a generator test assertion.
- A `using` missing (`using System;` in new files).

If all is green, no commit is needed — this is a verification task.

---

## Task 11: Version bump + CHANGELOG + README + ROADMAP + FEATURES

**Files:**
- Modify: 9 `*.csproj` files (all packages) — Version bump
- Modify: `src/Moongazing.OrionGuard/Moongazing.OrionGuard.csproj` — PackageReleaseNotes v6.2.0 block
- Modify: `CHANGELOG.md` — v6.2.0 entry
- Modify: `README.md` — short v6.2 note
- Modify: `docs/FEATURES-v6.1.md` — append v6.2 section
- Modify: `docs/ROADMAP.md` — shift slots

- [ ] **Step 1: Bump all 9 package versions 6.1.0 → 6.2.0**

For each of these files, change `<Version>6.1.0</Version>` to `<Version>6.2.0</Version>`:

1. `src/Moongazing.OrionGuard/Moongazing.OrionGuard.csproj`
2. `src/Moongazing.OrionGuard.Generators/Moongazing.OrionGuard.Generators.csproj`
3. `src/Moongazing.OrionGuard.AspNetCore/Moongazing.OrionGuard.AspNetCore.csproj`
4. `src/Moongazing.OrionGuard.Blazor/Moongazing.OrionGuard.Blazor.csproj`
5. `src/Moongazing.OrionGuard.Grpc/Moongazing.OrionGuard.Grpc.csproj`
6. `src/Moongazing.OrionGuard.MediatR/Moongazing.OrionGuard.MediatR.csproj`
7. `src/Moongazing.OrionGuard.OpenTelemetry/Moongazing.OrionGuard.OpenTelemetry.csproj`
8. `src/Moongazing.OrionGuard.SignalR/Moongazing.OrionGuard.SignalR.csproj`
9. `src/Moongazing.OrionGuard.Swagger/Moongazing.OrionGuard.Swagger.csproj`

- [ ] **Step 2: Update core csproj `PackageReleaseNotes` with a v6.2.0 block**

In `src/Moongazing.OrionGuard/Moongazing.OrionGuard.csproj`, locate the `<PackageReleaseNotes>` opening tag and the current `v6.1.0 — Release Notes` heading. Prepend a v6.2.0 block above it:

```
v6.2.0 — Release Notes

NEW: IStronglyTypedId&lt;TValue&gt; marker interface — implemented by both the StronglyTypedId&lt;TValue&gt; abstract record (manual style) AND source-generated readonly partial struct ids. The AgainstDefaultStronglyTypedId guard now accepts this interface as its receiver, so both id styles work with the same guard.

NEW: DomainEventBase abstract record — consumers can write `public sealed record OrderPlaced(OrderId Id) : DomainEventBase;` instead of hand-rolling EventId and OccurredOnUtc. Both properties use init accessors so tests can pin them via `with` expressions.

NEW: Generated strongly-typed ids implement IParsable&lt;TSelf&gt; and ISpanParsable&lt;TSelf&gt; — ASP.NET Core minimal APIs bind them from route/query/form parameters without a custom TypeConverter hop. Standard .NET FormatException semantics on Parse failure.

IMPROVED: The StronglyTypedId source generator now detects whether the consumer project references EF Core and skips emitting the ValueConverter companion when it does not — console apps, Blazor WASM, and class libraries no longer need a spurious EF Core PackageReference just to build.

MIGRATION: Source-compatible with v6.1.0 — no user code change required. Receiver and return type of AgainstDefaultStronglyTypedId widened to IStronglyTypedId&lt;TValue&gt; (manual records still implement this interface). Recompile recommended.

ROADMAP: v6.3.0 = domain event dispatcher + MediatR bridge + EF Core SaveChanges interceptor. v6.4.0 = full BusinessRule base class + Guard.Against.BrokenRule + AspNetCore ProblemDetails mapping.

Full changelog: https://github.com/tunahanaliozturk/OrionGuard/blob/master/CHANGELOG.md

```

Keep the existing `v6.1.0 — Release Notes` block immediately below, and the `v6.0.0 — Release Notes` and earlier blocks untouched.

- [ ] **Step 3: Update `CHANGELOG.md`**

Insert a new section at the top of `CHANGELOG.md` above the `## [6.1.0]` line:

````markdown
## [6.2.0] - 2026-04-19

### Added

- `Moongazing.OrionGuard.Domain.Primitives.IStronglyTypedId<TValue>` marker interface implemented by both the `StronglyTypedId<TValue>` abstract record and source-generated strongly-typed id structs.
- `Moongazing.OrionGuard.Domain.Events.DomainEventBase` abstract record — auto-assigns `EventId` (new `Guid`) and `OccurredOnUtc` (UTC timestamp) at construction, with `init` accessors for test overrides via `with` expressions.
- Source-generated strongly-typed ids now implement `IParsable<TSelf>` and `ISpanParsable<TSelf>` — ASP.NET Core minimal API route/query/form binding works out of the box.

### Changed

- `AgainstDefaultStronglyTypedId` guard receiver widened from `StronglyTypedId<TValue>` to `IStronglyTypedId<TValue>`. Source-compatible with v6.1.0 callers.
- The `[StronglyTypedId<TValue>]` source generator no longer emits its EF Core `ValueConverter` companion when the consumer project does not reference `Microsoft.EntityFrameworkCore`. JSON and TypeConverter companions emit unconditionally.

### Roadmap

- v6.3.0 (next): Domain event dispatcher, MediatR bridge, EF Core `SaveChanges` interceptor.
- v6.4.0: Full `BusinessRule` base class, `Guard.Against.BrokenRule`, ASP.NET Core ProblemDetails mapping.

````

- [ ] **Step 4: Update `README.md`**

In `README.md`, find the `### DDD Primitives (NEW in v6.1)` section. Immediately below the final `> v6.2.0 will add IDomainEventDispatcher...` blockquote in that section, replace that blockquote with:

```markdown
> **v6.2 update:** `IStronglyTypedId<TValue>` marker interface unifies source-gen struct ids and manual record ids under one guard. `DomainEventBase` record spares you the `EventId`/`OccurredOnUtc` boilerplate. Generated ids implement `IParsable<TSelf>` / `ISpanParsable<TSelf>` for ASP.NET Core minimal API binding. EF Core converter emission is now conditional on the consumer referencing EF Core.
>
> v6.3.0 (next) adds `IDomainEventDispatcher` + MediatR bridge + EF Core `SaveChanges` interceptor. v6.4.0 adds the `BusinessRule` base class, `Guard.Against.BrokenRule`, and ASP.NET Core ProblemDetails integration.
```

- [ ] **Step 5: Append v6.2 section to `docs/FEATURES-v6.1.md`**

Append at the end of `docs/FEATURES-v6.1.md`:

````markdown

---

## v6.2 Additions (April 2026)

### `IStronglyTypedId<TValue>` marker interface

```csharp
public interface IStronglyTypedId<TValue>
    where TValue : notnull, IEquatable<TValue>
{
    TValue Value { get; }
}
```

Both declaration styles now implement it — manual records via `StronglyTypedId<TValue> : IStronglyTypedId<TValue>`, and source-generated structs via the generator adding the interface to the emitted partial declaration. The `AgainstDefaultStronglyTypedId` guard receiver widens accordingly, so a single guard call works regardless of which style the id was declared in.

### `DomainEventBase` record

```csharp
public abstract record DomainEventBase : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredOnUtc { get; init; } = DateTime.UtcNow;
}

public sealed record OrderPlaced(OrderId Id) : DomainEventBase;   // EventId + OccurredOnUtc populated automatically
var fixedEvent = new OrderPlaced(id) with { EventId = testId };   // init accessors enable test overrides
```

The dispatcher (v6.3.0) operates on the `IDomainEvent` abstraction — this record is a pure ergonomic shortcut.

### `IParsable<TSelf>` + `ISpanParsable<TSelf>` on generated ids

```csharp
[StronglyTypedId<Guid>]
public readonly partial struct OrderId;

// Minimal API route binding now works without a custom TypeConverter hop:
app.MapGet("/orders/{id}", (OrderId id) => $"Order {id}");
```

Failure semantics follow standard .NET — `Parse` throws `FormatException`, `TryParse` returns `false`. ASP.NET Core translates the exception to 400 Bad Request automatically.

### Conditional EF Core converter emission

The `[StronglyTypedId<TValue>]` generator inspects the consumer's `Compilation` for the `Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<,>` type and skips emitting the EF Core converter companion when it is absent. Console apps, Blazor WASM apps, and class libraries no longer need a spurious EF Core `PackageReference` purely to compile generated code. Adding EF Core to the project resumes emission automatically on the next incremental build.

````

- [ ] **Step 6: Update `docs/ROADMAP.md`**

In `docs/ROADMAP.md`, shift the roadmap slots by one minor version. Search for any line that previously described `v6.2.0` as dispatcher-related and change it to reflect the new v6.2.0 (API polish) / v6.3.0 (dispatcher) / v6.4.0 (business rules) assignment. If there is no explicit per-version table, add a short "Version map" block near the top:

```markdown
### Current version map

- **v6.1.0** — DDD tactical primitives (ValueObject, Entity, AggregateRoot, StronglyTypedId base + generator), guard extension, DI helper, 14-language localization keys.
- **v6.2.0** — API polish: `IStronglyTypedId<TValue>` unification, `DomainEventBase`, `IParsable`/`ISpanParsable` on generated ids, conditional EF Core converter emission.
- **v6.3.0** (next) — Domain event dispatcher, MediatR bridge, EF Core `SaveChanges` interceptor.
- **v6.4.0** — Full `BusinessRule` base class, `Guard.Against.BrokenRule`, ASP.NET Core ProblemDetails mapping.
```

Place this block immediately below the existing roadmap intro paragraph (before the first `##` subsection).

- [ ] **Step 7: Build + test once more**

Run:
```bash
dotnet build Moongazing.OrionGuard.sln -c Release
dotnet test Moongazing.OrionGuard.sln -c Release --no-build --verbosity quiet
```
Expected: both succeed.

- [ ] **Step 8: Commit**

```bash
git add \
  src/Moongazing.OrionGuard/Moongazing.OrionGuard.csproj \
  src/Moongazing.OrionGuard.Generators/Moongazing.OrionGuard.Generators.csproj \
  src/Moongazing.OrionGuard.AspNetCore/Moongazing.OrionGuard.AspNetCore.csproj \
  src/Moongazing.OrionGuard.Blazor/Moongazing.OrionGuard.Blazor.csproj \
  src/Moongazing.OrionGuard.Grpc/Moongazing.OrionGuard.Grpc.csproj \
  src/Moongazing.OrionGuard.MediatR/Moongazing.OrionGuard.MediatR.csproj \
  src/Moongazing.OrionGuard.OpenTelemetry/Moongazing.OrionGuard.OpenTelemetry.csproj \
  src/Moongazing.OrionGuard.SignalR/Moongazing.OrionGuard.SignalR.csproj \
  src/Moongazing.OrionGuard.Swagger/Moongazing.OrionGuard.Swagger.csproj \
  CHANGELOG.md README.md docs/FEATURES-v6.1.md docs/ROADMAP.md
git commit -m "release: bump ecosystem to v6.2.0 with API polish CHANGELOG, README, ROADMAP"
```

---

## Self-Review Checklist

Before starting execution, verify:

- [ ] **Spec coverage.** Every section of the spec (2 through 7) has a task: Feature A → Task 6; Feature B → Tasks 1/2/3/4; Feature C → Task 5; Feature D → Tasks 7/8. Demo cleanup → Task 9. Release wiring → Task 11.
- [ ] **Type consistency.** `IStronglyTypedId<TValue>` signature (`TValue Value { get; }`, `where TValue : notnull, IEquatable<TValue>`) matches Task 1, Task 2's declaration change, Task 3's guard signature, Task 4's generator interface-list emission.
- [ ] **Emitter hint names.** Task 7 uses `{TypeName}.Parsable.g.cs` (via `ParsableEmitter.HintName`) — Task 7 tests match this string (`"ShipmentId.Parsable"`).
- [ ] **`Ulid` guard consistency.** Task 7 wraps Ulid branches in `#if NET9_0_OR_GREATER` matching the existing convention in `StronglyTypedIdEmitter.cs`. The non-Ulid branches don't need any conditional because `IParsable<T>` is available on every TFM the core package targets (net8.0+).
- [ ] **Test counts.** New tests: Task 1 = 1, Task 3 = 1, Task 4 = 1, Task 5 = 4, Task 6 = 2, Task 7 = 3. Total = 12 new tests. No task adds implementation without a test (Task 2 re-uses Task 1's test; Task 8 re-uses Task 7's tests; Task 10 is verification only; Task 9 is a demo-level integration check; Task 11 is release prep).
- [ ] **Zero placeholders.** Grep the plan for "TBD" / "TODO" / "...to be filled": none present. Every code step contains the exact emitted/inserted text.
- [ ] **Null-check style.** Task 3's rewritten `StronglyTypedIdGuards.cs` retains the `if (id is null) throw new NullValueException(...)` pattern. CA1510 only targets manual throws of `ArgumentNullException`, not custom OrionGuard exceptions, so the existing pattern is compliant. Do NOT switch this line to `ArgumentNullException.ThrowIfNull(id)` — that would throw the wrong exception type and regress the v6.1 contract that consumers catch `NullValueException`.
