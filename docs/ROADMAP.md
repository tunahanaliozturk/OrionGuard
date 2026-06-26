# OrionGuard Public Roadmap

> Where OrionGuard is going, and how you can help shape it.

This document is the authoritative roadmap for OrionGuard. It captures the vision,
lists every feature under active consideration, and invites the community to
propose, vote on, and contribute to what ships next.

---

## Vision

OrionGuard's goal is to be **the default validation layer for modern .NET
applications** -- the library teams reach for without thinking. To get there we need to
do three things exceptionally well:

1. **Close the gap with FluentValidation.** Every feature a senior engineer expects
   from a mature validator must be present and at least as ergonomic.
2. **Go further.** Security guards, file upload verification, PII detection,
   configuration guards, delta validation, parallel async rules -- capabilities
   the incumbents simply do not have.
3. **Feel like a platform, not a library.** Source generators, Roslyn analyzers,
   CLI tooling, IDE extensions, observability integrations. Validation is infrastructure;
   OrionGuard should treat it that way.

This roadmap is the plan for getting there. It is living: items move between tiers as
priorities shift, new ideas land, and community feedback arrives.

---

## How to Read This

Every entry has:

- **ID** (e.g. `R1`, `R17`) stable across document revisions.
- **Status** -- `[Shipped]`, `[In Progress]`, `[Planned]`, `[Research]`, `[Deferred]`.
- **Effort** -- `S` (under a day), `M` (1-3 days), `L` (a week or more), `XL` (multi-week).
- **Target** -- tentative release version.

Priority tiers roughly map to timing:

| Tier | Theme | Timing |
|------|-------|--------|
| Tier 0 | Shipped (reference) | Done |
| Tier 1 | Adoption & Growth | v6.7 |
| Tier 2 | Production Excellence | v6.7 - v6.8 |
| Tier 3 | Differentiation & Innovation | v6.8 - v7.0 |
| Tier 4 | Ecosystem & Integrations | Rolling |
| Tier 5 | Developer Experience | Rolling |
| Research | Exploratory | Unscheduled |

---

### Current version map

- **v6.1.0** — DDD tactical primitives (ValueObject, Entity, AggregateRoot, StronglyTypedId base + generator), guard extension, DI helper, 14-language localization keys.
- **v6.2.0** — API polish: `IStronglyTypedId<TValue>` unification, `DomainEventBase`, `IParsable`/`ISpanParsable` on generated ids, conditional EF Core converter emission, sub-package NuGet ID rename (drop `Moongazing.` prefix).
- **v6.3.0** — Domain event dispatcher, MediatR bridge, EF Core `SaveChanges` interceptor.
- **v6.4.0** — Full `BusinessRule` base class, `Guard.Against.BrokenRule`, ASP.NET Core ProblemDetails mapping; in-process `IDistributedLock` primitive (in-memory + EF Core `SKIP LOCKED`); audit-trail copy-before-delete; outbox type-map registry; archive-only outbox post-publish behaviour.
- **v6.4.1** — Logo refresh shipped 2026-05-23 (minimalist family-style indigo shield, no code changes).
- **v6.4.2** — Logo cream-bg variant for dark-mode README/NuGet card contrast (no code changes).
- **v6.5.0** — Shipped 2026-06-01. Redis-backed `IDistributedLock` bridge (`OrionGuard.Locks.Redis`) over the standalone [OrionLock](https://github.com/tunahanaliozturk/OrionLock) Redis backend. Push-based outbox dispatcher and outbox dead-letter UI were de-scoped from this minor and deferred to v6.5.1 and v6.5.2 respectively.
- **v6.5.1 – v6.5.30** — Shipped 2026-06-04 through 2026-06-18. Push-dispatch contract (`IOutboxWakeSignal`) plus the Postgres `LISTEN/NOTIFY` (`OrionGuard.Outbox.PostgresNotify`) and SQL Server Service Broker (`OrionGuard.Outbox.SqlServerBroker`) push backends; the outbox operator dashboard (`OrionGuard.Outbox.Dashboard`) with read, replay, discard, cursor pagination, sort, and security headers; off-box archival sinks; and the outbox dispatcher / archival diagnostics meter suite. See CHANGELOG for the per-patch detail.
- **v6.6.0** — Shipped 2026-06-19. First-class asynchronous validation pipeline on `ObjectValidator<T>` (`Validate.For(...)` / `Validate.ForStrict(...)`): `MustAsync`, `WhenAsync`, and the `ToResultAsync` / `BuildAsync` / `ThrowIfInvalidAsync` terminals. Sync and async rules merge into one `GuardResult` with full `CancellationToken` flow and short-circuit parity; the async terminal is idempotent (each async rule runs once); the sync terminals throw rather than silently skip pending async rules. `NotNull<TProperty>` constraint relaxed to `class?`. Source- and binary-compatible.
- **v6.6.1** — Shipped 2026-06-20. Diagnostics meter versions self-derive from each assembly's `AssemblyInformationalVersion` instead of hardcoded literals, so a meter version can no longer drift from its package version.
- **v6.6.2** — Shipped 2026-06-20. Allocation-free digit/identity validators: the Turkish-id, Luhn (card / IMEI), ISBN-13, and EAN hot paths drop their per-call delegate / enumerator / `int[]` allocations for a single-pass `stackalloc` scan (about 11x faster, zero allocation on the Turkish-id path; ~2x on Luhn). `char.IsDigit` semantics and results unchanged.
- **v6.7.0** — Shipped 2026-06-23. Two add-on integrations landed together. (1) OpenAPI-first validation (R2): the `OrionGuard.OpenApi` source-generator package turns an OpenAPI 3 schema into an `IValidator<T>` at compile time, enforcing `type`, `required`, `nullable`, string length / `pattern` / `format`, numeric range (incl. exclusive bounds), `enum`, array `minItems` / `maxItems`, and intra-document `$ref`. The analyzer bundles its own JSON reader. YAML input and polymorphism / composition (`discriminator` / `oneOf` / `anyOf` / `allOf`) are deferred follow-ups, surfaced as diagnostics `OG1002` and `OG1006` respectively. (2) `OrionGuard.Hangfire`: a Hangfire `IClientFilter` that validates a background job's arguments at enqueue time using the same `IServiceProvider`-based validator resolution as the other integrations, rejecting an invalid enqueue with a structured `JobArgumentValidationException` at the call site instead of failing inside a worker; arguments with no registered validator pass through. The R1 FluentValidation codemod remains in progress for the wider v6.7 theme. Uniform family version bump across all packages.

---

## Next 12 Months — Where OrionGuard Is Going

This is the operational view of the roadmap: what we expect to ship in the next year, mapped
to concrete versions and quarterly windows. The deep tiered backlog below stays as the
inventory of everything under consideration; this section is the *commitment view* that
mirrors the public-facing roadmaps in the sibling [[orionaudit]] and [[orionlock]] packages.

Dates are targets, not commitments. If a milestone slips by more than four weeks, the delay
shows up here.

### v6.5.0 — Family Integration *(Shipped 2026-06-01)*

Theme: *let OrionGuard quietly hand off the workloads its siblings now do better.*

- **`StronglyTypedId` generator soft-deprecation.** Shipped in v6.4.2. The in-box generator
  emits `[Obsolete]` with a migration hint pointing at
  [OrionKey](https://github.com/tunahanaliozturk/OrionKey). No runtime break — generated ids
  continue to compile and run. Removal scheduled for v7.0.
- **`OrionGuard.Locks.Redis` bridge.** Shipped 2026-06-01. Implements the v6.4
  `IDistributedLock` primitive on top of the standalone
  [OrionLock](https://github.com/tunahanaliozturk/OrionLock) Redis backend. Consumers who
  already added OrionGuard for validation get distributed locking via one extra
  `UseOrionLockRedis(...)` call on the EF Core options.

### v6.5.1 — Push-Dispatch Contract *(shipped 2026-06-04)*

Ships the push-dispatch contract + in-process implementation. Concrete cross-process backends move to follow-up patches so this minor stays small and reviewable.

- **`IOutboxWakeSignal`** abstraction in `Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Push`.
- **`NullOutboxWakeSignal`** - polling-only default (byte-for-byte v6.5.0 behaviour).
- **`ChannelOutboxWakeSignal`** - in-process `Channel<bool>` implementation for single-process deployments and unit tests.
- Dispatcher honours the signal as the polling-loop wait, with the configured polling interval as an upper bound on wake latency.

### v6.5.2 — Postgres Push Backend *(shipped 2026-06-09)*

- **`Moongazing.OrionGuard.Outbox.PostgresNotify`** add-on package shipped. `PostgresNotifyOutboxWakeSignal` runs `LISTEN` on a dedicated Npgsql connection and signals the dispatcher via the in-process channel; reconnect loop with exponential back-off bounded by `MaxReconnectDelay`. `PostgresNotifyTriggerSql.Create / Drop` helpers emit the consumer-managed AFTER INSERT trigger; the package does not auto-install schema changes.

### v6.5.3 — SQL Server Push Backend *(shipped 2026-06-09)*

- **`Moongazing.OrionGuard.Outbox.SqlServerBroker`** add-on package shipped. `SqlServerBrokerOutboxWakeSignal` runs `WAITFOR (RECEIVE ... TIMEOUT)` on a dedicated `SqlConnection`; reconnect loop with exponential back-off bounded by `MaxReconnectDelay`. `SqlServerBrokerSetupSql.Create / Drop` helpers install the Service Broker message type, contract, queue, service, and AFTER INSERT trigger; identifier and literal escapes (including double-escape for the EXEC-nested SEND TO SERVICE literal) prevent malformed SQL when custom names contain `]` or `'`.

### v6.5.4 — Outbox Operator Read Surface *(shipped 2026-06-09)*

- **`Moongazing.OrionGuard.Outbox.Dashboard` add-on shipped.** `MapOutboxDashboard<TDbContext>` registers an authorized read-only endpoint group; `GET /_orion/outbox/failed?page=N&size=M` returns paginated failed-message metadata (payload deliberately excluded). Authorization required by default; pagination clamped to a configurable `MaxPageSize`; error text truncated to `ErrorTruncationLength`.

### v6.5.5 — Outbox Operator Mutation Surface *(shipped 2026-06-10)*

- `POST /_orion/outbox/{id:guid}/replay` clears `RetryCount`, `Error`, and `ProcessedOnUtc` so the next dispatcher pass re-attempts the row. 404 for unknown ids.
- `POST /_orion/outbox/{id:guid}/discard` stamps `ProcessedOnUtc = UtcNow` so the dispatcher skips the row; idempotent for already-processed rows.
- `OutboxDashboardOptions.OnMutation` audit hook (consumer-controlled storage shape; the dashboard never writes audit rows).
- `OutboxDashboardOptions.EnableMutations` (default `true`) - false leaves the dashboard strictly read-only.

### v6.6.0 — Asynchronous Validation *(shipped 2026-06-19)*

Theme: *let an I/O-bound rule join the same pipeline as the synchronous ones.*

- **Async pipeline on `ObjectValidator<T>`.** `Validate.For(...)` / `Validate.ForStrict(...)`
  gain `MustAsync` (property- and instance-scoped), `WhenAsync`, and the `ToResultAsync` /
  `BuildAsync` / `ThrowIfInvalidAsync` terminals. A rule that performs I/O (a database
  uniqueness check, a remote lookup) runs alongside the synchronous rules and merges into one
  `GuardResult`.
- **Short-circuit parity + cancellation.** A strict validator throws
  `AggregateValidationException` on the first failure including the first async failure, after
  which no further async rules run; a non-strict validator aggregates every failure. The
  `CancellationToken` is observed before and during each async rule and `OperationCanceledException`
  propagates rather than being recorded as a validation error.
- **Idempotent async terminal; no silent drops.** Awaiting the async terminal more than once
  returns the same errors and runs each async rule once. The synchronous terminals
  (`ToResult` / `Build` / `ThrowIfInvalid`) now throw `InvalidOperationException` when async
  rules are pending instead of discarding them.
- **`NotNull<TProperty>` constraint relaxed** from `class` to `class?` so validating a nullable
  reference property no longer requires suppressing nullability warnings. The whole release is
  source- and binary-compatible.

Two patches followed on the same milestone:

- **v6.6.1 (shipped 2026-06-20).** Diagnostics meter versions self-derive from each assembly's
  `AssemblyInformationalVersion`, retiring several stale hardcoded meter versions so a meter
  version can no longer drift from its package version.
- **v6.6.2 (shipped 2026-06-20).** Allocation-free digit/identity validators: the Turkish-id,
  Luhn (card / IMEI), ISBN-13, and EAN paths drop their per-call allocations for a single-pass
  `stackalloc` scan. About 11x faster and zero-allocation on the Turkish-id path, ~2x on Luhn,
  with `char.IsDigit` semantics and results preserved exactly.

### v6.7.0 — Migration & Contract-First *(shipped 2026-06-23; R1 codemod in progress)*

Theme: *make adoption a weekend, not a quarter.*

- **R1: FluentValidation migration codemod (`dotnet orionguard migrate`).** Reads a project,
  rewrites validators to OrionGuard equivalents, emits a diff report. Covers the 25 most
  common FluentValidation built-ins, rule sets, `ChildRules`, `When/Unless`, `SetValidator`
  and async rules. The v6.6.0 async pipeline gives the `MustAsync`-shaped target the codemod
  rewrites FluentValidation's async rules onto.
- **R2: OpenAPI-First validation.** *(Shipped 2026-06-23.)* `[OpenApiValidator("openapi.json", "#/...")]`
  on a partial class generates an `IValidator<T>` from the named schema at compile time, complementing
  the validator-to-OpenAPI direction shipped by `OrionGuard.Swagger`. Shipped as the `OrionGuard.OpenApi`
  source-generator package, which bundles its own JSON reader (no unbundled analyzer dependency) and
  enforces `type`, `required`, `nullable`, string length / `pattern` / `format`, numeric range (incl.
  exclusive bounds), `enum`, array `minItems` / `maxItems`, and intra-document `$ref`. YAML input
  (raises `OG1002`) and polymorphism / composition `discriminator` / `oneOf` / `anyOf` / `allOf`
  (raise `OG1006`) are deferred follow-ups rather than half-implemented.
- **`OrionGuard.Hangfire` integration.** *(Shipped 2026-06-23.)* Validates a background job's
  arguments at enqueue time via a Hangfire `IClientFilter`: each argument is checked against its
  registered OrionGuard validator using the same `IServiceProvider`-based resolution as the other
  integrations, and a rejected enqueue raises a structured `JobArgumentValidationException` at the
  call site instead of being persisted and failing inside a worker. Arguments with no registered
  validator pass through. Worker-side (dequeue-time) validation is a candidate follow-up.

### v6.8.0 — Production Excellence *(planned, Q1 2027)*

Theme: *stop one validator from melting an instance, and answer "why is this failing in prod"
in one query.*

- **R7: Validation budget.** Per-request budget for total validation wall-clock and rule
  count; tripping the budget short-circuits with a structured `ValidationBudgetExceeded`
  result and a metric.
- **R8: Top-N failure analytics.** Built-in aggregation of failure reasons over a rolling
  window, exposed both as `IMeter` histograms and a `MapValidationAnalytics` endpoint.
- **Per-rule circuit breaker.** Optional circuit-breaker wrapper around individual async
  rules (the v6.6.0 `MustAsync` path) so a flaky external dependency cannot stall the request
  pipeline.
- **OpenTelemetry semantic-convention pass.** Align metric and span names with the upcoming
  OTel semconv shape, in lockstep with [[orionaudit]] and [[orionlock]]. Builds on the v6.6.1
  self-deriving meter versions so the rename lands cleanly across every assembly.

### v7.0.0 — Stable API *(planned, Q2 2027)*

Theme: *commit to the surface and remove the deprecated paths.*

- **API freeze + SemVer 2.0.0 commitment.** Public types lock; breaking changes only on
  majors from this point.
- **Remove the in-box `StronglyTypedId` source generator** (soft-deprecated in v6.5, removed
  here). Migration path: install `OrionKey`, no source changes required if the migration
  analyzer was followed.
- **Drop net8.0 decision.** Decide and publish whether v7.x ships TFM `net8.0` or starts at
  `net9.0`. This is the last chance to cut net8 before SemVer locks it in for the 7.x line.
- **Documentation site.** Hosted reference + recipes + migration guides covering everything
  shipped in v6.x and the bridges to the rest of the family.

---

## Tier 0 -- Shipped in v6.0

A quick reference so the rest of this document makes sense.

- Fluent guard clauses with `CallerArgumentExpression` parameter capture
- Zero-allocation `FastGuard` with span-based hot paths + `SearchValues<T>`
- Security guards: SQL / XSS / path traversal / command / LDAP / XXE / open redirect
- File upload security: magic-byte verification, malicious content detection
- PII / sensitive data detection: card numbers, secrets, emails, phones, IPs
- Configuration guards for startup validation
- API contract guards
- Idempotency guard
- Deep nested / cross-property / polymorphic validation
- Dynamic (JSON-driven) rule engine
- Rule sets, `Include()`, `ValidationContext`, `Severity` (Error / Warning / Info)
- Parallel async rule execution with exception-safe batching
- Delta (PATCH-style) validation
- Source generator + Roslyn analyzer (`OG0001`)
- ASP.NET Core, MediatR, Blazor, gRPC, SignalR, Swagger, OpenTelemetry integrations
- 14-language localization, compiled regex LRU cache, expression accessor caching

See [FEATURES-v6.md](FEATURES-v6.md) for full details.

---

## Tier 1 -- Adoption & Growth (v6.7)

The fastest-impact work: remove migration friction, ship features that make
prospective users pick OrionGuard when evaluating libraries.

### R1. FluentValidation Migration Codemod `[Planned]` `L` `v6.7`

A `dotnet tool` that reads existing FluentValidation validators and rewrites them
as OrionGuard equivalents.

```bash
dotnet tool install -g Moongazing.OrionGuard.Migration
dotnet orionguard migrate ./src/MyProject.csproj --dry-run
dotnet orionguard migrate ./src/MyProject.csproj --apply
```

Built on Roslyn. Handles the 25 most common FluentValidation built-ins, rule sets,
`ChildRules`, `When/Unless`, `SetValidator`, and async rules. Emits a diff report
for anything it could not migrate automatically.

**Why it matters.** Migration effort is the single largest adoption blocker. The
ambition is for a typical codebase to migrate in minutes, not weeks.

---

### R2. OpenAPI-First Validation `[Shipped]` `L` `v6.7`

Given an OpenAPI 3 document, emit validators that enforce the schema constraints.
The inverse of `OrionGuard.Swagger`, which goes validator to OpenAPI. Shipped in v6.7.0
as the `OrionGuard.OpenApi` source-generator package.

```csharp
[OpenApiValidator("openapi.json", "#/components/schemas/CreateUserRequest")]
public partial class CreateUserValidator : IValidator<CreateUserRequest> { }
```

Source-generator powered. Supports `type`, `format` (email, uuid, date-time, date, uri,
hostname, ipv4), `minLength`, `maxLength`, `minimum`, `maximum` (incl. `exclusiveMinimum` /
`exclusiveMaximum`), `pattern`, `enum`, `required`, `nullable`, `minItems`, `maxItems`, and
intra-document `$ref` (nested schemas). The generated validator implements the real
`IValidator<T>` and returns a `GuardResult`, so it drops into the ASP.NET Core filter, the
MediatR behaviour, or a manual call exactly like a hand-written validator. The analyzer bundles
its own JSON reader and takes no unbundled NuGet dependency.

**Deferred (tracked follow-ups).** YAML documents are not read yet (JSON only; a YAML document
raises diagnostic `OG1002`) to avoid pulling a heavy, unbundleable YAML parser into the analyzer.
Polymorphism and composition (`discriminator` / `oneOf` / `anyOf` / `allOf`, the intended map to
`Validate.Polymorphic<T>()`) raise `OG1006` and are skipped rather than half-implemented. Generic
target types -- a generic `[OpenApiValidator]` class or a validator nested inside a generic type --
raise `OG1010` and are skipped, because reconstructing the partial's type parameters and constraints
correctly is a follow-up; a non-generic nested target is fully supported.

**Why it matters.** API-first teams want one source of truth. OpenAPI spec -> validator
-> controller -> tests -- all derived automatically. Nobody in the .NET space
offers this bidirectionally.

---

### R3. Resilience Pipeline for Async Rules `[Planned]` `M` `v6.8`

The async rule pipeline itself shipped in v6.6.0 (`MustAsync` + `ToResultAsync`); this entry
is the resilience layer on top of it, so a transient infrastructure failure does not surface
as a validation error. It pairs with the per-rule circuit breaker scheduled for v6.8.0.

```csharp
RuleForAsync(async u => await repo.IsEmailUniqueAsync(u.Email), "...")
    .Retry(3, backoff: TimeSpan.FromMilliseconds(100))
    .Timeout(TimeSpan.FromSeconds(2))
    .CircuitBreak(failureThreshold: 5, openFor: TimeSpan.FromSeconds(30));
```

Lightweight built-in implementation (no Polly dependency). Fallback configurable:
`FailOpen()` (treat as valid on exhaustion) or `FailLoud()` (emit structured
`RULE_RESILIENCE_EXHAUSTED` error).

---

### R4. Regulatory Rule Packs `[Planned]` `L per pack` `v6.2+`

Curated, audited validation packages for regulated domains. Each pack is:

- Maintained against the current regulatory text
- Documented with regulation references
- Independently versioned
- Independently testable with a published test suite

Planned initial packs:

| Package | Scope |
|---------|-------|
| `OrionGuard.Rules.EU` | EU VAT format per country, GDPR data classifications |
| `OrionGuard.Rules.US` | SSN, EIN, state codes, ZIP+4, FEIN checksum |
| `OrionGuard.Rules.TR` | TC Kimlik, Turkish phone, IBAN, vergi numarasi |
| `OrionGuard.Rules.UK` | NIN, postcode, company registration numbers |
| `OrionGuard.Rules.Banking` | FATCA / MiFID II / PSD2 fields, LEI |
| `OrionGuard.Rules.Health` | HL7 codes, ICD-10, HIPAA PHI tagging |
| `OrionGuard.Rules.Payments` | Card networks, BIN ranges, 3DS fields |

**Why it matters.** Fintech and healthcare teams currently spend weeks hand-rolling
these. Shipping signed, audited packs collapses that to minutes.

---

## Tier 2 -- Production Excellence (v6.8)

Features that production engineers, SREs, and compliance teams care about.

### R5. Validation Replay & Time-Travel Debugging `[Planned]` `M` `v6.8`

Append-only store of every validation run (input + rule decisions + timestamp)
with CLI replay.

```csharp
services.AddOrionGuardReplay(new S3ReplayStore("s3://my-bucket/validation-traces"));
```

```bash
dotnet orionguard replay --trace-id abc123
# Shows input, rules executed, outcomes, in order
```

Pluggable store: file, Redis, S3, Azure Blob, SQL. Optional PII scrubbing on write.

**Why it matters.** Production debugging time collapses from hours to seconds.
Fraud investigation and audit trail in one feature.

---

### R6. Distributed Validation Cache `[Planned]` `M` `v6.8`

`CachedValidator` backed by `IDistributedCache` (Redis / SQL Server / in-memory).
Cluster-aware, multi-instance deployment ready.

```csharp
services.AddStackExchangeRedisCache(o => o.Configuration = "redis:6379");
services.AddOrionGuardDistributedCache();

validator.WithDistributedCaching(
    ttl: TimeSpan.FromMinutes(5),
    keyPrefix: "orionguard:user:");
```

Graceful degradation: falls back to local per-process cache if the distributed
layer is unavailable.

---

### R7. Validation Budget Enforcement `[Planned]` `S` `v6.8`

Per-request time ceiling; slow validators are a silent performance regression source.

```csharp
validator.WithBudget(TimeSpan.FromMilliseconds(50))
    .WithBudgetExceededBehavior(BudgetPolicy.FailOpen);
```

`FailOpen` = continue + metric; `FailClosed` = HTTP 503. OpenTelemetry histogram
`orionguard.budget.exceeded_ratio`.

---

### R8. Top-N Failure Analytics `[Planned]` `S` `v6.8`

Rolling window of most-frequent validation failures, per property, per error code.

```csharp
services.AddOrionGuardAnalytics(window: TimeSpan.FromHours(24));
// Emits: orionguard.errors.top{property="Email",code="INVALID_FORMAT"} 1247
```

Ships with a Grafana dashboard JSON.

---

### R9. Performance Regression Guard `[Research]` `M` `v6.9`

CI mode that benchmarks every validator on a PR branch vs. main, fails the build if
p99 regresses by more than a configurable threshold.

```yaml
# .github/workflows/validation-perf.yml
- uses: moongazing/orionguard-perf@v1
  with:
    threshold: 10  # percent
    baseline: main
```

---

### R10. Snapshot Testing for Validators `[Planned]` `S` `v6.8`

Verify.NET-style snapshot of rule definitions. Catches silent behavioural changes.

```csharp
[Fact]
public async Task UserValidator_Rules_MatchSnapshot()
{
    var validator = new CreateUserValidator();
    await validator.VerifySnapshot();
}
```

Canonical JSON serialization of rule metadata (property, operator, parameters,
severity, rule-set). Non-deterministic elements stabilised.

---

## Tier 3 -- Differentiation & Innovation (v6.3 - v7.0)

Features that change how people think about validation, not just incremental
improvements.

### R11. Test Data Generator `[Planned]` `L` `v7.0`

Given a validator, produce valid and invalid example instances.

```csharp
var gen = TestDataGenerator.For<CreateUserRequest>(new CreateUserValidator());

var valid = gen.GenerateValid();                // legally-valid instance
var invalid = gen.GenerateInvalid("Email");     // invalid only on Email
var matrix = gen.GenerateInvalidMatrix();       // one instance per rule
```

Unlike FsCheck / CsCheck, this **understands the validator's rules** and shrinks
against them. Integrates with xUnit `MemberData`, NUnit `TestCaseSource`.

**Why it matters.** The single highest-value developer productivity boost in the
roadmap. Test writing time for validators drops by roughly half.

---

### R12. Natural-Language Rule Compilation `[Research]` `L` `v7.0`

Author validation rules in prose; emit concrete rules at build time via an LLM.

```csharp
[NaturalLanguageRule("The password must have uppercase, lowercase, digit, and special char, minimum 8 chars.")]
public string Password { get; set; }
```

Build-time only -- the LLM emits the rules, which are committed to source. Runtime
has no model dependency. The generated output is reviewed like any other code.

**Why it matters.** Product managers and business analysts can specify rules
directly in human language, bypassing a translation step that today loses fidelity.

---

### R13. Streaming Validation `[Planned]` `M` `v6.9`

`IAsyncEnumerable<T>` support for validating large datasets without materializing
them in memory.

```csharp
var results = validator.ValidateStream(
    source: GetUsersFromDbAsStream(),
    parallelism: 8,
    continueOnError: true);

await foreach (var (user, result) in results)
{
    if (result.IsInvalid) await errorQueue.EnqueueAsync(user);
}
```

Back-pressure aware. Configurable parallelism. Designed for ETL pipelines, bulk
import, Kafka / Event Hubs consumers.

---

### R14. Batch + SIMD Validation `[Research]` `L` `v7.0`

Vectorized validation across a `Span<T>` of inputs.

```csharp
var results = validator.ValidateBatch(users);   // Span<User> -> Span<GuardResult>
```

Uses `Vector<T>` where applicable (numeric range checks, regex anchors, string
format probes). Target 10-50x speedup for bulk workflows.

---

### R15. State Machine Validation `[Planned]` `M` `v7.0`

First-class support for workflow state transitions.

```csharp
public class OrderStateValidator : StateMachineValidator<Order, OrderState>
{
    public OrderStateValidator()
    {
        From(OrderState.Pending).To(OrderState.Approved)
            .When((o, ctx) => ctx.Get<string>("Role") == "admin");

        From(OrderState.Approved).To(OrderState.Shipped)
            .When(o => o.PaymentConfirmed);
    }
}
```

Transitions that are not explicitly allowed are automatically invalid.

---

### R16. Feature Flag-Driven Rules `[Planned]` `M` `v6.9`

Gate individual rules on a feature flag provider.

```csharp
RuleFor(u => u.Age, "Age", v => v.InRange(18, 120))
    .EnabledWhen(FeatureFlag.Is("strict-age-validation"));
```

Adapters for LaunchDarkly, Unleash, Azure App Configuration, `Microsoft.FeatureManagement`.

**Why it matters.** Safe rollout of stricter validation. A/B test rule tightenings
against production traffic before committing.

---

### R17. Audit Trail Sink `[Research]` `M` `v7.0`

Every validation decision emitted to a sink (Kafka, EventBridge, Service Bus,
Azure Event Hubs) as a structured event.

```csharp
services.AddOrionGuardAuditTrail(
    sink: new KafkaAuditSink("validation-events"),
    includePayload: true,
    scrubbers: new[] { new CardNumberScrubber(), new EmailScrubber() });
```

Built-in PII scrubbers so audit events themselves don't violate GDPR / HIPAA.

---

### R18. Validator to TypeScript Generator `[Research]` `L` `v7.0`

Emit TypeScript validators (zod / yup flavours) from C# validators so the frontend
stays in sync automatically.

```bash
dotnet orionguard emit --lang typescript --flavour zod ./src/MyApi.csproj --out ./frontend/validators/
```

**Why it matters.** Full-stack teams manually re-implement validation on the
client today. This kills the drift.

---

### R19. JSON Schema Bidirectional `[Planned]` `M` `v6.9`

Import and export against JSON Schema draft 2020-12.

```csharp
var validator = JsonSchema.ToValidator("schemas/user.json");
var schema = validator.ToJsonSchema();
```

Fills the same role as OpenAPI-first (R2) but for non-HTTP schemas.

---

### R20. Validator Composition Graph Visualizer `[Planned]` `M` `v6.9`

CLI that renders Graphviz or Mermaid diagrams of a validator.

```bash
dotnet orionguard visualize --validator CreateUserValidator --format mermaid --out docs/rules.mmd
```

Shows rules, rule sets, `Include()` chains, `Parallel()` markers, sync vs async
split. Useful for onboarding and architecture review.

---

## Tier 4 -- Ecosystem & Integrations (Rolling)

Small, focused integrations. Low effort each, high cumulative value.

- **R21. MassTransit integration** `[Planned]` `S` -- auto-validate `IConsumer<T>` parameters.
- **R22. Hangfire integration** `[Shipped]` `S` -- validate job arguments on enqueue. Shipped in
  v6.7.0 as `OrionGuard.Hangfire` (client-filter, enqueue-time).
- **R23. Quartz.NET integration** `[Planned]` `S` -- validate job data map.
- **R24. Akka.NET integration** `[Research]` `M` -- actor message validation.
- **R25. Orleans integration** `[Research]` `M` -- grain call parameter validation.
- **R26. HotChocolate (GraphQL) integration** `[Planned]` `M` -- input type validation.
- **R27. YARP integration** `[Research]` `M` -- reverse-proxy-level validation.
- **R28. Azure Functions binding** `[Planned]` `S` -- attribute-driven binding validation.
- **R29. AWS Lambda binding** `[Planned]` `S` -- PowerTools-style filter.
- **R30. .NET Aspire dashboard integration** `[Planned]` `S` -- native telemetry panel.
- **R31. Dapr integration** `[Research]` `M` -- sidecar validation.
- **R32. Kafka consumer filter** `[Planned]` `S` -- validate consumed messages.
- **R33. Protobuf schema import** `[Research]` `M` -- generate validators from `.proto`.
- **R34. Avro schema import** `[Research]` `M` -- generate validators from Avro schemas.

---

## Tier 5 -- Developer Experience (Rolling)

Tooling that makes OrionGuard feel effortless.

- **R35. VS Code extension** `[Planned]` `L` -- snippets, intellisense, rule preview, quick-fix "add validation rule" code action.
- **R36. JetBrains Rider extension** `[Planned]` `L` -- feature parity with VS Code extension.
- **R37. Watch mode CLI** `[Planned]` `S` -- `dotnet orionguard watch` re-runs validators on file save.
- **R38. Interactive REPL** `[Planned]` `M` -- `dotnet orionguard repl` for ad-hoc validator testing.
- **R39. IDE snippet pack** `[Planned]` `S` -- `ogemail`, `ogrange`, `ogruleset` style triggers.
- **R40. Fix-it hints** `[Research]` `M` -- `"did you mean 'user@example.com'?"` suggestions in error messages.
- **R41. Rule coverage reporter** `[Planned]` `M` -- coverlet-equivalent for validators: which rules ran in your tests?
- **R42. Git pre-commit hook generator** `[Planned]` `S` -- `dotnet orionguard init-hooks` installs local validation hooks.
- **R43. Mutation testing integration (Stryker.NET)** `[Research]` `M` -- verify tests detect broken validators.
- **R44. Property-based testing helpers (CsCheck)** `[Research]` `M` -- generators that honour validator rules.

---

## Tier 6 -- Novel & Experimental

Bigger bets. Smaller probability of shipping but high upside if they do.

### R45. Validator-to-SQL CHECK Constraints `[Research]` `L`

Emit DB CHECK constraints from validators so schema integrity matches application rules.

```bash
dotnet orionguard emit-sql --validator CreateUserValidator --dialect postgres --out migrations/constraints.sql
```

**Why it matters.** Database constraints are a last line of defence most apps lack.
This generates them for free from existing validators.

---

### R46. Validator WebAssembly Compilation `[Research]` `XL`

Compile validators to WebAssembly modules that run in any language, including on
CDN edge workers (Cloudflare Workers, Fastly Compute, Akamai EdgeWorkers).

**Why it matters.** Validate at the edge, reject invalid requests before they hit
origin. Massive cost savings for high-volume APIs.

---

### R47. Rule Marketplace `[Research]` `XL`

npm-style registry for community-contributed rule packs with signing, versioning,
and discovery.

```bash
dotnet orionguard pack add @acme/payment-rules@2.1.0
```

**Why it matters.** Network effect. A marketplace is a moat.

---

### R48. Chaos Engineering Injector `[Research]` `M`

Opt-in wrapper that randomly fails async rules to exercise resilience policies.

```csharp
validator = validator.WithChaos(failureRate: 0.05, environments: new[] { "staging" });
```

**Why it matters.** Most teams write resilience policies; very few test them.

---

### R49. Property Drift Detection `[Research]` `M`

Roslyn analyzer that detects properties with no validation rule -- stronger than
`OG0001` because it also detects new properties added after validator creation.

```csharp
public class CreateUserRequest
{
    [NotNull, Email] public string Email { get; set; }
    public string NewField { get; set; }   // warning OG0002: property added without updating validator
}
```

---

### R50. Rule Precedence Planner `[Research]` `M`

Static analysis of rule cost (cheap sync vs. expensive async) plus dependency graph,
suggests optimal execution order.

```bash
dotnet orionguard analyze --validator CreateUserValidator --suggest-order
# Suggests: Move NotEmpty (cost: 1us) before IsEmailUniqueAsync (cost: 50ms)
```

---

### R51. Fuzz Testing Integration `[Research]` `M`

SharpFuzz integration for finding inputs that crash validators.

```csharp
[Fuzz]
public async Task CreateUserValidator_NeverThrows(FuzzInput input)
{
    var result = new CreateUserValidator().Validate(input.ToCreateUserRequest());
    Assert.True(result.IsValid || result.IsInvalid);
}
```

---

### R52. Validator Diffing in CI `[Research]` `M`

GitHub Action that inspects rule changes on every PR and posts a human-readable summary.

```
CreateUserValidator
  + RuleFor(Email).NotEmpty().Email()  -- added Email constraint
  - RuleFor(Age).InRange(0, 150)        -- removed (was this intentional?)
```

---

### R53. Zero-Downtime Rule Hot-Reload `[Research]` `L`

Hot-reload validators from `DynamicValidator` without process restart. Paired with
distributed config (Azure App Config, AWS AppConfig, Consul) for live updates
across a fleet.

---

### R54. Compliance Certification Export `[Research]` `M`

Generate signed PDF reports showing exactly which rules applied to a given request
-- suitable for SOC2 / ISO 27001 / PCI audits.

---

### R55. Local-First Rule Editor (Blazor) `[Research]` `L`

A self-hosted Blazor app that lets PMs / BAs edit rules through a UI. Rule changes
commit to source control as a pull request.

---

## Community Wishlist

Ideas raised by the community that we have not yet costed. If you see one you want,
upvote on the linked issue. Items with enough signal graduate to an official tier.

*(This section will populate from GitHub issues once the repository is public.)*

---

## How to Propose a Feature

1. Open a GitHub issue using the **Feature Proposal** template.
2. One paragraph describing the use case (who, what, why).
3. Sketch the minimal public API (5-10 lines of sample code).
4. Identify the scope: S / M / L / XL.
5. Label: `proposal` + appropriate `area/*` tag.

Accepted proposals land in this document under the appropriate tier and become
candidates for the next milestone.

**What makes a proposal strong:**

- A real use case from a real codebase (not "it would be cool if")
- A clear reason existing features don't already cover it
- A minimal API that fits the existing patterns
- Consideration of edge cases and failure modes

**What weakens a proposal:**

- "FluentValidation has X, we should too" -- we only adopt features if they are
  better than FluentValidation's version, not to reach parity.
- Requests that expand the public API surface without a clear migration story for
  existing users.
- Anything that requires runtime reflection on types that source generators
  could handle at compile time.

---

## Release Cadence

- **Patch releases** (`6.x.y`) -- bug fixes and minor tweaks, shipped as needed.
- **Minor releases** (`6.x.0`) -- feature drops targeting one or two Tier 1-2 items.
  Target cadence: every 6-8 weeks.
- **Major releases** (`7.0`) -- breaking changes bundled. The current target window for
  v7.0 is **Q2 2027**; the headline breaking change is the removal of the in-box
  `StronglyTypedId` source generator (soft-deprecated in v6.5 in favour of
  [OrionKey](https://github.com/tunahanaliozturk/OrionKey)).

### One-line milestone calendar

| Release | Target             | Theme                                                        |
| ------- | ------------------ | ------------------------------------------------------------ |
| v6.4.1  | shipped 2026-05-23 | Logo refresh                                                 |
| v6.5.0  | shipped 2026-06-01 | Family integration (OrionKey/OrionLock bridges, push outbox) |
| v6.6.0  | shipped 2026-06-19 | Asynchronous validation pipeline (MustAsync / ToResultAsync) |
| v6.6.1  | shipped 2026-06-20 | Self-deriving diagnostics meter versions                     |
| v6.6.2  | shipped 2026-06-20 | Allocation-free digit / identity validators                  |
| v6.7.0  | shipped 2026-06-23 | OpenAPI-first validation + Hangfire integration (FluentValidation codemod in progress) |
| v6.8.0  | Q1 2027            | Validation budget, top-N analytics, circuit-breaker          |
| v7.0.0  | Q2 2027            | API freeze, remove deprecated paths, docs site               |

Dates are targets, not commitments. If a milestone slips by more than four weeks, the delay
shows up here.

---

## Non-Goals

Things OrionGuard will deliberately not do:

- **Become a general-purpose rule engine.** OrionGuard is for input and state
  validation. For business rule engines, use NRules or similar.
- **Replace database integrity constraints.** Validators complement the database,
  they do not replace it. Use both.
- **Emit runtime code generation at runtime.** All code generation must happen at
  build time so NativeAOT remains fully supported.
- **Require a paid license for any feature.** The core library and every ecosystem
  package will remain MIT-licensed indefinitely.

---

**Last updated.** 2026-06-23

**Curator.** [Tunahan Ali Ozturk](https://github.com/tunahanaliozturk)

**Questions, feedback, proposals.** Open an issue, start a discussion, or reach out
directly. This roadmap exists because the community shapes it.
