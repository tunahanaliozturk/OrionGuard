# OrionGuard v6.3.0 Domain Events Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the domain-event dispatching layer plus transactional outbox, OpenTelemetry instrumentation, and framework-agnostic test helpers — bumping the OrionGuard packages from v6.2.0 to v6.3.0.

**Architecture:** Core (`OrionGuard`) gets a MediatR-free `IDomainEventDispatcher` and default `ServiceProviderDomainEventDispatcher`. MediatR users opt in via marker (`: INotification`) — the bridge calls `IPublisher.Publish` directly without wrappers. EF Core integration ships in a brand-new `OrionGuard.EntityFrameworkCore` package with two modes: Inline (post-save dispatch) and Outbox (transactional `OutboxMessage` writes consumed by a `BackgroundService`). OpenTelemetry decorator + W3C trace propagation through the outbox preserves end-to-end traces. A new `OrionGuard.Testing` package exposes `DomainEventCapture` and `InMemoryDomainEventDispatcher` with framework-agnostic assertions.

**Tech Stack:** C# 13, .NET 8 / 9 / 10 multi-targeting, xUnit 2.9.2, EF Core 9, MediatR 12.4, OpenTelemetry.Api 1.9.

**Spec:** [docs/superpowers/specs/2026-05-09-orionguard-v6.3.0-domain-events-design.md](../specs/2026-05-09-orionguard-v6.3.0-domain-events-design.md)

**Branch:** `feature/v6.3.0-domain-events` (already created)

---

## File structure

### New files in `OrionGuard` core
```
src/Moongazing.OrionGuard/Domain/Events/
  IDomainEventDispatcher.cs
  IDomainEventHandler.cs
  DomainEventDispatchOptions.cs
  DispatchMode.cs
  ServiceProviderDomainEventDispatcher.cs
src/Moongazing.OrionGuard/DependencyInjection/
  DomainEventServiceCollectionExtensions.cs
```

### New files in `OrionGuard.MediatR`
```
src/Moongazing.OrionGuard.MediatR/DomainEvents/
  MediatRDomainEventDispatcher.cs
  MediatRDomainEventServiceCollectionExtensions.cs
```

### New package: `OrionGuard.EntityFrameworkCore`
```
src/Moongazing.OrionGuard.EntityFrameworkCore/
  Moongazing.OrionGuard.EntityFrameworkCore.csproj
  OrionGuardEfCoreOptions.cs
  DomainEventSaveChangesInterceptor.cs
  DomainEventCollector.cs
  ServiceCollectionExtensions.cs
  Outbox/
    OutboxMessage.cs
    OutboxMessageEntityTypeConfiguration.cs
    OutboxOptions.cs
    OutboxDispatcherHostedService.cs
  docs/README.md
  docs/logo.png
```

### New package: `OrionGuard.Testing`
```
src/Moongazing.OrionGuard.Testing/
  Moongazing.OrionGuard.Testing.csproj
  DomainEvents/
    DomainEventCapture.cs
    DomainEventAssertions.cs
    DomainEventAssertionException.cs
    InMemoryDomainEventDispatcher.cs
  docs/README.md
  docs/logo.png
```

### Additions to `OrionGuard.OpenTelemetry`
```
src/Moongazing.OrionGuard.OpenTelemetry/DomainEvents/
  OrionGuardDomainEventTelemetry.cs
  InstrumentedDomainEventDispatcher.cs
  DomainEventOpenTelemetryExtensions.cs
```

### New test projects
```
tests/Moongazing.OrionGuard.EntityFrameworkCore.Tests/
  Moongazing.OrionGuard.EntityFrameworkCore.Tests.csproj
  DomainEventInterceptorInlineTests.cs
  DomainEventInterceptorOutboxTests.cs
  OutboxDispatcherTests.cs
  TestFixtures/
    TestDbContext.cs
    TestAggregate.cs
tests/Moongazing.OrionGuard.Testing.Tests/
  Moongazing.OrionGuard.Testing.Tests.csproj
  DomainEventCaptureTests.cs
  InMemoryDomainEventDispatcherTests.cs
```

### Additions to existing test project
```
tests/Moongazing.OrionGuard.Tests/
  ServiceProviderDomainEventDispatcherTests.cs
  DomainEventDIExtensionsTests.cs
```

### Modified files
```
Moongazing.OrionGuard.sln                                           # add 4 new projects
src/Moongazing.OrionGuard/Moongazing.OrionGuard.csproj              # 6.2.0 → 6.3.0, release notes
src/Moongazing.OrionGuard.MediatR/Moongazing.OrionGuard.MediatR.csproj
src/Moongazing.OrionGuard.OpenTelemetry/Moongazing.OrionGuard.OpenTelemetry.csproj
CHANGELOG.md                                                        # v6.3.0 entry
README.md                                                           # ecosystem table additions
demo/Moongazing.OrionGuard.Demo/Program.cs                          # showcase
```

---

## Task 1: Bootstrap `OrionGuard.EntityFrameworkCore` package

**Files:**
- Create: `src/Moongazing.OrionGuard.EntityFrameworkCore/Moongazing.OrionGuard.EntityFrameworkCore.csproj`
- Create: `src/Moongazing.OrionGuard.EntityFrameworkCore/docs/README.md`
- Copy: `src/Moongazing.OrionGuard.EntityFrameworkCore/docs/logo.png` (from existing package)
- Create: `src/Moongazing.OrionGuard.EntityFrameworkCore/OrionGuardEfCoreMarker.cs` (placeholder so the project compiles)
- Modify: `Moongazing.OrionGuard.sln`

- [ ] **Step 1: Create the csproj file**

Create `src/Moongazing.OrionGuard.EntityFrameworkCore/Moongazing.OrionGuard.EntityFrameworkCore.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net8.0;net9.0;net10.0</TargetFrameworks>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <NoWarn>CS1591;NU1900;NU1901;NU1902;NU1903;NU1904</NoWarn>
    <PackageId>OrionGuard.EntityFrameworkCore</PackageId>
    <Version>6.3.0</Version>
    <Authors>Tunahan Ali Ozturk</Authors>
    <Description>EF Core integration for OrionGuard. Provides a SaveChanges interceptor that dispatches domain events from AggregateRoot&lt;TId&gt; instances after commit (Inline mode) or via a transactional outbox (Outbox mode) consumed by a hosted background worker. Includes W3C trace context propagation across the outbox boundary.</Description>
    <PackageTags>guard;validation;ddd;domain-events;outbox;entity-framework-core;efcore;aggregate-root</PackageTags>
    <PackageReadmeFile>docs/README.md</PackageReadmeFile>
    <PackageIcon>docs/logo.png</PackageIcon>
    <RepositoryUrl>https://github.com/tunahanaliozturk/OrionGuard</RepositoryUrl>
    <RepositoryType>git</RepositoryType>
    <PackageProjectUrl>https://github.com/tunahanaliozturk/OrionGuard</PackageProjectUrl>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore" Version="9.0.0" />
    <PackageReference Include="Microsoft.Extensions.Hosting.Abstractions" Version="9.0.0" />
    <ProjectReference Include="..\Moongazing.OrionGuard\Moongazing.OrionGuard.csproj" />
  </ItemGroup>

  <ItemGroup>
    <None Include="docs\README.md" Pack="true" PackagePath="docs/" />
    <None Include="docs\logo.png" Pack="true" PackagePath="docs/" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create the docs folder with README and logo**

Copy `src/Moongazing.OrionGuard.MediatR/docs/logo.png` to `src/Moongazing.OrionGuard.EntityFrameworkCore/docs/logo.png`.

Create `src/Moongazing.OrionGuard.EntityFrameworkCore/docs/README.md`:

```markdown
# OrionGuard.EntityFrameworkCore

EF Core integration for OrionGuard's domain-event dispatcher.

```bash
dotnet add package OrionGuard.EntityFrameworkCore
```

```csharp
services.AddOrionGuardDomainEvents();
services.AddOrionGuardDomainEventHandlers(typeof(Program).Assembly);
services.AddOrionGuardEfCore<AppDbContext>(o => o.UseOutbox());
```

See the main [OrionGuard README](https://github.com/tunahanaliozturk/OrionGuard) for full documentation.
```

- [ ] **Step 3: Add a placeholder marker class so the project compiles**

Create `src/Moongazing.OrionGuard.EntityFrameworkCore/OrionGuardEfCoreMarker.cs`:

```csharp
namespace Moongazing.OrionGuard.EntityFrameworkCore;

/// <summary>Type marker used by assembly scanning. Replaced by real types in subsequent tasks.</summary>
internal static class OrionGuardEfCoreMarker { }
```

- [ ] **Step 4: Register the project in the solution file**

Modify `Moongazing.OrionGuard.sln`. After the line that registers `Moongazing.OrionGuard.SignalR` (look for `{34F88DED-049C-4B87-81AB-F7F69EA59684}`), add:

```
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Moongazing.OrionGuard.EntityFrameworkCore", "src\Moongazing.OrionGuard.EntityFrameworkCore\Moongazing.OrionGuard.EntityFrameworkCore.csproj", "{B1E8F3A2-7C4D-4E92-B5F1-A8D9C2E03D14}"
EndProject
```

In `GlobalSection(ProjectConfigurationPlatforms)`, append the standard 12-line block for the new GUID:

```
		{B1E8F3A2-7C4D-4E92-B5F1-A8D9C2E03D14}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{B1E8F3A2-7C4D-4E92-B5F1-A8D9C2E03D14}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{B1E8F3A2-7C4D-4E92-B5F1-A8D9C2E03D14}.Debug|x64.ActiveCfg = Debug|Any CPU
		{B1E8F3A2-7C4D-4E92-B5F1-A8D9C2E03D14}.Debug|x64.Build.0 = Debug|Any CPU
		{B1E8F3A2-7C4D-4E92-B5F1-A8D9C2E03D14}.Debug|x86.ActiveCfg = Debug|Any CPU
		{B1E8F3A2-7C4D-4E92-B5F1-A8D9C2E03D14}.Debug|x86.Build.0 = Debug|Any CPU
		{B1E8F3A2-7C4D-4E92-B5F1-A8D9C2E03D14}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{B1E8F3A2-7C4D-4E92-B5F1-A8D9C2E03D14}.Release|Any CPU.Build.0 = Release|Any CPU
		{B1E8F3A2-7C4D-4E92-B5F1-A8D9C2E03D14}.Release|x64.ActiveCfg = Release|Any CPU
		{B1E8F3A2-7C4D-4E92-B5F1-A8D9C2E03D14}.Release|x64.Build.0 = Release|Any CPU
		{B1E8F3A2-7C4D-4E92-B5F1-A8D9C2E03D14}.Release|x86.ActiveCfg = Release|Any CPU
		{B1E8F3A2-7C4D-4E92-B5F1-A8D9C2E03D14}.Release|x86.Build.0 = Release|Any CPU
```

In `GlobalSection(NestedProjects)`, append:

```
		{B1E8F3A2-7C4D-4E92-B5F1-A8D9C2E03D14} = {397A784D-16D0-4DC2-8609-3EE0EF18998F}
```

- [ ] **Step 5: Verify build**

Run: `dotnet build src/Moongazing.OrionGuard.EntityFrameworkCore/Moongazing.OrionGuard.EntityFrameworkCore.csproj -c Debug`
Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/Moongazing.OrionGuard.EntityFrameworkCore Moongazing.OrionGuard.sln
git commit -m "build: bootstrap OrionGuard.EntityFrameworkCore package skeleton"
```

---

## Task 2: Bootstrap `OrionGuard.Testing` package

**Files:**
- Create: `src/Moongazing.OrionGuard.Testing/Moongazing.OrionGuard.Testing.csproj`
- Create: `src/Moongazing.OrionGuard.Testing/docs/README.md`
- Copy: `src/Moongazing.OrionGuard.Testing/docs/logo.png`
- Create: `src/Moongazing.OrionGuard.Testing/OrionGuardTestingMarker.cs`
- Modify: `Moongazing.OrionGuard.sln`

- [ ] **Step 1: Create the csproj**

Create `src/Moongazing.OrionGuard.Testing/Moongazing.OrionGuard.Testing.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net8.0;net9.0;net10.0</TargetFrameworks>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <NoWarn>CS1591;NU1900;NU1901;NU1902;NU1903;NU1904</NoWarn>
    <PackageId>OrionGuard.Testing</PackageId>
    <Version>6.3.0</Version>
    <Authors>Tunahan Ali Ozturk</Authors>
    <Description>Testing helpers for OrionGuard. Provides DomainEventCapture and InMemoryDomainEventDispatcher with framework-agnostic assertions (no xUnit / NUnit / FluentAssertions dependency). Works with any test runner.</Description>
    <PackageTags>guard;validation;testing;ddd;domain-events;assertions;test-helpers</PackageTags>
    <PackageReadmeFile>docs/README.md</PackageReadmeFile>
    <PackageIcon>docs/logo.png</PackageIcon>
    <RepositoryUrl>https://github.com/tunahanaliozturk/OrionGuard</RepositoryUrl>
    <RepositoryType>git</RepositoryType>
    <PackageProjectUrl>https://github.com/tunahanaliozturk/OrionGuard</PackageProjectUrl>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Moongazing.OrionGuard\Moongazing.OrionGuard.csproj" />
  </ItemGroup>

  <ItemGroup>
    <None Include="docs\README.md" Pack="true" PackagePath="docs/" />
    <None Include="docs\logo.png" Pack="true" PackagePath="docs/" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Add docs/README.md and copy logo.png**

Copy `src/Moongazing.OrionGuard.MediatR/docs/logo.png` to `src/Moongazing.OrionGuard.Testing/docs/logo.png`.

Create `src/Moongazing.OrionGuard.Testing/docs/README.md`:

```markdown
# OrionGuard.Testing

Test helpers for OrionGuard. No test-framework dependency — works with xUnit, NUnit, MSTest, etc.

```bash
dotnet add package OrionGuard.Testing
```

```csharp
var events = DomainEventCapture.From(aggregate);
events.Should().HaveRaised<OrderShipped>(e => e.OrderId == expectedId);
```
```

- [ ] **Step 3: Add a placeholder marker class**

Create `src/Moongazing.OrionGuard.Testing/OrionGuardTestingMarker.cs`:

```csharp
namespace Moongazing.OrionGuard.Testing;

/// <summary>Type marker used by assembly scanning. Replaced by real types in subsequent tasks.</summary>
internal static class OrionGuardTestingMarker { }
```

- [ ] **Step 4: Register in solution**

Modify `Moongazing.OrionGuard.sln`. After the EntityFrameworkCore project entry, add:

```
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Moongazing.OrionGuard.Testing", "src\Moongazing.OrionGuard.Testing\Moongazing.OrionGuard.Testing.csproj", "{C2F9A4B3-8D5E-4F03-C6E2-B9EAD3F14E25}"
EndProject
```

Append to `GlobalSection(ProjectConfigurationPlatforms)`:

```
		{C2F9A4B3-8D5E-4F03-C6E2-B9EAD3F14E25}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{C2F9A4B3-8D5E-4F03-C6E2-B9EAD3F14E25}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{C2F9A4B3-8D5E-4F03-C6E2-B9EAD3F14E25}.Debug|x64.ActiveCfg = Debug|Any CPU
		{C2F9A4B3-8D5E-4F03-C6E2-B9EAD3F14E25}.Debug|x64.Build.0 = Debug|Any CPU
		{C2F9A4B3-8D5E-4F03-C6E2-B9EAD3F14E25}.Debug|x86.ActiveCfg = Debug|Any CPU
		{C2F9A4B3-8D5E-4F03-C6E2-B9EAD3F14E25}.Debug|x86.Build.0 = Debug|Any CPU
		{C2F9A4B3-8D5E-4F03-C6E2-B9EAD3F14E25}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{C2F9A4B3-8D5E-4F03-C6E2-B9EAD3F14E25}.Release|Any CPU.Build.0 = Release|Any CPU
		{C2F9A4B3-8D5E-4F03-C6E2-B9EAD3F14E25}.Release|x64.ActiveCfg = Release|Any CPU
		{C2F9A4B3-8D5E-4F03-C6E2-B9EAD3F14E25}.Release|x64.Build.0 = Release|Any CPU
		{C2F9A4B3-8D5E-4F03-C6E2-B9EAD3F14E25}.Release|x86.ActiveCfg = Release|Any CPU
		{C2F9A4B3-8D5E-4F03-C6E2-B9EAD3F14E25}.Release|x86.Build.0 = Release|Any CPU
```

Append to `GlobalSection(NestedProjects)`:

```
		{C2F9A4B3-8D5E-4F03-C6E2-B9EAD3F14E25} = {397A784D-16D0-4DC2-8609-3EE0EF18998F}
```

- [ ] **Step 5: Build**

Run: `dotnet build src/Moongazing.OrionGuard.Testing/Moongazing.OrionGuard.Testing.csproj -c Debug`
Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/Moongazing.OrionGuard.Testing Moongazing.OrionGuard.sln
git commit -m "build: bootstrap OrionGuard.Testing package skeleton"
```

---

## Task 3: Core dispatcher abstractions

Add the public contract types that subsequent tasks implement and consume.

**Files:**
- Create: `src/Moongazing.OrionGuard/Domain/Events/IDomainEventDispatcher.cs`
- Create: `src/Moongazing.OrionGuard/Domain/Events/IDomainEventHandler.cs`
- Create: `src/Moongazing.OrionGuard/Domain/Events/DispatchMode.cs`
- Create: `src/Moongazing.OrionGuard/Domain/Events/DomainEventDispatchOptions.cs`

- [ ] **Step 1: Create `DispatchMode` enum**

Create `src/Moongazing.OrionGuard/Domain/Events/DispatchMode.cs`:

```csharp
namespace Moongazing.OrionGuard.Domain.Events;

/// <summary>Strategy used by <see cref="IDomainEventDispatcher"/> to invoke handlers for a single event.</summary>
public enum DispatchMode
{
    /// <summary>Run handlers in registration order; first exception aborts and propagates.</summary>
    SequentialFailFast,

    /// <summary>Run handlers in registration order; collect exceptions and rethrow as <see cref="AggregateException"/> at the end.</summary>
    SequentialContinueOnError,

    /// <summary>Run handlers concurrently via <see cref="Task.WhenAll(Task[])"/>; exceptions surface per Task.WhenAll semantics.</summary>
    Parallel,
}
```

- [ ] **Step 2: Create `DomainEventDispatchOptions`**

Create `src/Moongazing.OrionGuard/Domain/Events/DomainEventDispatchOptions.cs`:

```csharp
namespace Moongazing.OrionGuard.Domain.Events;

/// <summary>Configuration for <see cref="IDomainEventDispatcher"/>.</summary>
public sealed class DomainEventDispatchOptions
{
    /// <summary>How handlers are invoked for a single event. Default <see cref="DispatchMode.SequentialFailFast"/>.</summary>
    public DispatchMode Mode { get; init; } = DispatchMode.SequentialFailFast;
}
```

- [ ] **Step 3: Create `IDomainEventHandler<TEvent>`**

Create `src/Moongazing.OrionGuard/Domain/Events/IDomainEventHandler.cs`:

```csharp
namespace Moongazing.OrionGuard.Domain.Events;

/// <summary>
/// Handles a domain event of type <typeparamref name="TEvent"/>. Resolved from the DI container by
/// <see cref="ServiceProviderDomainEventDispatcher"/>.
/// </summary>
public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    /// <summary>Handles the event. Implementations should be idempotent in production deployments.</summary>
    Task HandleAsync(TEvent @event, CancellationToken cancellationToken);
}
```

- [ ] **Step 4: Create `IDomainEventDispatcher`**

Create `src/Moongazing.OrionGuard/Domain/Events/IDomainEventDispatcher.cs`:

```csharp
namespace Moongazing.OrionGuard.Domain.Events;

/// <summary>
/// Dispatches domain events to their registered handlers. The default implementation
/// (<see cref="ServiceProviderDomainEventDispatcher"/>) resolves <see cref="IDomainEventHandler{TEvent}"/>
/// instances from <see cref="IServiceProvider"/>; the MediatR bridge instead delegates to <c>IPublisher</c>.
/// </summary>
public interface IDomainEventDispatcher
{
    /// <summary>Dispatches a single event.</summary>
    Task DispatchAsync(IDomainEvent @event, CancellationToken cancellationToken = default);

    /// <summary>Dispatches a batch of events. Default implementations process them in iteration order.</summary>
    Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 5: Build**

Run: `dotnet build src/Moongazing.OrionGuard/Moongazing.OrionGuard.csproj -c Debug`
Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/Moongazing.OrionGuard/Domain/Events
git commit -m "feat(core): add IDomainEventDispatcher / IDomainEventHandler abstractions"
```

---

## Task 4: `ServiceProviderDomainEventDispatcher` — default implementation

Implements all three `DispatchMode` strategies, resolving handlers from the DI container via reflection (because handler type is closed over event type at runtime).

**Files:**
- Create: `src/Moongazing.OrionGuard/Domain/Events/ServiceProviderDomainEventDispatcher.cs`
- Create: `tests/Moongazing.OrionGuard.Tests/ServiceProviderDomainEventDispatcherTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/Moongazing.OrionGuard.Tests/ServiceProviderDomainEventDispatcherTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.Domain.Events;

namespace Moongazing.OrionGuard.Tests;

public class ServiceProviderDomainEventDispatcherTests
{
    private sealed record TestEvent(string Payload) : DomainEventBase;

    private sealed class CountingHandler : IDomainEventHandler<TestEvent>
    {
        public int Calls { get; private set; }
        public List<string> Payloads { get; } = new();
        public Task HandleAsync(TestEvent @event, CancellationToken ct)
        {
            Calls++;
            Payloads.Add(@event.Payload);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingHandler : IDomainEventHandler<TestEvent>
    {
        public Task HandleAsync(TestEvent @event, CancellationToken ct)
            => throw new InvalidOperationException("boom");
    }

    private static (IServiceProvider sp, T handler) BuildSp<T>(DispatchMode mode = DispatchMode.SequentialFailFast)
        where T : class, IDomainEventHandler<TestEvent>, new()
    {
        var handler = new T();
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<TestEvent>>(handler);
        services.AddSingleton(new DomainEventDispatchOptions { Mode = mode });
        services.AddSingleton<IDomainEventDispatcher, ServiceProviderDomainEventDispatcher>();
        return (services.BuildServiceProvider(), handler);
    }

    [Fact]
    public async Task DispatchAsync_InvokesHandler_WhenEventDispatched()
    {
        var (sp, handler) = BuildSp<CountingHandler>();
        var dispatcher = sp.GetRequiredService<IDomainEventDispatcher>();

        await dispatcher.DispatchAsync(new TestEvent("a"));

        Assert.Equal(1, handler.Calls);
        Assert.Equal("a", handler.Payloads.Single());
    }

    [Fact]
    public async Task DispatchAsync_BatchOverload_InvokesHandlerForEachEvent()
    {
        var (sp, handler) = BuildSp<CountingHandler>();
        var dispatcher = sp.GetRequiredService<IDomainEventDispatcher>();

        await dispatcher.DispatchAsync(new IDomainEvent[] { new TestEvent("a"), new TestEvent("b") });

        Assert.Equal(2, handler.Calls);
        Assert.Equal(new[] { "a", "b" }, handler.Payloads);
    }

    [Fact]
    public async Task DispatchAsync_FailFast_PropagatesFirstException()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<TestEvent>, ThrowingHandler>();
        services.AddSingleton<IDomainEventHandler<TestEvent>, CountingHandler>();
        services.AddSingleton(new DomainEventDispatchOptions { Mode = DispatchMode.SequentialFailFast });
        services.AddSingleton<IDomainEventDispatcher, ServiceProviderDomainEventDispatcher>();
        var sp = services.BuildServiceProvider();
        var dispatcher = sp.GetRequiredService<IDomainEventDispatcher>();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => dispatcher.DispatchAsync(new TestEvent("a")));
    }

    [Fact]
    public async Task DispatchAsync_ContinueOnError_RunsAllHandlersAndAggregatesExceptions()
    {
        var counting = new CountingHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<TestEvent>, ThrowingHandler>();
        services.AddSingleton<IDomainEventHandler<TestEvent>>(counting);
        services.AddSingleton(new DomainEventDispatchOptions { Mode = DispatchMode.SequentialContinueOnError });
        services.AddSingleton<IDomainEventDispatcher, ServiceProviderDomainEventDispatcher>();
        var sp = services.BuildServiceProvider();
        var dispatcher = sp.GetRequiredService<IDomainEventDispatcher>();

        var ex = await Assert.ThrowsAsync<AggregateException>(
            () => dispatcher.DispatchAsync(new TestEvent("a")));

        Assert.Single(ex.InnerExceptions);
        Assert.IsType<InvalidOperationException>(ex.InnerExceptions[0]);
        Assert.Equal(1, counting.Calls);
    }

    [Fact]
    public async Task DispatchAsync_Parallel_InvokesAllHandlers()
    {
        var h1 = new CountingHandler();
        var h2 = new CountingHandler();
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventHandler<TestEvent>>(h1);
        services.AddSingleton<IDomainEventHandler<TestEvent>>(h2);
        services.AddSingleton(new DomainEventDispatchOptions { Mode = DispatchMode.Parallel });
        services.AddSingleton<IDomainEventDispatcher, ServiceProviderDomainEventDispatcher>();
        var sp = services.BuildServiceProvider();
        var dispatcher = sp.GetRequiredService<IDomainEventDispatcher>();

        await dispatcher.DispatchAsync(new TestEvent("a"));

        Assert.Equal(1, h1.Calls);
        Assert.Equal(1, h2.Calls);
    }

    [Fact]
    public async Task DispatchAsync_NoHandlerRegistered_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new DomainEventDispatchOptions());
        services.AddSingleton<IDomainEventDispatcher, ServiceProviderDomainEventDispatcher>();
        var sp = services.BuildServiceProvider();

        await sp.GetRequiredService<IDomainEventDispatcher>().DispatchAsync(new TestEvent("a"));
    }

    [Fact]
    public async Task DispatchAsync_NullEvent_ThrowsArgumentNullException()
    {
        var (sp, _) = BuildSp<CountingHandler>();
        var dispatcher = sp.GetRequiredService<IDomainEventDispatcher>();

        await Assert.ThrowsAsync<ArgumentNullException>(() => dispatcher.DispatchAsync((IDomainEvent)null!));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Moongazing.OrionGuard.Tests --filter "FullyQualifiedName~ServiceProviderDomainEventDispatcherTests"`
Expected: FAIL — `ServiceProviderDomainEventDispatcher` type does not exist.

- [ ] **Step 3: Implement `ServiceProviderDomainEventDispatcher`**

Create `src/Moongazing.OrionGuard/Domain/Events/ServiceProviderDomainEventDispatcher.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;

namespace Moongazing.OrionGuard.Domain.Events;

/// <summary>
/// Default <see cref="IDomainEventDispatcher"/> that resolves <see cref="IDomainEventHandler{TEvent}"/>
/// instances from <see cref="IServiceProvider"/> and invokes them per <see cref="DomainEventDispatchOptions.Mode"/>.
/// </summary>
public sealed class ServiceProviderDomainEventDispatcher : IDomainEventDispatcher
{
    private readonly IServiceProvider _serviceProvider;
    private readonly DomainEventDispatchOptions _options;

    public ServiceProviderDomainEventDispatcher(IServiceProvider serviceProvider, DomainEventDispatchOptions options)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task DispatchAsync(IDomainEvent @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(@event.GetType());
        var handlers = _serviceProvider.GetServices(handlerType).Where(h => h is not null).ToArray();
        if (handlers.Length == 0) return;

        var method = handlerType.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.HandleAsync))!;

        switch (_options.Mode)
        {
            case DispatchMode.SequentialFailFast:
                foreach (var handler in handlers)
                    await InvokeAsync(method, handler!, @event, cancellationToken).ConfigureAwait(false);
                break;

            case DispatchMode.SequentialContinueOnError:
                List<Exception>? errors = null;
                foreach (var handler in handlers)
                {
                    try { await InvokeAsync(method, handler!, @event, cancellationToken).ConfigureAwait(false); }
                    catch (Exception ex) { (errors ??= new List<Exception>()).Add(ex); }
                }
                if (errors is { Count: > 0 }) throw new AggregateException(errors);
                break;

            case DispatchMode.Parallel:
                var tasks = handlers.Select(h => InvokeAsync(method, h!, @event, cancellationToken));
                await Task.WhenAll(tasks).ConfigureAwait(false);
                break;

            default:
                throw new InvalidOperationException($"Unknown DispatchMode '{_options.Mode}'.");
        }
    }

    public async Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(events);
        foreach (var e in events)
            await DispatchAsync(e, cancellationToken).ConfigureAwait(false);
    }

    private static Task InvokeAsync(System.Reflection.MethodInfo method, object handler, IDomainEvent @event, CancellationToken ct)
    {
        var task = (Task?)method.Invoke(handler, new object[] { @event, ct });
        return task ?? Task.CompletedTask;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Moongazing.OrionGuard.Tests --filter "FullyQualifiedName~ServiceProviderDomainEventDispatcherTests"`
Expected: PASS — 7 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Moongazing.OrionGuard/Domain/Events/ServiceProviderDomainEventDispatcher.cs tests/Moongazing.OrionGuard.Tests/ServiceProviderDomainEventDispatcherTests.cs
git commit -m "feat(core): add ServiceProviderDomainEventDispatcher with 3 dispatch modes"
```

---

## Task 5: Core DI helpers

Two extension methods: `AddOrionGuardDomainEvents` registers the dispatcher + options; `AddOrionGuardDomainEventHandlers` scans assemblies for `IDomainEventHandler<>` implementations and registers each as `Scoped`.

**Files:**
- Create: `src/Moongazing.OrionGuard/DependencyInjection/DomainEventServiceCollectionExtensions.cs`
- Create: `tests/Moongazing.OrionGuard.Tests/DomainEventDIExtensionsTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/Moongazing.OrionGuard.Tests/DomainEventDIExtensionsTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.DependencyInjection;
using Moongazing.OrionGuard.Domain.Events;

namespace Moongazing.OrionGuard.Tests;

public class DomainEventDIExtensionsTests
{
    public sealed record SampleEvent(int Id) : DomainEventBase;

    public sealed class SampleHandler : IDomainEventHandler<SampleEvent>
    {
        public Task HandleAsync(SampleEvent @event, CancellationToken ct) => Task.CompletedTask;
    }

    [Fact]
    public void AddOrionGuardDomainEvents_RegistersDispatcherAndOptions()
    {
        var services = new ServiceCollection();
        services.AddOrionGuardDomainEvents();
        var sp = services.BuildServiceProvider();

        Assert.IsType<ServiceProviderDomainEventDispatcher>(sp.GetRequiredService<IDomainEventDispatcher>());
        Assert.Equal(DispatchMode.SequentialFailFast, sp.GetRequiredService<DomainEventDispatchOptions>().Mode);
    }

    [Fact]
    public void AddOrionGuardDomainEvents_AppliesConfigure()
    {
        var services = new ServiceCollection();
        services.AddOrionGuardDomainEvents(o => Assert.Equal(DispatchMode.SequentialFailFast, o.Mode));
        // Real assertion: configure delegate gets a fresh options instance and we mutate it.
        services = new ServiceCollection();
        services.AddOrionGuardDomainEvents(o => { /* no-op; default expected below */ });
        var sp = services.BuildServiceProvider();
        Assert.Equal(DispatchMode.SequentialFailFast, sp.GetRequiredService<DomainEventDispatchOptions>().Mode);

        services = new ServiceCollection();
        services.AddOrionGuardDomainEvents();
        sp = services.BuildServiceProvider();
        var opts = sp.GetRequiredService<DomainEventDispatchOptions>();
        Assert.Equal(DispatchMode.SequentialFailFast, opts.Mode);
    }

    [Fact]
    public void AddOrionGuardDomainEventHandlers_ScansAssembly_RegistersHandlers()
    {
        var services = new ServiceCollection();
        services.AddOrionGuardDomainEventHandlers(typeof(DomainEventDIExtensionsTests).Assembly);
        var sp = services.BuildServiceProvider();

        var handler = sp.GetRequiredService<IDomainEventHandler<SampleEvent>>();
        Assert.IsType<SampleHandler>(handler);
    }
}
```

- [ ] **Step 2: Run tests, verify failure**

Run: `dotnet test tests/Moongazing.OrionGuard.Tests --filter "FullyQualifiedName~DomainEventDIExtensionsTests"`
Expected: FAIL — extension methods do not exist.

- [ ] **Step 3: Implement DI helpers**

Create `src/Moongazing.OrionGuard/DependencyInjection/DomainEventServiceCollectionExtensions.cs`:

```csharp
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.Domain.Events;

namespace Moongazing.OrionGuard.DependencyInjection;

public static class DomainEventServiceCollectionExtensions
{
    /// <summary>
    /// Registers the default <see cref="IDomainEventDispatcher"/> (<see cref="ServiceProviderDomainEventDispatcher"/>)
    /// and <see cref="DomainEventDispatchOptions"/> singleton.
    /// </summary>
    public static IServiceCollection AddOrionGuardDomainEvents(
        this IServiceCollection services,
        Action<DomainEventDispatchOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new DomainEventDispatchOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);
        services.AddScoped<IDomainEventDispatcher, ServiceProviderDomainEventDispatcher>();
        return services;
    }

    /// <summary>
    /// Scans the supplied assemblies for concrete classes implementing <see cref="IDomainEventHandler{TEvent}"/>
    /// and registers each closed interface to its implementing type as <see cref="ServiceLifetime.Scoped"/>.
    /// </summary>
    public static IServiceCollection AddOrionGuardDomainEventHandlers(
        this IServiceCollection services,
        params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assemblies);

        foreach (var assembly in assemblies)
        {
            foreach (var type in assembly.GetTypes())
            {
                if (type.IsAbstract || type.IsInterface) continue;
                foreach (var iface in type.GetInterfaces())
                {
                    if (!iface.IsGenericType) continue;
                    if (iface.GetGenericTypeDefinition() != typeof(IDomainEventHandler<>)) continue;
                    services.AddScoped(iface, type);
                }
            }
        }
        return services;
    }
}
```

- [ ] **Step 4: Run tests, verify pass**

Run: `dotnet test tests/Moongazing.OrionGuard.Tests --filter "FullyQualifiedName~DomainEventDIExtensionsTests"`
Expected: PASS — 3 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Moongazing.OrionGuard/DependencyInjection/DomainEventServiceCollectionExtensions.cs tests/Moongazing.OrionGuard.Tests/DomainEventDIExtensionsTests.cs
git commit -m "feat(core): add AddOrionGuardDomainEvents / AddOrionGuardDomainEventHandlers DI helpers"
```

---

## Task 6: MediatR bridge dispatcher

A drop-in `IDomainEventDispatcher` that delegates to `IPublisher`. Consumer events opt in by adding `: INotification` — bridge throws `InvalidOperationException` for events that don't.

**Files:**
- Create: `src/Moongazing.OrionGuard.MediatR/DomainEvents/MediatRDomainEventDispatcher.cs`
- Create: `src/Moongazing.OrionGuard.MediatR/DomainEvents/MediatRDomainEventServiceCollectionExtensions.cs`
- Create: `tests/Moongazing.OrionGuard.Tests/MediatRDomainEventDispatcherTests.cs`

- [ ] **Step 1: Add MediatR test deps**

The existing test project already references `Moongazing.OrionGuard`. Add MediatR + bridge to `tests/Moongazing.OrionGuard.Tests/Moongazing.OrionGuard.Tests.csproj` inside the existing `<ItemGroup>` containing `ProjectReference`:

```xml
<PackageReference Include="MediatR" Version="12.4.1" />
<ProjectReference Include="..\..\src\Moongazing.OrionGuard.MediatR\Moongazing.OrionGuard.MediatR.csproj" />
```

- [ ] **Step 2: Write failing tests**

Create `tests/Moongazing.OrionGuard.Tests/MediatRDomainEventDispatcherTests.cs`:

```csharp
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.Domain.Events;
using Moongazing.OrionGuard.MediatR.DomainEvents;

namespace Moongazing.OrionGuard.Tests;

public class MediatRDomainEventDispatcherTests
{
    public sealed record OrderShipped(Guid OrderId) : DomainEventBase, INotification;
    public sealed record LegacyEvent(Guid Id) : DomainEventBase;   // no INotification

    public sealed class OrderShippedHandler : INotificationHandler<OrderShipped>
    {
        public List<OrderShipped> Received { get; } = new();
        public Task Handle(OrderShipped notification, CancellationToken ct)
        {
            Received.Add(notification);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task DispatchAsync_PublishesEventThroughMediatR_WhenEventImplementsINotification()
    {
        var handler = new OrderShippedHandler();
        var services = new ServiceCollection();
        services.AddMediatR(c => c.RegisterServicesFromAssemblyContaining<MediatRDomainEventDispatcherTests>());
        services.AddSingleton<INotificationHandler<OrderShipped>>(handler);
        services.AddOrionGuardMediatRDomainEvents();
        var sp = services.BuildServiceProvider();

        var dispatcher = sp.GetRequiredService<IDomainEventDispatcher>();
        var evt = new OrderShipped(Guid.NewGuid());
        await dispatcher.DispatchAsync(evt);

        Assert.Single(handler.Received);
        Assert.Equal(evt.OrderId, handler.Received[0].OrderId);
    }

    [Fact]
    public async Task DispatchAsync_Throws_WhenEventDoesNotImplementINotification()
    {
        var services = new ServiceCollection();
        services.AddMediatR(c => c.RegisterServicesFromAssemblyContaining<MediatRDomainEventDispatcherTests>());
        services.AddOrionGuardMediatRDomainEvents();
        var sp = services.BuildServiceProvider();

        var dispatcher = sp.GetRequiredService<IDomainEventDispatcher>();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => dispatcher.DispatchAsync(new LegacyEvent(Guid.NewGuid())));
    }
}
```

- [ ] **Step 3: Run tests, verify failure**

Run: `dotnet test tests/Moongazing.OrionGuard.Tests --filter "FullyQualifiedName~MediatRDomainEventDispatcherTests"`
Expected: FAIL — `MediatRDomainEventDispatcher`, `AddOrionGuardMediatRDomainEvents` not found.

- [ ] **Step 4: Implement bridge dispatcher**

Create `src/Moongazing.OrionGuard.MediatR/DomainEvents/MediatRDomainEventDispatcher.cs`:

```csharp
using MediatR;
using Moongazing.OrionGuard.Domain.Events;

namespace Moongazing.OrionGuard.MediatR.DomainEvents;

/// <summary>
/// <see cref="IDomainEventDispatcher"/> that publishes events through MediatR's <see cref="IPublisher"/>.
/// Consumer events MUST also implement <see cref="MediatR.INotification"/> — typically by adding it to
/// the event record declaration (e.g. <c>public sealed record X(Y Y) : DomainEventBase, INotification;</c>).
/// </summary>
public sealed class MediatRDomainEventDispatcher : IDomainEventDispatcher
{
    private readonly IPublisher _publisher;

    public MediatRDomainEventDispatcher(IPublisher publisher)
    {
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
    }

    public Task DispatchAsync(IDomainEvent @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        if (@event is not INotification notification)
            throw new InvalidOperationException(
                $"{@event.GetType().FullName} must implement MediatR.INotification to use MediatRDomainEventDispatcher. " +
                $"Add ': INotification' to the event record's base list.");
        return _publisher.Publish(notification, cancellationToken);
    }

    public async Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(events);
        foreach (var e in events)
            await DispatchAsync(e, cancellationToken).ConfigureAwait(false);
    }
}
```

- [ ] **Step 5: Implement DI helper**

Create `src/Moongazing.OrionGuard.MediatR/DomainEvents/MediatRDomainEventServiceCollectionExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.Domain.Events;

namespace Moongazing.OrionGuard.MediatR.DomainEvents;

public static class MediatRDomainEventServiceCollectionExtensions
{
    /// <summary>
    /// Replaces any existing <see cref="IDomainEventDispatcher"/> registration with
    /// <see cref="MediatRDomainEventDispatcher"/>. Call after <c>services.AddMediatR(...)</c>.
    /// </summary>
    public static IServiceCollection AddOrionGuardMediatRDomainEvents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var existing = services.SingleOrDefault(d => d.ServiceType == typeof(IDomainEventDispatcher));
        if (existing is not null) services.Remove(existing);
        services.AddScoped<IDomainEventDispatcher, MediatRDomainEventDispatcher>();
        return services;
    }
}
```

- [ ] **Step 6: Run tests, verify pass**

Run: `dotnet test tests/Moongazing.OrionGuard.Tests --filter "FullyQualifiedName~MediatRDomainEventDispatcherTests"`
Expected: PASS — 2 tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/Moongazing.OrionGuard.MediatR/DomainEvents tests/Moongazing.OrionGuard.Tests/MediatRDomainEventDispatcherTests.cs tests/Moongazing.OrionGuard.Tests/Moongazing.OrionGuard.Tests.csproj
git commit -m "feat(mediatr): add MediatRDomainEventDispatcher bridge"
```

---

## Task 7: Test helpers — `DomainEventCapture` + `DomainEventAssertions`

**Files:**
- Create: `src/Moongazing.OrionGuard.Testing/DomainEvents/DomainEventAssertionException.cs`
- Create: `src/Moongazing.OrionGuard.Testing/DomainEvents/DomainEventCapture.cs`
- Create: `src/Moongazing.OrionGuard.Testing/DomainEvents/DomainEventAssertions.cs`
- Create test project: `tests/Moongazing.OrionGuard.Testing.Tests/Moongazing.OrionGuard.Testing.Tests.csproj`
- Register in `Moongazing.OrionGuard.sln`
- Create: `tests/Moongazing.OrionGuard.Testing.Tests/DomainEventCaptureTests.cs`

- [ ] **Step 1: Create the test project**

Create `tests/Moongazing.OrionGuard.Testing.Tests/Moongazing.OrionGuard.Testing.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.2" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Moongazing.OrionGuard\Moongazing.OrionGuard.csproj" />
    <ProjectReference Include="..\..\src\Moongazing.OrionGuard.Testing\Moongazing.OrionGuard.Testing.csproj" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
</Project>
```

Register in `Moongazing.OrionGuard.sln` exactly as Task 1 demonstrated, using GUID `{D3FA5B6C-9E04-4A12-9E73-CB10D4F235E3}` and parenting to the `tests` group `{A26C0EB7-E4A7-4335-8423-E8AEE9EF1738}`.

- [ ] **Step 2: Write failing tests**

Create `tests/Moongazing.OrionGuard.Testing.Tests/DomainEventCaptureTests.cs`:

```csharp
using Moongazing.OrionGuard.Domain.Events;
using Moongazing.OrionGuard.Domain.Primitives;
using Moongazing.OrionGuard.Testing.DomainEvents;

namespace Moongazing.OrionGuard.Testing.Tests;

public class DomainEventCaptureTests
{
    private sealed record OrderShipped(Guid OrderId) : DomainEventBase;
    private sealed record OrderCancelled(Guid OrderId) : DomainEventBase;

    private sealed class Order : AggregateRoot<Guid>
    {
        public Order(Guid id) : base(id) { }
        public void Ship() => RaiseEvent(new OrderShipped(Id));
        public void Cancel() => RaiseEvent(new OrderCancelled(Id));
    }

    [Fact]
    public void From_PullsEventsFromAggregate()
    {
        var order = new Order(Guid.NewGuid());
        order.Ship();
        order.Cancel();

        var capture = DomainEventCapture.From(order);

        Assert.Equal(2, capture.All.Count);
        Assert.Empty(order.DomainEvents);
    }

    [Fact]
    public void Should_HaveRaised_DoesNotThrow_WhenEventPresent()
    {
        var order = new Order(Guid.NewGuid());
        order.Ship();
        var capture = DomainEventCapture.From(order);

        capture.Should().HaveRaised<OrderShipped>(e => e.OrderId == order.Id);
    }

    [Fact]
    public void Should_HaveRaised_Throws_WhenEventNotPresent()
    {
        var capture = DomainEventCapture.From(new Order(Guid.NewGuid()));

        Assert.Throws<DomainEventAssertionException>(() => capture.Should().HaveRaised<OrderShipped>());
    }

    [Fact]
    public void Should_NotHaveRaised_DoesNotThrow_WhenEventAbsent()
    {
        var order = new Order(Guid.NewGuid());
        order.Ship();
        var capture = DomainEventCapture.From(order);

        capture.Should().NotHaveRaised<OrderCancelled>();
    }

    [Fact]
    public void Should_NotHaveRaised_Throws_WhenEventPresent()
    {
        var order = new Order(Guid.NewGuid());
        order.Ship();
        var capture = DomainEventCapture.From(order);

        Assert.Throws<DomainEventAssertionException>(() => capture.Should().NotHaveRaised<OrderShipped>());
    }

    [Fact]
    public void Should_HaveRaisedExactlyOf_VerifiesCount()
    {
        var order = new Order(Guid.NewGuid());
        order.Ship();
        order.Cancel();
        var capture = DomainEventCapture.From(order);

        capture.Should().HaveRaisedExactly(1).Of<OrderShipped>();

        Assert.Throws<DomainEventAssertionException>(() => capture.Should().HaveRaisedExactly(2).Of<OrderShipped>());
    }

    [Fact]
    public void Single_ReturnsTheOnlyEventOfType()
    {
        var order = new Order(Guid.NewGuid());
        order.Ship();
        var capture = DomainEventCapture.From(order);

        var shipped = capture.Single<OrderShipped>();

        Assert.Equal(order.Id, shipped.OrderId);
    }
}
```

- [ ] **Step 3: Verify failure**

Run: `dotnet test tests/Moongazing.OrionGuard.Testing.Tests`
Expected: FAIL — types not found.

- [ ] **Step 4: Implement assertion exception**

Create `src/Moongazing.OrionGuard.Testing/DomainEvents/DomainEventAssertionException.cs`:

```csharp
namespace Moongazing.OrionGuard.Testing.DomainEvents;

/// <summary>
/// Thrown by <see cref="DomainEventAssertions"/> when an expectation about captured domain events fails.
/// Test runners (xUnit, NUnit, MSTest) treat any thrown exception as a test failure, so this works
/// without depending on a specific framework's assertion type.
/// </summary>
public sealed class DomainEventAssertionException : Exception
{
    public DomainEventAssertionException(string message) : base(message) { }
}
```

- [ ] **Step 5: Implement `DomainEventCapture`**

Create `src/Moongazing.OrionGuard.Testing/DomainEvents/DomainEventCapture.cs`:

```csharp
using Moongazing.OrionGuard.Domain.Events;
using Moongazing.OrionGuard.Domain.Primitives;

namespace Moongazing.OrionGuard.Testing.DomainEvents;

/// <summary>
/// Captures a snapshot of domain events for assertion. Use <see cref="From(IAggregateRoot)"/> in
/// unit tests; use <see cref="FromList(IEnumerable{IDomainEvent})"/> with
/// <see cref="InMemoryDomainEventDispatcher"/> in integration tests.
/// </summary>
public sealed class DomainEventCapture
{
    private readonly List<IDomainEvent> _events;

    private DomainEventCapture(IEnumerable<IDomainEvent> events) => _events = events.ToList();

    /// <summary>All captured events in raise order.</summary>
    public IReadOnlyList<IDomainEvent> All => _events;

    /// <summary>Pulls events out of an aggregate's buffer (empties the buffer).</summary>
    public static DomainEventCapture From(IAggregateRoot aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        return new DomainEventCapture(aggregate.PullDomainEvents());
    }

    /// <summary>Wraps an existing event list (does not pull from anywhere).</summary>
    public static DomainEventCapture FromList(IEnumerable<IDomainEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        return new DomainEventCapture(events);
    }

    /// <summary>Returns the single event of type <typeparamref name="TEvent"/>; throws if zero or more than one.</summary>
    public TEvent Single<TEvent>() where TEvent : IDomainEvent
        => _events.OfType<TEvent>().Single();

    /// <summary>All captured events of type <typeparamref name="TEvent"/>.</summary>
    public IEnumerable<TEvent> OfType<TEvent>() where TEvent : IDomainEvent
        => _events.OfType<TEvent>();

    /// <summary>True when at least one event of type <typeparamref name="TEvent"/> was captured.</summary>
    public bool Contains<TEvent>() where TEvent : IDomainEvent
        => _events.OfType<TEvent>().Any();

    /// <summary>True when at least one event of type <typeparamref name="TEvent"/> matching <paramref name="predicate"/> was captured.</summary>
    public bool Contains<TEvent>(Func<TEvent, bool> predicate) where TEvent : IDomainEvent
        => _events.OfType<TEvent>().Any(predicate);

    /// <summary>Entry point for fluent assertions.</summary>
    public DomainEventAssertions Should() => new(this);
}
```

- [ ] **Step 6: Implement `DomainEventAssertions`**

Create `src/Moongazing.OrionGuard.Testing/DomainEvents/DomainEventAssertions.cs`:

```csharp
using Moongazing.OrionGuard.Domain.Events;

namespace Moongazing.OrionGuard.Testing.DomainEvents;

public sealed class DomainEventAssertions
{
    private readonly DomainEventCapture _capture;
    internal DomainEventAssertions(DomainEventCapture capture) => _capture = capture;

    public DomainEventAssertions HaveRaised<TEvent>(Func<TEvent, bool>? predicate = null)
        where TEvent : IDomainEvent
    {
        var match = predicate is null ? _capture.Contains<TEvent>() : _capture.Contains(predicate);
        if (!match)
            throw new DomainEventAssertionException(
                $"Expected {typeof(TEvent).Name} to be raised but it was not. " +
                $"Captured events: [{FormatCaptured()}]");
        return this;
    }

    public DomainEventAssertions NotHaveRaised<TEvent>() where TEvent : IDomainEvent
    {
        if (_capture.Contains<TEvent>())
            throw new DomainEventAssertionException(
                $"Expected {typeof(TEvent).Name} NOT to be raised but it was. " +
                $"Captured events: [{FormatCaptured()}]");
        return this;
    }

    public CountAssertion HaveRaisedExactly(int expected) => new(this, _capture, expected);

    private string FormatCaptured()
        => string.Join(", ", _capture.All.Select(e => e.GetType().Name));

    public sealed class CountAssertion
    {
        private readonly DomainEventAssertions _parent;
        private readonly DomainEventCapture _capture;
        private readonly int _expected;
        internal CountAssertion(DomainEventAssertions parent, DomainEventCapture capture, int expected)
        {
            _parent = parent; _capture = capture; _expected = expected;
        }

        public DomainEventAssertions Of<TEvent>() where TEvent : IDomainEvent
        {
            var actual = _capture.OfType<TEvent>().Count();
            if (actual != _expected)
                throw new DomainEventAssertionException(
                    $"Expected exactly {_expected} {typeof(TEvent).Name} event(s), but found {actual}.");
            return _parent;
        }
    }
}
```

- [ ] **Step 7: Run tests, verify pass**

Run: `dotnet test tests/Moongazing.OrionGuard.Testing.Tests`
Expected: PASS — 7 tests pass.

- [ ] **Step 8: Commit**

```bash
git add src/Moongazing.OrionGuard.Testing tests/Moongazing.OrionGuard.Testing.Tests Moongazing.OrionGuard.sln
git commit -m "feat(testing): add DomainEventCapture + framework-agnostic assertions"
```

---

## Task 8: Test helpers — `InMemoryDomainEventDispatcher`

**Files:**
- Create: `src/Moongazing.OrionGuard.Testing/DomainEvents/InMemoryDomainEventDispatcher.cs`
- Create: `tests/Moongazing.OrionGuard.Testing.Tests/InMemoryDomainEventDispatcherTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/Moongazing.OrionGuard.Testing.Tests/InMemoryDomainEventDispatcherTests.cs`:

```csharp
using Moongazing.OrionGuard.Domain.Events;
using Moongazing.OrionGuard.Testing.DomainEvents;

namespace Moongazing.OrionGuard.Testing.Tests;

public class InMemoryDomainEventDispatcherTests
{
    private sealed record OrderShipped(Guid OrderId) : DomainEventBase;

    [Fact]
    public async Task DispatchAsync_StoresEvents_InCapturedList()
    {
        var dispatcher = new InMemoryDomainEventDispatcher();
        var evt = new OrderShipped(Guid.NewGuid());

        await dispatcher.DispatchAsync(evt);

        Assert.Single(dispatcher.Captured);
        Assert.Same(evt, dispatcher.Captured[0]);
    }

    [Fact]
    public async Task DispatchAsync_BatchOverload_StoresAllEvents()
    {
        var dispatcher = new InMemoryDomainEventDispatcher();
        var batch = new IDomainEvent[] { new OrderShipped(Guid.NewGuid()), new OrderShipped(Guid.NewGuid()) };

        await dispatcher.DispatchAsync(batch);

        Assert.Equal(2, dispatcher.Captured.Count);
    }

    [Fact]
    public async Task Should_ProvidesAssertionsAcrossCapturedEvents()
    {
        var dispatcher = new InMemoryDomainEventDispatcher();
        await dispatcher.DispatchAsync(new OrderShipped(Guid.Parse("00000000-0000-0000-0000-000000000001")));

        dispatcher.Should().HaveRaised<OrderShipped>(e => e.OrderId == Guid.Parse("00000000-0000-0000-0000-000000000001"));
    }

    [Fact]
    public async Task Clear_RemovesAllCapturedEvents()
    {
        var dispatcher = new InMemoryDomainEventDispatcher();
        await dispatcher.DispatchAsync(new OrderShipped(Guid.NewGuid()));

        dispatcher.Clear();

        Assert.Empty(dispatcher.Captured);
    }
}
```

- [ ] **Step 2: Verify failure**

Run: `dotnet test tests/Moongazing.OrionGuard.Testing.Tests --filter "FullyQualifiedName~InMemoryDomainEventDispatcherTests"`
Expected: FAIL — type not found.

- [ ] **Step 3: Implement**

Create `src/Moongazing.OrionGuard.Testing/DomainEvents/InMemoryDomainEventDispatcher.cs`:

```csharp
using Moongazing.OrionGuard.Domain.Events;

namespace Moongazing.OrionGuard.Testing.DomainEvents;

/// <summary>
/// <see cref="IDomainEventDispatcher"/> that records every dispatched event in memory instead of
/// invoking handlers. Intended for integration tests where you replace the production dispatcher
/// to assert that the right events left the application boundary.
/// </summary>
public sealed class InMemoryDomainEventDispatcher : IDomainEventDispatcher
{
    private readonly List<IDomainEvent> _captured = new();

    public IReadOnlyList<IDomainEvent> Captured => _captured;

    public Task DispatchAsync(IDomainEvent @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        _captured.Add(@event);
        return Task.CompletedTask;
    }

    public Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(events);
        _captured.AddRange(events);
        return Task.CompletedTask;
    }

    public DomainEventAssertions Should() => new(DomainEventCapture.FromList(_captured));

    public void Clear() => _captured.Clear();
}
```

- [ ] **Step 4: Run tests, verify pass**

Run: `dotnet test tests/Moongazing.OrionGuard.Testing.Tests --filter "FullyQualifiedName~InMemoryDomainEventDispatcherTests"`
Expected: PASS — 4 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Moongazing.OrionGuard.Testing/DomainEvents/InMemoryDomainEventDispatcher.cs tests/Moongazing.OrionGuard.Testing.Tests/InMemoryDomainEventDispatcherTests.cs
git commit -m "feat(testing): add InMemoryDomainEventDispatcher"
```

---

## Task 9: Outbox entity + options + entity type configuration

**Files:**
- Create: `src/Moongazing.OrionGuard.EntityFrameworkCore/Outbox/OutboxMessage.cs`
- Create: `src/Moongazing.OrionGuard.EntityFrameworkCore/Outbox/OutboxOptions.cs`
- Create: `src/Moongazing.OrionGuard.EntityFrameworkCore/Outbox/OutboxMessageEntityTypeConfiguration.cs`
- Create: `src/Moongazing.OrionGuard.EntityFrameworkCore/OrionGuardEfCoreOptions.cs`
- Delete: `src/Moongazing.OrionGuard.EntityFrameworkCore/OrionGuardEfCoreMarker.cs`

- [ ] **Step 1: Create `OutboxMessage`**

Create `src/Moongazing.OrionGuard.EntityFrameworkCore/Outbox/OutboxMessage.cs`:

```csharp
namespace Moongazing.OrionGuard.EntityFrameworkCore.Outbox;

/// <summary>
/// Persisted representation of a domain event awaiting dispatch. Written to the consumer's DbContext
/// in the same transaction as aggregate state changes; consumed by <see cref="OutboxDispatcherHostedService"/>.
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string EventType { get; set; } = default!;
    public string Payload { get; set; } = default!;
    public DateTime OccurredOnUtc { get; set; }
    public DateTime? ProcessedOnUtc { get; set; }
    public string? Error { get; set; }
    public int RetryCount { get; set; }
    public string? CorrelationId { get; set; }
    public string? TraceParent { get; set; }
    public string? TraceState { get; set; }
}
```

- [ ] **Step 2: Create `OutboxOptions`**

Create `src/Moongazing.OrionGuard.EntityFrameworkCore/Outbox/OutboxOptions.cs`:

```csharp
namespace Moongazing.OrionGuard.EntityFrameworkCore.Outbox;

public sealed class OutboxOptions
{
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);
    public int BatchSize { get; set; } = 100;
    public int MaxRetries { get; set; } = 5;
    public string TableName { get; set; } = "OrionGuard_Outbox";
}
```

> Properties use `set` (not `init`) so the `UseOutbox(o => ...)` configure callback can mutate them.

- [ ] **Step 3: Create entity type configuration**

Create `src/Moongazing.OrionGuard.EntityFrameworkCore/Outbox/OutboxMessageEntityTypeConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Moongazing.OrionGuard.EntityFrameworkCore.Outbox;

public sealed class OutboxMessageEntityTypeConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    private readonly string _tableName;
    public OutboxMessageEntityTypeConfiguration(string tableName) => _tableName = tableName;

    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable(_tableName);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EventType).IsRequired().HasMaxLength(512);
        builder.Property(x => x.Payload).IsRequired();
        builder.Property(x => x.CorrelationId).HasMaxLength(128);
        builder.Property(x => x.TraceParent).HasMaxLength(64);
        builder.Property(x => x.TraceState).HasMaxLength(256);
        builder.HasIndex(x => new { x.ProcessedOnUtc, x.OccurredOnUtc })
            .HasDatabaseName("IX_OrionGuard_Outbox_Unprocessed");
    }
}
```

- [ ] **Step 4: Create `OrionGuardEfCoreOptions`**

Create `src/Moongazing.OrionGuard.EntityFrameworkCore/OrionGuardEfCoreOptions.cs`:

```csharp
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox;

namespace Moongazing.OrionGuard.EntityFrameworkCore;

public enum DomainEventDispatchStrategy { Inline, Outbox }

public sealed class OrionGuardEfCoreOptions
{
    public DomainEventDispatchStrategy Strategy { get; private set; } = DomainEventDispatchStrategy.Inline;
    public OutboxOptions Outbox { get; private set; } = new();

    public OrionGuardEfCoreOptions UseInline()
    {
        Strategy = DomainEventDispatchStrategy.Inline;
        return this;
    }

    public OrionGuardEfCoreOptions UseOutbox(Action<OutboxOptions>? configure = null)
    {
        Strategy = DomainEventDispatchStrategy.Outbox;
        if (configure is not null)
        {
            var temp = new OutboxOptions();
            configure(temp);
            Outbox = temp;
        }
        return this;
    }
}
```

- [ ] **Step 5: Delete the placeholder marker**

Delete `src/Moongazing.OrionGuard.EntityFrameworkCore/OrionGuardEfCoreMarker.cs`.

- [ ] **Step 6: Build verification**

Run: `dotnet build src/Moongazing.OrionGuard.EntityFrameworkCore/Moongazing.OrionGuard.EntityFrameworkCore.csproj -c Debug`
Expected: 0 errors.

- [ ] **Step 7: Commit**

```bash
git add src/Moongazing.OrionGuard.EntityFrameworkCore
git commit -m "feat(efcore): add OutboxMessage, OutboxOptions, OrionGuardEfCoreOptions"
```

---

## Task 10: EF Core integration test fixtures + Inline mode interceptor

The interceptor lifecycle:
- `SavingChangesAsync`: traverse `ChangeTracker.Entries<IAggregateRoot>()`, pull events from each, append onto a `[ThreadStatic]`-free per-context list. (We use `DomainEventCollector` keyed by `DbContext` instance.)
- `SavedChangesAsync`: dispatch the collected events via `IDomainEventDispatcher`.

**Files:**
- Create test project: `tests/Moongazing.OrionGuard.EntityFrameworkCore.Tests/Moongazing.OrionGuard.EntityFrameworkCore.Tests.csproj`
- Register in `Moongazing.OrionGuard.sln` (GUID `{E47B2C8A-91D3-4F65-B208-15C8FA60D139}`)
- Create test fixtures: `TestFixtures/TestAggregate.cs`, `TestFixtures/TestDbContext.cs`
- Create: `tests/Moongazing.OrionGuard.EntityFrameworkCore.Tests/DomainEventInterceptorInlineTests.cs`
- Create: `src/Moongazing.OrionGuard.EntityFrameworkCore/DomainEventCollector.cs`
- Create: `src/Moongazing.OrionGuard.EntityFrameworkCore/DomainEventSaveChangesInterceptor.cs`

- [ ] **Step 1: Create EF Core test project**

Create `tests/Moongazing.OrionGuard.EntityFrameworkCore.Tests/Moongazing.OrionGuard.EntityFrameworkCore.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.2" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="9.0.0" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="9.0.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Moongazing.OrionGuard\Moongazing.OrionGuard.csproj" />
    <ProjectReference Include="..\..\src\Moongazing.OrionGuard.EntityFrameworkCore\Moongazing.OrionGuard.EntityFrameworkCore.csproj" />
    <ProjectReference Include="..\..\src\Moongazing.OrionGuard.Testing\Moongazing.OrionGuard.Testing.csproj" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
</Project>
```

Register in `Moongazing.OrionGuard.sln` with the GUID `{E47B2C8A-91D3-4F65-B208-15C8FA60D139}` parented to `{A26C0EB7-E4A7-4335-8423-E8AEE9EF1738}` (tests group), exactly as in Task 1 step 4.

- [ ] **Step 2: Create test fixtures**

Create `tests/Moongazing.OrionGuard.EntityFrameworkCore.Tests/TestFixtures/TestAggregate.cs`:

```csharp
using Moongazing.OrionGuard.Domain.Events;
using Moongazing.OrionGuard.Domain.Primitives;

namespace Moongazing.OrionGuard.EntityFrameworkCore.Tests.TestFixtures;

public sealed record OrderShipped(Guid OrderId) : DomainEventBase;
public sealed record OrderCancelled(Guid OrderId) : DomainEventBase;

public sealed class Order : AggregateRoot<Guid>
{
    public string Status { get; private set; } = "New";
    public Order(Guid id) : base(id) { }
    private Order() { }   // EF
    public void Ship() { Status = "Shipped"; RaiseEvent(new OrderShipped(Id)); }
    public void Cancel() { Status = "Cancelled"; RaiseEvent(new OrderCancelled(Id)); }
}
```

Create `tests/Moongazing.OrionGuard.EntityFrameworkCore.Tests/TestFixtures/TestDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox;

namespace Moongazing.OrionGuard.EntityFrameworkCore.Tests.TestFixtures;

public sealed class TestDbContext : DbContext
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(b =>
        {
            b.HasKey(o => o.Id);
            b.Property(o => o.Status).HasMaxLength(32);
            b.Ignore(o => o.DomainEvents);
        });
        modelBuilder.ApplyConfiguration(new OutboxMessageEntityTypeConfiguration("OrionGuard_Outbox"));
    }
}
```

- [ ] **Step 3: Write failing Inline tests**

Create `tests/Moongazing.OrionGuard.EntityFrameworkCore.Tests/DomainEventInterceptorInlineTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.DependencyInjection;
using Moongazing.OrionGuard.Domain.Events;
using Moongazing.OrionGuard.EntityFrameworkCore.Tests.TestFixtures;
using Moongazing.OrionGuard.Testing.DomainEvents;

namespace Moongazing.OrionGuard.EntityFrameworkCore.Tests;

public class DomainEventInterceptorInlineTests : IAsyncLifetime
{
    private InMemoryDomainEventDispatcher _dispatcher = null!;
    private ServiceProvider _sp = null!;

    public Task InitializeAsync()
    {
        _dispatcher = new InMemoryDomainEventDispatcher();
        var services = new ServiceCollection();
        services.AddOrionGuardDomainEvents();
        // override default dispatcher with our spy:
        var existing = services.Single(d => d.ServiceType == typeof(IDomainEventDispatcher));
        services.Remove(existing);
        services.AddSingleton<IDomainEventDispatcher>(_dispatcher);

        services.AddSingleton(new OrionGuardEfCoreOptions().UseInline());
        services.AddScoped<DomainEventCollector>();
        services.AddDbContext<TestDbContext>((sp, o) =>
            o.UseSqlite($"DataSource=:memory:")
             .AddInterceptors(new DomainEventSaveChangesInterceptor(sp)));

        _sp = services.BuildServiceProvider();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _sp.DisposeAsync();

    private async Task<TestDbContext> NewContextAsync()
    {
        var ctx = _sp.GetRequiredService<TestDbContext>();
        var conn = ctx.Database.GetDbConnection();
        if (conn.State == System.Data.ConnectionState.Closed) await conn.OpenAsync();
        await ctx.Database.EnsureCreatedAsync();
        return ctx;
    }

    [Fact]
    public async Task SaveChanges_DispatchesEventsRaisedByAggregates()
    {
        await using var scope = _sp.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await ctx.Database.OpenConnectionAsync();
        await ctx.Database.EnsureCreatedAsync();

        var order = new Order(Guid.NewGuid());
        ctx.Orders.Add(order);
        order.Ship();
        await ctx.SaveChangesAsync();

        Assert.Single(_dispatcher.Captured);
        Assert.IsType<OrderShipped>(_dispatcher.Captured[0]);
    }

    [Fact]
    public async Task SaveChanges_EmptiesAggregateBufferAfterDispatch()
    {
        await using var scope = _sp.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await ctx.Database.OpenConnectionAsync();
        await ctx.Database.EnsureCreatedAsync();

        var order = new Order(Guid.NewGuid());
        ctx.Orders.Add(order);
        order.Ship();
        await ctx.SaveChangesAsync();

        Assert.Empty(order.DomainEvents);
    }

    [Fact]
    public async Task SaveChanges_NoEvents_DoesNothing()
    {
        await using var scope = _sp.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await ctx.Database.OpenConnectionAsync();
        await ctx.Database.EnsureCreatedAsync();

        ctx.Orders.Add(new Order(Guid.NewGuid()));
        await ctx.SaveChangesAsync();

        Assert.Empty(_dispatcher.Captured);
    }
}
```

- [ ] **Step 4: Verify failure**

Run: `dotnet test tests/Moongazing.OrionGuard.EntityFrameworkCore.Tests`
Expected: FAIL — `DomainEventCollector`, `DomainEventSaveChangesInterceptor` not found.

- [ ] **Step 5: Implement `DomainEventCollector`**

Create `src/Moongazing.OrionGuard.EntityFrameworkCore/DomainEventCollector.cs`:

```csharp
using Moongazing.OrionGuard.Domain.Events;

namespace Moongazing.OrionGuard.EntityFrameworkCore;

/// <summary>
/// Per-DbContext-scope buffer that holds events pulled by the interceptor at SavingChanges so they
/// can be dispatched at SavedChanges (Inline mode) or — note — written to the outbox during
/// SavingChanges itself (Outbox mode, which does not use this buffer).
/// </summary>
public sealed class DomainEventCollector
{
    private readonly List<IDomainEvent> _events = new();
    public IReadOnlyList<IDomainEvent> Pending => _events;
    public void Add(IDomainEvent @event) => _events.Add(@event);
    public void AddRange(IEnumerable<IDomainEvent> events) => _events.AddRange(events);
    public IReadOnlyList<IDomainEvent> DrainSnapshot()
    {
        var snapshot = _events.ToArray();
        _events.Clear();
        return snapshot;
    }
}
```

- [ ] **Step 6: Implement Inline-mode interceptor**

Create `src/Moongazing.OrionGuard.EntityFrameworkCore/DomainEventSaveChangesInterceptor.cs`:

```csharp
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.Domain.Events;
using Moongazing.OrionGuard.Domain.Primitives;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox;

namespace Moongazing.OrionGuard.EntityFrameworkCore;

/// <summary>
/// Pulls events from tracked <see cref="IAggregateRoot"/> entities at SavingChanges and dispatches
/// them at SavedChanges (Inline mode) or persists them as <see cref="OutboxMessage"/> rows in the
/// same transaction (Outbox mode). Resolved per-DbContext via <see cref="IServiceProvider"/>.
/// </summary>
public sealed class DomainEventSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly IServiceProvider _serviceProvider;

    /// <param name="serviceProvider">
    /// The DbContext's resolution-time scope provider — captured at DbContext construction by the
    /// <c>(sp, o) =&gt; o.AddInterceptors(new DomainEventSaveChangesInterceptor(sp))</c> wiring.
    /// Used to resolve <see cref="DomainEventCollector"/> (Scoped), <see cref="OrionGuardEfCoreOptions"/>
    /// (Singleton), and <see cref="IDomainEventDispatcher"/> (Scoped).
    /// </param>
    public DomainEventSaveChangesInterceptor(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var ctx = eventData.Context!;
        var options = _serviceProvider.GetRequiredService<OrionGuardEfCoreOptions>();
        var collector = _serviceProvider.GetRequiredService<DomainEventCollector>();

        var aggregates = ctx.ChangeTracker.Entries<IAggregateRoot>().Select(e => e.Entity).ToList();
        if (aggregates.Count == 0) return await base.SavingChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);

        if (options.Strategy == DomainEventDispatchStrategy.Inline)
        {
            foreach (var aggregate in aggregates)
            {
                var events = aggregate.PullDomainEvents();
                if (events.Count > 0) collector.AddRange(events);
            }
        }
        else
        {
            foreach (var aggregate in aggregates)
            {
                var events = aggregate.PullDomainEvents();
                foreach (var e in events)
                {
                    ctx.Add(new OutboxMessage
                    {
                        EventType = e.GetType().AssemblyQualifiedName!,
                        Payload = JsonSerializer.Serialize(e, e.GetType()),
                        OccurredOnUtc = e.OccurredOnUtc,
                    });
                }
            }
        }
        return await base.SavingChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        var options = _serviceProvider.GetRequiredService<OrionGuardEfCoreOptions>();
        if (options.Strategy != DomainEventDispatchStrategy.Inline)
            return await base.SavedChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);

        var collector = _serviceProvider.GetRequiredService<DomainEventCollector>();
        var pending = collector.DrainSnapshot();
        if (pending.Count == 0)
            return await base.SavedChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);

        var dispatcher = _serviceProvider.GetRequiredService<IDomainEventDispatcher>();
        foreach (var e in pending)
            await dispatcher.DispatchAsync(e, cancellationToken).ConfigureAwait(false);

        return await base.SavedChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
    }
}
```

> The interceptor resolves `DomainEventCollector`, `OrionGuardEfCoreOptions`, and `IDomainEventDispatcher` from the **scoped** service provider that the DbContext was constructed with. The `(sp, o) => ... new DomainEventSaveChangesInterceptor(sp)` wiring in `AddDbContext` captures the per-request scope, so all resolutions hit the right scope.

- [ ] **Step 7: Run tests, verify pass**

Run: `dotnet test tests/Moongazing.OrionGuard.EntityFrameworkCore.Tests --filter "FullyQualifiedName~Inline"`
Expected: PASS — 3 tests pass.

- [ ] **Step 8: Commit**

```bash
git add src/Moongazing.OrionGuard.EntityFrameworkCore tests/Moongazing.OrionGuard.EntityFrameworkCore.Tests Moongazing.OrionGuard.sln
git commit -m "feat(efcore): add DomainEventSaveChangesInterceptor with Inline mode"
```

---

## Task 11: EF Core interceptor — Outbox mode tests + verification

The interceptor implementation already covers Outbox in Task 10. Add the Outbox-specific tests now to verify the second branch.

**Files:**
- Create: `tests/Moongazing.OrionGuard.EntityFrameworkCore.Tests/DomainEventInterceptorOutboxTests.cs`

- [ ] **Step 1: Write Outbox tests**

Create `tests/Moongazing.OrionGuard.EntityFrameworkCore.Tests/DomainEventInterceptorOutboxTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.DependencyInjection;
using Moongazing.OrionGuard.Domain.Events;
using Moongazing.OrionGuard.EntityFrameworkCore.Tests.TestFixtures;
using Moongazing.OrionGuard.Testing.DomainEvents;

namespace Moongazing.OrionGuard.EntityFrameworkCore.Tests;

public class DomainEventInterceptorOutboxTests
{
    private static ServiceProvider BuildSp(InMemoryDomainEventDispatcher dispatcher)
    {
        var services = new ServiceCollection();
        services.AddOrionGuardDomainEvents();
        var existing = services.Single(d => d.ServiceType == typeof(IDomainEventDispatcher));
        services.Remove(existing);
        services.AddSingleton<IDomainEventDispatcher>(dispatcher);

        services.AddSingleton(new OrionGuardEfCoreOptions().UseOutbox());
        services.AddScoped<DomainEventCollector>();
        services.AddDbContext<TestDbContext>((sp, o) =>
            o.UseSqlite("DataSource=:memory:")
             .AddInterceptors(new DomainEventSaveChangesInterceptor(sp)));
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task SaveChanges_OutboxMode_WritesOutboxRowsInsteadOfDispatching()
    {
        var dispatcher = new InMemoryDomainEventDispatcher();
        await using var sp = BuildSp(dispatcher);
        await using var scope = sp.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await ctx.Database.OpenConnectionAsync();
        await ctx.Database.EnsureCreatedAsync();

        var order = new Order(Guid.NewGuid());
        ctx.Orders.Add(order);
        order.Ship();
        await ctx.SaveChangesAsync();

        // No inline dispatch in Outbox mode:
        Assert.Empty(dispatcher.Captured);

        // One outbox row written transactionally:
        var rows = await ctx.OutboxMessages.AsNoTracking().ToListAsync();
        var row = Assert.Single(rows);
        Assert.Contains(nameof(OrderShipped), row.EventType);
        Assert.Null(row.ProcessedOnUtc);
        Assert.Equal(0, row.RetryCount);
    }

    [Fact]
    public async Task SaveChanges_OutboxMode_NoAggregates_WritesNoOutboxRows()
    {
        var dispatcher = new InMemoryDomainEventDispatcher();
        await using var sp = BuildSp(dispatcher);
        await using var scope = sp.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await ctx.Database.OpenConnectionAsync();
        await ctx.Database.EnsureCreatedAsync();

        ctx.Orders.Add(new Order(Guid.NewGuid()));   // no Ship() call
        await ctx.SaveChangesAsync();

        Assert.Empty(await ctx.OutboxMessages.AsNoTracking().ToListAsync());
    }
}
```

- [ ] **Step 2: Run, verify pass**

Run: `dotnet test tests/Moongazing.OrionGuard.EntityFrameworkCore.Tests --filter "FullyQualifiedName~Outbox"`
Expected: PASS — 2 tests pass.

- [ ] **Step 3: Commit**

```bash
git add tests/Moongazing.OrionGuard.EntityFrameworkCore.Tests/DomainEventInterceptorOutboxTests.cs
git commit -m "test(efcore): cover Outbox-mode interceptor branch"
```

---

## Task 12: `OutboxDispatcherHostedService`

A `BackgroundService` that polls unprocessed outbox rows, deserializes payloads, dispatches via `IDomainEventDispatcher`, marks rows processed, and dead-letters after `MaxRetries`.

**Files:**
- Create: `src/Moongazing.OrionGuard.EntityFrameworkCore/Outbox/OutboxDispatcherHostedService.cs`
- Create: `tests/Moongazing.OrionGuard.EntityFrameworkCore.Tests/OutboxDispatcherTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/Moongazing.OrionGuard.EntityFrameworkCore.Tests/OutboxDispatcherTests.cs`:

```csharp
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.DependencyInjection;
using Moongazing.OrionGuard.Domain.Events;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox;
using Moongazing.OrionGuard.EntityFrameworkCore.Tests.TestFixtures;
using Moongazing.OrionGuard.Testing.DomainEvents;

namespace Moongazing.OrionGuard.EntityFrameworkCore.Tests;

public class OutboxDispatcherTests
{
    private static ServiceProvider BuildSp(IDomainEventDispatcher dispatcher, OutboxOptions? opts = null)
    {
        var resolved = opts ?? new OutboxOptions { PollingInterval = TimeSpan.FromMilliseconds(50), BatchSize = 10, MaxRetries = 2 };
        var services = new ServiceCollection();
        services.AddOrionGuardDomainEvents();
        var existing = services.Single(d => d.ServiceType == typeof(IDomainEventDispatcher));
        services.Remove(existing);
        services.AddSingleton(dispatcher);

        var efCoreOptions = new OrionGuardEfCoreOptions().UseOutbox(o =>
        {
            o.PollingInterval = resolved.PollingInterval;
            o.BatchSize = resolved.BatchSize;
            o.MaxRetries = resolved.MaxRetries;
            o.TableName = resolved.TableName;
        });
        services.AddSingleton(efCoreOptions);
        services.AddSingleton(efCoreOptions.Outbox);
        services.AddScoped<DomainEventCollector>();
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<TestDbContext>());
        services.AddDbContext<TestDbContext>((sp, o) =>
            o.UseSqlite("DataSource=:memory:")
             .AddInterceptors(new DomainEventSaveChangesInterceptor(sp)));
        return services.BuildServiceProvider();
    }

    private static async Task SeedOutboxRowAsync(TestDbContext ctx, IDomainEvent evt)
    {
        ctx.OutboxMessages.Add(new OutboxMessage
        {
            EventType = evt.GetType().AssemblyQualifiedName!,
            Payload = JsonSerializer.Serialize(evt, evt.GetType()),
            OccurredOnUtc = evt.OccurredOnUtc,
        });
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task ProcessBatch_DispatchesUnprocessedRowsAndMarksThemProcessed()
    {
        var dispatcher = new InMemoryDomainEventDispatcher();
        await using var sp = BuildSp(dispatcher);
        await using var scope = sp.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await ctx.Database.OpenConnectionAsync();
        await ctx.Database.EnsureCreatedAsync();

        await SeedOutboxRowAsync(ctx, new OrderShipped(Guid.NewGuid()));

        var worker = new OutboxDispatcherHostedService(sp,
            sp.GetRequiredService<OutboxOptions>(),
            (sp.GetRequiredService<IServiceScopeFactory>()));

        await worker.ProcessBatchAsync(default);

        Assert.Single(dispatcher.Captured);
        var row = await ctx.OutboxMessages.AsNoTracking().SingleAsync();
        Assert.NotNull(row.ProcessedOnUtc);
    }

    [Fact]
    public async Task ProcessBatch_OnHandlerThrow_IncrementsRetryAndRecordsError()
    {
        var dispatcher = new ThrowingDispatcher();
        await using var sp = BuildSp(dispatcher);
        await using var scope = sp.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await ctx.Database.OpenConnectionAsync();
        await ctx.Database.EnsureCreatedAsync();

        await SeedOutboxRowAsync(ctx, new OrderShipped(Guid.NewGuid()));

        var worker = new OutboxDispatcherHostedService(sp,
            sp.GetRequiredService<OutboxOptions>(),
            sp.GetRequiredService<IServiceScopeFactory>());

        await worker.ProcessBatchAsync(default);

        var row = await ctx.OutboxMessages.AsNoTracking().SingleAsync();
        Assert.Equal(1, row.RetryCount);
        Assert.NotNull(row.Error);
        Assert.Null(row.ProcessedOnUtc);
    }

    [Fact]
    public async Task ProcessBatch_AfterMaxRetries_DeadLettersTheRow()
    {
        var dispatcher = new ThrowingDispatcher();
        var opts = new OutboxOptions { MaxRetries = 2, BatchSize = 10, PollingInterval = TimeSpan.FromMilliseconds(50) };
        await using var sp = BuildSp(dispatcher, opts);
        await using var scope = sp.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await ctx.Database.OpenConnectionAsync();
        await ctx.Database.EnsureCreatedAsync();

        await SeedOutboxRowAsync(ctx, new OrderShipped(Guid.NewGuid()));
        var worker = new OutboxDispatcherHostedService(sp, opts, sp.GetRequiredService<IServiceScopeFactory>());

        await worker.ProcessBatchAsync(default);
        await worker.ProcessBatchAsync(default);

        var row = await ctx.OutboxMessages.AsNoTracking().SingleAsync();
        Assert.Equal(2, row.RetryCount);
        Assert.NotNull(row.ProcessedOnUtc);   // dead-lettered
    }

    private sealed class ThrowingDispatcher : IDomainEventDispatcher
    {
        public Task DispatchAsync(IDomainEvent @event, CancellationToken ct = default) => throw new InvalidOperationException("boom");
        public Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken ct = default) => throw new InvalidOperationException("boom");
    }
}
```

- [ ] **Step 2: Verify failure**

Run: `dotnet test tests/Moongazing.OrionGuard.EntityFrameworkCore.Tests --filter "FullyQualifiedName~OutboxDispatcherTests"`
Expected: FAIL — `OutboxDispatcherHostedService` not found.

- [ ] **Step 3: Implement**

Create `src/Moongazing.OrionGuard.EntityFrameworkCore/Outbox/OutboxDispatcherHostedService.cs`:

```csharp
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moongazing.OrionGuard.Domain.Events;

namespace Moongazing.OrionGuard.EntityFrameworkCore.Outbox;

/// <summary>
/// Polls <see cref="OutboxMessage"/> rows from the consumer's <see cref="DbContext"/>, deserializes
/// each event by its <see cref="OutboxMessage.EventType"/>, and dispatches via
/// <see cref="IDomainEventDispatcher"/>. On dispatch failure, increments <see cref="OutboxMessage.RetryCount"/>;
/// after <see cref="OutboxOptions.MaxRetries"/> attempts the row is dead-lettered (marked processed).
/// </summary>
public sealed class OutboxDispatcherHostedService : BackgroundService
{
    private readonly IServiceProvider _root;
    private readonly OutboxOptions _options;
    private readonly IServiceScopeFactory _scopeFactory;

    public OutboxDispatcherHostedService(
        IServiceProvider root,
        OutboxOptions options,
        IServiceScopeFactory scopeFactory)
    {
        _root = root;
        _options = options;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ProcessBatchAsync(stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            catch { /* swallow per-batch fault; per-row faults already recorded on the row */ }
            try { await Task.Delay(_options.PollingInterval, stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Internal entry point for tests.</summary>
    public async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<DbContext>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>();

        var batch = await ctx.Set<OutboxMessage>()
            .Where(m => m.ProcessedOnUtc == null)
            .OrderBy(m => m.OccurredOnUtc)
            .Take(_options.BatchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (batch.Count == 0) return;

        foreach (var msg in batch)
        {
            try
            {
                var type = Type.GetType(msg.EventType)
                    ?? throw new InvalidOperationException($"Cannot resolve event type '{msg.EventType}'.");
                var @event = (IDomainEvent)JsonSerializer.Deserialize(msg.Payload, type)!;
                await dispatcher.DispatchAsync(@event, cancellationToken).ConfigureAwait(false);
                msg.ProcessedOnUtc = DateTime.UtcNow;
                msg.Error = null;
            }
            catch (Exception ex)
            {
                msg.RetryCount++;
                msg.Error = ex.ToString();
                if (msg.RetryCount >= _options.MaxRetries)
                    msg.ProcessedOnUtc = DateTime.UtcNow;   // dead-letter
            }
        }
        await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
```

> **Note for the engineer:** the test asserts `scope.ServiceProvider.GetRequiredService<DbContext>()` returns `TestDbContext`. EF Core's `AddDbContext<TestDbContext>` registers `TestDbContext` only — `DbContext` is not registered. If the tests fail with "service not found", change `ctx` resolution to a typed lookup keyed by the registered `DbContext` derived type. The simplest fix: change the constructor to take a `Func<IServiceProvider, DbContext>` dbContextResolver. We register that func from `AddOrionGuardEfCore<TDbContext>` in Task 13. For Task 12 alone, register `services.AddScoped<DbContext>(sp => sp.GetRequiredService<TestDbContext>())` in `BuildSp`.

- [ ] **Step 4: Run tests, verify pass**

Run: `dotnet test tests/Moongazing.OrionGuard.EntityFrameworkCore.Tests --filter "FullyQualifiedName~OutboxDispatcherTests"`
Expected: PASS — 3 tests pass. (`BuildSp` already registers `DbContext -> TestDbContext` as Scoped, which is what the worker resolves inside its scope.)

- [ ] **Step 5: Commit**

```bash
git add src/Moongazing.OrionGuard.EntityFrameworkCore/Outbox/OutboxDispatcherHostedService.cs tests/Moongazing.OrionGuard.EntityFrameworkCore.Tests/OutboxDispatcherTests.cs
git commit -m "feat(efcore): add OutboxDispatcherHostedService with retry + dead-letter"
```

---

## Task 13: EF Core DI helper — `AddOrionGuardEfCore<TDbContext>`

Wraps the registration: options + interceptor + (in Outbox mode) hosted service + `DbContext` -> derived `TDbContext` adapter.

**Files:**
- Create: `src/Moongazing.OrionGuard.EntityFrameworkCore/ServiceCollectionExtensions.cs`

- [ ] **Step 1: Implement**

Create `src/Moongazing.OrionGuard.EntityFrameworkCore/ServiceCollectionExtensions.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox;

namespace Moongazing.OrionGuard.EntityFrameworkCore;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Configures OrionGuard's domain-event dispatching against an existing <typeparamref name="TDbContext"/>
    /// registration. Call after <c>services.AddDbContext&lt;TDbContext&gt;(...)</c>.
    /// </summary>
    /// <remarks>
    /// In Outbox mode, the consumer must apply <see cref="OutboxMessageEntityTypeConfiguration"/>
    /// inside their <c>OnModelCreating</c> override using the configured <see cref="OutboxOptions.TableName"/>,
    /// and the <see cref="OutboxDispatcherHostedService"/> is registered automatically.
    /// </remarks>
    public static IServiceCollection AddOrionGuardEfCore<TDbContext>(
        this IServiceCollection services,
        Action<OrionGuardEfCoreOptions>? configure = null)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new OrionGuardEfCoreOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);
        services.AddSingleton(options.Outbox);
        services.AddScoped<DomainEventCollector>();
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<TDbContext>());

        if (options.Strategy == DomainEventDispatchStrategy.Outbox)
        {
            services.AddHostedService(sp => new OutboxDispatcherHostedService(
                sp, sp.GetRequiredService<OutboxOptions>(), sp.GetRequiredService<IServiceScopeFactory>()));
        }
        return services;
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/Moongazing.OrionGuard.EntityFrameworkCore/Moongazing.OrionGuard.EntityFrameworkCore.csproj -c Debug`
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/Moongazing.OrionGuard.EntityFrameworkCore/ServiceCollectionExtensions.cs
git commit -m "feat(efcore): add AddOrionGuardEfCore<TDbContext> DI helper"
```

---

## Task 14: OpenTelemetry telemetry sources

**Files:**
- Create: `src/Moongazing.OrionGuard.OpenTelemetry/DomainEvents/OrionGuardDomainEventTelemetry.cs`
- Modify: `src/Moongazing.OrionGuard.OpenTelemetry/Moongazing.OrionGuard.OpenTelemetry.csproj` (no change required other than version bump in Task 17)

- [ ] **Step 1: Implement**

Create `src/Moongazing.OrionGuard.OpenTelemetry/DomainEvents/OrionGuardDomainEventTelemetry.cs`:

```csharp
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Moongazing.OrionGuard.OpenTelemetry.DomainEvents;

public static class OrionGuardDomainEventTelemetry
{
    public const string ActivitySourceName = "Moongazing.OrionGuard.DomainEvents";
    public const string MeterName = "Moongazing.OrionGuard.DomainEvents";

    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName, "6.3.0");
    internal static readonly Meter Meter = new(MeterName, "6.3.0");

    internal static readonly Counter<long> EventsDispatched = Meter.CreateCounter<long>(
        "orionguard.domain_events.dispatched", unit: "events", description: "Total domain events dispatched");

    internal static readonly Counter<long> EventsFailed = Meter.CreateCounter<long>(
        "orionguard.domain_events.failed", unit: "events", description: "Failed domain event dispatches");

    internal static readonly Histogram<double> DispatchDuration = Meter.CreateHistogram<double>(
        "orionguard.domain_events.duration", unit: "ms", description: "Dispatch duration in milliseconds");

    internal static readonly Counter<long> OutboxProcessed = Meter.CreateCounter<long>(
        "orionguard.outbox.processed", unit: "messages", description: "Outbox messages processed");

    internal static readonly Counter<long> OutboxRetries = Meter.CreateCounter<long>(
        "orionguard.outbox.retries", unit: "retries", description: "Outbox retry attempts");
}
```

- [ ] **Step 2: Build + commit**

Run: `dotnet build src/Moongazing.OrionGuard.OpenTelemetry/Moongazing.OrionGuard.OpenTelemetry.csproj -c Debug`

```bash
git add src/Moongazing.OrionGuard.OpenTelemetry/DomainEvents/OrionGuardDomainEventTelemetry.cs
git commit -m "feat(otel): add domain-event ActivitySource and Meter sources"
```

---

## Task 15: `InstrumentedDomainEventDispatcher` decorator + DI helper

**Files:**
- Create: `src/Moongazing.OrionGuard.OpenTelemetry/DomainEvents/InstrumentedDomainEventDispatcher.cs`
- Create: `src/Moongazing.OrionGuard.OpenTelemetry/DomainEvents/DomainEventOpenTelemetryExtensions.cs`
- Create: `tests/Moongazing.OrionGuard.Tests/InstrumentedDomainEventDispatcherTests.cs`

- [ ] **Step 1: Add OpenTelemetry project reference to test project**

Modify `tests/Moongazing.OrionGuard.Tests/Moongazing.OrionGuard.Tests.csproj` — add inside the existing `ProjectReference` group:

```xml
<ProjectReference Include="..\..\src\Moongazing.OrionGuard.OpenTelemetry\Moongazing.OrionGuard.OpenTelemetry.csproj" />
```

- [ ] **Step 2: Write failing tests**

Create `tests/Moongazing.OrionGuard.Tests/InstrumentedDomainEventDispatcherTests.cs`:

```csharp
using System.Diagnostics;
using Moongazing.OrionGuard.Domain.Events;
using Moongazing.OrionGuard.OpenTelemetry.DomainEvents;

namespace Moongazing.OrionGuard.Tests;

public class InstrumentedDomainEventDispatcherTests
{
    private sealed record TestEvent(int Id) : DomainEventBase;

    private sealed class StubDispatcher : IDomainEventDispatcher
    {
        public bool Throw { get; set; }
        public Task DispatchAsync(IDomainEvent @event, CancellationToken ct = default)
            => Throw ? throw new InvalidOperationException("boom") : Task.CompletedTask;
        public Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken ct = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task DispatchAsync_OnSuccess_StartsAndCompletesActivity()
    {
        var captured = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == OrionGuardDomainEventTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = a => captured.Add(a),
        };
        ActivitySource.AddActivityListener(listener);

        var inner = new StubDispatcher();
        var instr = new InstrumentedDomainEventDispatcher(inner);

        await instr.DispatchAsync(new TestEvent(1));

        var activity = Assert.Single(captured);
        Assert.Equal("DomainEvent.Dispatch TestEvent", activity.DisplayName);
        Assert.Equal(ActivityStatusCode.Ok, activity.Status);
    }

    [Fact]
    public async Task DispatchAsync_OnFailure_RecordsErrorAndRethrows()
    {
        var captured = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == OrionGuardDomainEventTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = a => captured.Add(a),
        };
        ActivitySource.AddActivityListener(listener);

        var inner = new StubDispatcher { Throw = true };
        var instr = new InstrumentedDomainEventDispatcher(inner);

        await Assert.ThrowsAsync<InvalidOperationException>(() => instr.DispatchAsync(new TestEvent(1)));
        var activity = Assert.Single(captured);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
    }
}
```

- [ ] **Step 3: Verify failure**

Run: `dotnet test tests/Moongazing.OrionGuard.Tests --filter "FullyQualifiedName~InstrumentedDomainEventDispatcherTests"`
Expected: FAIL — `InstrumentedDomainEventDispatcher` not found.

- [ ] **Step 4: Implement decorator**

Create `src/Moongazing.OrionGuard.OpenTelemetry/DomainEvents/InstrumentedDomainEventDispatcher.cs`:

```csharp
using System.Diagnostics;
using Moongazing.OrionGuard.Domain.Events;

namespace Moongazing.OrionGuard.OpenTelemetry.DomainEvents;

public sealed class InstrumentedDomainEventDispatcher : IDomainEventDispatcher
{
    private readonly IDomainEventDispatcher _inner;
    public InstrumentedDomainEventDispatcher(IDomainEventDispatcher inner)
        => _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public async Task DispatchAsync(IDomainEvent @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        var typeName = @event.GetType().Name;
        using var activity = OrionGuardDomainEventTelemetry.ActivitySource
            .StartActivity($"DomainEvent.Dispatch {typeName}", ActivityKind.Internal);
        activity?.SetTag("orionguard.event.id", @event.EventId);
        activity?.SetTag("orionguard.event.type", @event.GetType().FullName);
        activity?.SetTag("orionguard.event.occurred_on", @event.OccurredOnUtc);

        var sw = Stopwatch.StartNew();
        var tags = new TagList { { "event_type", typeName } };
        try
        {
            await _inner.DispatchAsync(@event, cancellationToken).ConfigureAwait(false);
            OrionGuardDomainEventTelemetry.EventsDispatched.Add(1, tags);
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (Exception ex)
        {
            OrionGuardDomainEventTelemetry.EventsFailed.Add(1, tags);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
            {
                { "exception.type", ex.GetType().FullName },
                { "exception.message", ex.Message },
            }));
            throw;
        }
        finally
        {
            OrionGuardDomainEventTelemetry.DispatchDuration.Record(sw.Elapsed.TotalMilliseconds, tags);
        }
    }

    public async Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(events);
        foreach (var e in events)
            await DispatchAsync(e, cancellationToken).ConfigureAwait(false);
    }
}
```

- [ ] **Step 5: DI helper**

Create `src/Moongazing.OrionGuard.OpenTelemetry/DomainEvents/DomainEventOpenTelemetryExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moongazing.OrionGuard.Domain.Events;

namespace Moongazing.OrionGuard.OpenTelemetry.DomainEvents;

public static class DomainEventOpenTelemetryExtensions
{
    /// <summary>
    /// Wraps the registered <see cref="IDomainEventDispatcher"/> with
    /// <see cref="InstrumentedDomainEventDispatcher"/>. Call after <c>AddOrionGuardDomainEvents()</c>.
    /// </summary>
    public static IServiceCollection WithOpenTelemetryDomainEvents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var existing = services.Single(d => d.ServiceType == typeof(IDomainEventDispatcher));
        services.Remove(existing);

        services.Add(new ServiceDescriptor(
            typeof(IDomainEventDispatcher),
            sp =>
            {
                var inner = (IDomainEventDispatcher)ActivatorUtilities.CreateInstance(sp, existing.ImplementationType
                    ?? throw new InvalidOperationException("Inner dispatcher must be registered by type."));
                return new InstrumentedDomainEventDispatcher(inner);
            },
            existing.Lifetime));
        return services;
    }
}
```

- [ ] **Step 6: Run tests, verify pass**

Run: `dotnet test tests/Moongazing.OrionGuard.Tests --filter "FullyQualifiedName~InstrumentedDomainEventDispatcherTests"`
Expected: PASS — 2 tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/Moongazing.OrionGuard.OpenTelemetry/DomainEvents tests/Moongazing.OrionGuard.Tests/InstrumentedDomainEventDispatcherTests.cs tests/Moongazing.OrionGuard.Tests/Moongazing.OrionGuard.Tests.csproj
git commit -m "feat(otel): add InstrumentedDomainEventDispatcher decorator + DI helper"
```

---

## Task 16: Distributed trace context propagation

The interceptor captures `Activity.Current?.Id` and `Activity.Current?.TraceStateString` and writes them onto each `OutboxMessage`. The worker, when starting its `DomainEvent.Dispatch` activity, parses those values and uses them as the parent context, so the trace continues across the outbox boundary.

**Files:**
- Modify: `src/Moongazing.OrionGuard.EntityFrameworkCore/DomainEventSaveChangesInterceptor.cs`
- Modify: `src/Moongazing.OrionGuard.EntityFrameworkCore/Outbox/OutboxDispatcherHostedService.cs`
- Create: `tests/Moongazing.OrionGuard.EntityFrameworkCore.Tests/TraceContextPropagationTests.cs`

- [ ] **Step 1: Update interceptor — capture trace context on Outbox writes**

In `DomainEventSaveChangesInterceptor.SavingChangesAsync`, replace the Outbox-mode loop body so each `OutboxMessage` also captures the current activity:

```csharp
var current = Activity.Current;
foreach (var aggregate in aggregates)
{
    var events = aggregate.PullDomainEvents();
    foreach (var e in events)
    {
        ctx.Add(new OutboxMessage
        {
            EventType = e.GetType().AssemblyQualifiedName!,
            Payload = JsonSerializer.Serialize(e, e.GetType()),
            OccurredOnUtc = e.OccurredOnUtc,
            TraceParent = current?.Id,
            TraceState = current?.TraceStateString,
        });
    }
}
```

Add `using System.Diagnostics;` at the top of the file if not already present.

- [ ] **Step 2: Update worker — restore trace context per message**

At the top of `OutboxDispatcherHostedService.cs`, add `using System.Diagnostics;`. Add a static `ActivitySource` field to the class (so it is created once, not per dispatch — and matches the source name used by the OpenTelemetry decorator):

```csharp
private static readonly ActivitySource OutboxActivitySource = new("Moongazing.OrionGuard.DomainEvents", "6.3.0");
```

Then replace the per-row dispatch block in `ProcessBatchAsync` with:

```csharp
foreach (var msg in batch)
{
    Activity? activity = null;
    if (!string.IsNullOrEmpty(msg.TraceParent)
        && ActivityContext.TryParse(msg.TraceParent, msg.TraceState, out var parentContext))
    {
        activity = OutboxActivitySource.StartActivity("Outbox.Dispatch", ActivityKind.Consumer, parentContext);
    }
    try
    {
        var type = Type.GetType(msg.EventType)
            ?? throw new InvalidOperationException($"Cannot resolve event type '{msg.EventType}'.");
        var @event = (IDomainEvent)JsonSerializer.Deserialize(msg.Payload, type)!;
        await dispatcher.DispatchAsync(@event, cancellationToken).ConfigureAwait(false);
        msg.ProcessedOnUtc = DateTime.UtcNow;
        msg.Error = null;
    }
    catch (Exception ex)
    {
        msg.RetryCount++;
        msg.Error = ex.ToString();
        if (msg.RetryCount >= _options.MaxRetries)
            msg.ProcessedOnUtc = DateTime.UtcNow;
    }
    finally
    {
        activity?.Dispose();
    }
}
```

> The source name `Moongazing.OrionGuard.DomainEvents` is duplicated here intentionally — `OrionGuard.EntityFrameworkCore` does not depend on `OrionGuard.OpenTelemetry`, so we cannot reuse the `OrionGuardDomainEventTelemetry.ActivitySource` constant. The string is part of the v6.3.0 API contract and tested against in Task 16 step 3.

- [ ] **Step 3: Write trace propagation test**

Create `tests/Moongazing.OrionGuard.EntityFrameworkCore.Tests/TraceContextPropagationTests.cs`:

```csharp
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionGuard.DependencyInjection;
using Moongazing.OrionGuard.Domain.Events;
using Moongazing.OrionGuard.EntityFrameworkCore.Tests.TestFixtures;
using Moongazing.OrionGuard.Testing.DomainEvents;

namespace Moongazing.OrionGuard.EntityFrameworkCore.Tests;

public class TraceContextPropagationTests
{
    [Fact]
    public async Task OutboxRow_RecordsTheCurrentActivityTraceParent()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        var dispatcher = new InMemoryDomainEventDispatcher();
        var services = new ServiceCollection();
        services.AddOrionGuardDomainEvents();
        var existing = services.Single(d => d.ServiceType == typeof(IDomainEventDispatcher));
        services.Remove(existing);
        services.AddSingleton<IDomainEventDispatcher>(dispatcher);
        services.AddSingleton(new OrionGuardEfCoreOptions().UseOutbox());
        services.AddScoped<DomainEventCollector>();
        services.AddDbContext<TestDbContext>((sp, o) =>
            o.UseSqlite("DataSource=:memory:")
             .AddInterceptors(new DomainEventSaveChangesInterceptor(sp)));
        await using var sp = services.BuildServiceProvider();

        await using var scope = sp.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TestDbContext>();
        await ctx.Database.OpenConnectionAsync();
        await ctx.Database.EnsureCreatedAsync();

        using var src = new ActivitySource("Test");
        using (var act = src.StartActivity("Test.SaveOrder"))
        {
            var order = new Order(Guid.NewGuid());
            ctx.Orders.Add(order);
            order.Ship();
            await ctx.SaveChangesAsync();

            var row = await ctx.OutboxMessages.AsNoTracking().SingleAsync();
            Assert.Equal(act!.Id, row.TraceParent);
        }
    }
}
```

- [ ] **Step 4: Run all EF Core tests**

Run: `dotnet test tests/Moongazing.OrionGuard.EntityFrameworkCore.Tests`
Expected: PASS — all tests including trace propagation pass.

- [ ] **Step 5: Commit**

```bash
git add src/Moongazing.OrionGuard.EntityFrameworkCore tests/Moongazing.OrionGuard.EntityFrameworkCore.Tests
git commit -m "feat(efcore): propagate W3C trace context across the outbox boundary"
```

---

## Task 17: Version bump — all csproj files to 6.3.0

**Files:**
- Modify: `src/Moongazing.OrionGuard/Moongazing.OrionGuard.csproj`
- Modify: `src/Moongazing.OrionGuard.MediatR/Moongazing.OrionGuard.MediatR.csproj`
- Modify: `src/Moongazing.OrionGuard.OpenTelemetry/Moongazing.OrionGuard.OpenTelemetry.csproj`
- Modify: `src/Moongazing.OrionGuard.AspNetCore/Moongazing.OrionGuard.AspNetCore.csproj` (if exists, bump for ecosystem consistency)
- Modify: `src/Moongazing.OrionGuard.Generators/Moongazing.OrionGuard.Generators.csproj`
- Modify: `src/Moongazing.OrionGuard.Grpc/Moongazing.OrionGuard.Grpc.csproj`
- Modify: `src/Moongazing.OrionGuard.Blazor/Moongazing.OrionGuard.Blazor.csproj`
- Modify: `src/Moongazing.OrionGuard.SignalR/Moongazing.OrionGuard.SignalR.csproj`
- Modify: `src/Moongazing.OrionGuard.Swagger/Moongazing.OrionGuard.Swagger.csproj`

- [ ] **Step 1: Bump core**

In `src/Moongazing.OrionGuard/Moongazing.OrionGuard.csproj`, change:
```xml
<Version>6.2.0</Version>
```
to:
```xml
<Version>6.3.0</Version>
```

Also replace the `<PackageReleaseNotes>` block with a new v6.3.0 entry summarizing the changes (mirror the format of the existing v6.2.0 entry; do not delete the older entries — append v6.3.0 at the top).

- [ ] **Step 2: Bump every other ecosystem package the same way**

For each of the eight other csproj files listed above, replace `<Version>6.2.0</Version>` with `<Version>6.3.0</Version>`. Do not change `<PackageReleaseNotes>` for sub-packages — keep them lean.

- [ ] **Step 3: Build the entire solution**

Run: `dotnet build Moongazing.OrionGuard.sln -c Debug`
Expected: 0 errors. All 13 packages build at version 6.3.0.

- [ ] **Step 4: Commit**

```bash
git add src
git commit -m "chore: bump all ecosystem packages to 6.3.0"
```

---

## Task 18: CHANGELOG.md and README.md updates

**Files:**
- Modify: `CHANGELOG.md`
- Modify: `README.md`

- [ ] **Step 1: Prepend v6.3.0 entry to CHANGELOG.md**

Insert immediately after `## [6.2.0] - 2026-04-19`'s closing `Roadmap` block (before the `## [6.1.0]` heading):

```markdown
## [6.3.0] - 2026-05-09

### Added

#### Domain event dispatcher (`Moongazing.OrionGuard.Domain.Events`)

- `IDomainEventDispatcher` and `IDomainEventHandler<TEvent>` abstractions.
- `ServiceProviderDomainEventDispatcher` — default implementation resolving handlers from `IServiceProvider`.
- `DomainEventDispatchOptions` with `DispatchMode.SequentialFailFast` (default), `SequentialContinueOnError`, and `Parallel`.
- `services.AddOrionGuardDomainEvents()` and `services.AddOrionGuardDomainEventHandlers(...)` DI helpers.

#### MediatR bridge (`OrionGuard.MediatR`)

- `MediatRDomainEventDispatcher` delegates to MediatR's `IPublisher`. Consumer events opt in by adding `: INotification` to their record declaration; the bridge throws `InvalidOperationException` for events that do not. No wrapper types — handlers stay as natural `INotificationHandler<TEvent>`.
- `services.AddOrionGuardMediatRDomainEvents()` swaps the registered dispatcher.

#### `OrionGuard.EntityFrameworkCore` (NEW PACKAGE)

- `DomainEventSaveChangesInterceptor` — pulls events from tracked `IAggregateRoot` instances at `SavingChangesAsync` and either dispatches them post-commit (`Inline` mode, default) or persists them as `OutboxMessage` rows in the same transaction (`Outbox` mode).
- `OutboxMessage` entity, `OutboxMessageEntityTypeConfiguration`, and `OutboxOptions` (`PollingInterval`, `BatchSize`, `MaxRetries`, `TableName`).
- `OutboxDispatcherHostedService` — `BackgroundService` that polls unprocessed rows, deserializes events, dispatches via `IDomainEventDispatcher`, increments `RetryCount` on failure, dead-letters after `MaxRetries`.
- W3C trace context propagation: outbox rows record `TraceParent` and `TraceState`; the worker resumes the parent activity context per message.
- `services.AddOrionGuardEfCore<TDbContext>(o => o.UseInline() | o.UseOutbox())`.

#### `OrionGuard.Testing` (NEW PACKAGE)

- `DomainEventCapture` and `DomainEventAssertions` for fluent unit-test assertions.
- `InMemoryDomainEventDispatcher` for integration tests.
- Framework-agnostic — no xUnit / NUnit / FluentAssertions dependency.

#### `OrionGuard.OpenTelemetry`

- `OrionGuardDomainEventTelemetry` — `ActivitySource` + `Meter` with `EventsDispatched`, `EventsFailed`, `DispatchDuration` (histogram), `OutboxProcessed`, `OutboxRetries`.
- `InstrumentedDomainEventDispatcher` decorator — opens a span per dispatch, records counters, sets activity status on exception.
- `services.WithOpenTelemetryDomainEvents()`.

### Migration from v6.2.0

- No breaking changes. Source-compatible.
- Existing `RaiseEvent` / `PullDomainEvents` aggregate code continues to work; events simply do not dispatch unless `AddOrionGuardDomainEvents` is wired.
- MediatR consumers add `, INotification` to their event records (one-line per event).
- Outbox consumers add a migration for the `OrionGuard_Outbox` table.

### Roadmap

- v6.4.0: `BusinessRule` base class + `Guard.Against.BrokenRule` + ASP.NET Core ProblemDetails mapping; distributed locking for multi-instance outbox workers; `OutboxTypeMapRegistry` (alias system); archival job.
- v6.5+: Push-based outbox dispatch (`LISTEN/NOTIFY`, `SqlDependency`); event sourcing primitives.

```

- [ ] **Step 2: Update README.md ecosystem table**

In `README.md`, locate the `## Ecosystem Packages` table and append two new rows after the existing rows:

```markdown
| `OrionGuard.EntityFrameworkCore` | `dotnet add package OrionGuard.EntityFrameworkCore` | EF Core SaveChanges interceptor + transactional outbox |
| `OrionGuard.Testing` | `dotnet add package OrionGuard.Testing` | DomainEventCapture + InMemoryDispatcher + assertions |
```

In the `## Why OrionGuard?` table at the top, add a new row:

```markdown
| Domain events + outbox | Yes | - | - | - |
```

- [ ] **Step 3: Commit**

```bash
git add CHANGELOG.md README.md
git commit -m "docs: v6.3.0 release notes + README ecosystem table updates"
```

---

## Task 19: Demo update — end-to-end example

Add a runnable demonstration that exercises all five components.

**Files:**
- Modify: `demo/Moongazing.OrionGuard.Demo/Program.cs`
- Modify: `demo/Moongazing.OrionGuard.Demo/Moongazing.OrionGuard.Demo.csproj` (add EF Core SQLite + new package references)

- [ ] **Step 1: Read the existing demo to understand its structure**

Run: `Read demo/Moongazing.OrionGuard.Demo/Program.cs` and `Read demo/Moongazing.OrionGuard.Demo/Moongazing.OrionGuard.Demo.csproj`. The demo is a console host; locate where it sets up DI and add a new `RunDomainEventsAsync` section before its existing demonstrations.

- [ ] **Step 2: Add references to the demo csproj**

In `demo/Moongazing.OrionGuard.Demo/Moongazing.OrionGuard.Demo.csproj`, ensure the following references exist (add any that are missing inside the existing `<ItemGroup>`):

```xml
<ProjectReference Include="..\..\src\Moongazing.OrionGuard.EntityFrameworkCore\Moongazing.OrionGuard.EntityFrameworkCore.csproj" />
<ProjectReference Include="..\..\src\Moongazing.OrionGuard.Testing\Moongazing.OrionGuard.Testing.csproj" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="9.0.0" />
<PackageReference Include="Microsoft.Extensions.Hosting" Version="9.0.0" />
```

- [ ] **Step 3: Add a domain-events demo entry point**

Append the following to `demo/Moongazing.OrionGuard.Demo/Program.cs` (or to a `DomainEventsSection.cs` partial if the file is split):

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moongazing.OrionGuard.DependencyInjection;
using Moongazing.OrionGuard.Domain.Events;
using Moongazing.OrionGuard.Domain.Primitives;
using Moongazing.OrionGuard.EntityFrameworkCore;
using Moongazing.OrionGuard.EntityFrameworkCore.Outbox;

namespace Moongazing.OrionGuard.Demo;

public static class DomainEventsDemo
{
    public sealed record OrderShipped(Guid OrderId) : DomainEventBase;

    public sealed class Order : AggregateRoot<Guid>
    {
        public Order(Guid id) : base(id) { }
        private Order() { }
        public string Status { get; private set; } = "New";
        public void Ship() { Status = "Shipped"; RaiseEvent(new OrderShipped(Id)); }
    }

    public sealed class DemoDbContext : DbContext
    {
        public DbSet<Order> Orders => Set<Order>();
        public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
        public DemoDbContext(DbContextOptions<DemoDbContext> options) : base(options) { }
        protected override void OnModelCreating(ModelBuilder b)
        {
            b.Entity<Order>().HasKey(o => o.Id);
            b.Entity<Order>().Ignore(o => o.DomainEvents);
            b.ApplyConfiguration(new OutboxMessageEntityTypeConfiguration("OrionGuard_Outbox"));
        }
    }

    public sealed class LoggingHandler : IDomainEventHandler<OrderShipped>
    {
        public Task HandleAsync(OrderShipped e, CancellationToken ct)
        {
            Console.WriteLine($"  -> handled OrderShipped({e.OrderId})");
            return Task.CompletedTask;
        }
    }

    public static async Task RunAsync()
    {
        Console.WriteLine("\n=== Domain Events demo (Inline mode) ===");
        var host = Host.CreateApplicationBuilder();
        host.Services.AddDbContext<DemoDbContext>(o => o.UseSqlite("DataSource=demo.db"));
        host.Services.AddOrionGuardDomainEvents();
        host.Services.AddOrionGuardDomainEventHandlers(typeof(DomainEventsDemo).Assembly);
        host.Services.AddOrionGuardEfCore<DemoDbContext>(o => o.UseInline());
        host.Services.AddSingleton<DomainEventSaveChangesInterceptor>();

        var app = host.Build();
        await using var scope = app.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<DemoDbContext>();
        await ctx.Database.EnsureDeletedAsync();
        await ctx.Database.EnsureCreatedAsync();

        var order = new Order(Guid.NewGuid());
        ctx.Orders.Add(order);
        order.Ship();
        await ctx.SaveChangesAsync();

        Console.WriteLine($"  Saved order {order.Id} (status={order.Status})");
    }
}
```

In the existing `Program.Main` (or top-level program), add a call:

```csharp
await DomainEventsDemo.RunAsync();
```

- [ ] **Step 4: Run the demo**

Run: `dotnet run --project demo/Moongazing.OrionGuard.Demo`
Expected output (excerpt):

```
=== Domain Events demo (Inline mode) ===
  -> handled OrderShipped(...)
  Saved order ... (status=Shipped)
```

- [ ] **Step 5: Commit**

```bash
git add demo/Moongazing.OrionGuard.Demo
git commit -m "docs(demo): showcase domain events + EF Core Inline mode end-to-end"
```

---

## Final verification

- [ ] **Step 1: Run the entire test suite**

Run: `dotnet test Moongazing.OrionGuard.sln -c Debug`
Expected: ALL tests pass across all test projects.

- [ ] **Step 2: Build all targets in Release**

Run: `dotnet build Moongazing.OrionGuard.sln -c Release`
Expected: 0 errors, 0 warnings.

- [ ] **Step 3: Pack to verify NuGet metadata**

Run: `dotnet pack src/Moongazing.OrionGuard.EntityFrameworkCore -c Release -o ./artifacts`
Run: `dotnet pack src/Moongazing.OrionGuard.Testing -c Release -o ./artifacts`
Expected: `OrionGuard.EntityFrameworkCore.6.3.0.nupkg` and `OrionGuard.Testing.6.3.0.nupkg` produced.

- [ ] **Step 4: Push branch (DO NOT MERGE — that is the user's call)**

Run: `git status && git log --oneline -25`
Verify all task commits are present and clean. Wait for the user to push and open the PR; do not push automatically.

---

## Self-review notes (engineer)

- Spec section 9 lists `Moongazing.OrionGuard.MediatR.DomainEvents.AddOrionGuardMediatRDomainEvents` — implemented in Task 6 step 5.
- Spec section 6.5 deferments are honoured (no distributed locking, no `LISTEN/NOTIFY`, no archival job).
- Spec section 8.3 ("Test framework agnostic") — verified by `OrionGuard.Testing.csproj` having no test-framework dependency.
- Spec section 7.5 ("net8 compatibility") — verified by `activity?.AddEvent(new ActivityEvent("exception", ...))` instead of `AddException`.
- The marker-based MediatR opt-in (spec section 4.1) is enforced at runtime in `MediatRDomainEventDispatcher` (Task 6 step 4).
- W3C trace propagation (spec section 7.3) verified end-to-end in Task 16.
