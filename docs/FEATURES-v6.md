# OrionGuard v6.0

**The validation ecosystem for .NET that scales from a single guard clause to enterprise-grade validation pipelines.**

.NET 8 | .NET 9 | .NET 10 -- MIT License

---

## Table of Contents

- [Design Philosophy](#design-philosophy)
- [Architecture Overview](#architecture-overview)
- [Which API Should I Use?](#which-api-should-i-use)
- [Core Validation Engine](#core-validation-engine)
- [Contextual Validation](#contextual-validation)
- [Multi-Severity Validation](#multi-severity-validation)
- [Validator Composition with Include](#validator-composition-with-include)
- [Parallel Async Rule Execution](#parallel-async-rule-execution)
- [Delta (PATCH) Validation](#delta-patch-validation)
- [Performance Engineering](#performance-engineering)
- [Security Layer](#security-layer)
- [File Upload Security](#file-upload-security)
- [Sensitive Data & Compliance Guards](#sensitive-data--compliance-guards)
- [Advanced Validation Patterns](#advanced-validation-patterns)
- [Startup & Configuration Guards](#startup--configuration-guards)
- [API Contract Validation](#api-contract-validation)
- [Idempotency Guard](#idempotency-guard)
- [Ecosystem & Integrations](#ecosystem--integrations)
- [Roslyn Analyzer (OG0001)](#roslyn-analyzer-og0001)
- [Observability & Diagnostics](#observability--diagnostics)
- [Internationalization](#internationalization)
- [Domain-Specific Guards](#domain-specific-guards)
- [Extensibility](#extensibility)
- [Exception Architecture](#exception-architecture)
- [Thread Safety Guarantees](#thread-safety-guarantees)
- [Real-World Architecture Scenarios](#real-world-architecture-scenarios)
- [Migration Path](#migration-path)
- [Performance Benchmarks](#performance-benchmarks)
- [Competitive Analysis](#competitive-analysis)

---

## Design Philosophy

OrionGuard is built around five principles that guide every API decision:

| Principle | What It Means in Practice |
|-----------|--------------------------|
| **Fail fast, fail loud** | Guard clauses throw immediately at system boundaries. No silent data corruption propagating through layers. |
| **Fail complete** | Result pattern collects *all* errors in a single pass. Users fix everything at once instead of playing whack-a-mole with one-at-a-time validation. |
| **Zero-cost when valid** | Hot-path guards use `ReadOnlySpan<T>`, aggressive inlining, and ThrowHelper separation. Valid inputs pay near-zero overhead. |
| **Convention over configuration** | Source generators, attribute-based validation, and auto-wired pipelines eliminate boilerplate. You write the *what*, not the *how*. |
| **Escape hatches everywhere** | Every default is overridable. Custom exception factories, message resolvers, rule engines, and profile registries give you full control when conventions don't fit. |

---

## Architecture Overview

```text
                          +-----------------------+
                          |   Your Application    |
                          +-----------+-----------+
                                      |
                  +-------------------+-------------------+
                  |                   |                   |
          +-------v-------+  +-------v-------+  +-------v--------+
          |  Guard Clauses |  | Object Valid. |  | Dynamic Rules  |
          |  Ensure.That() |  | Validate.*()  |  | JSON Engine     |
          |  FastGuard.*() |  | AbstractValid.|  | DynamicValidator |
          |  Guard.*()     |  | Attributes    |  |                 |
          +-------+-------+  +-------+-------+  +-------+--------+
                  |                   |                   |
                  +-------------------+-------------------+
                                      |
                          +-----------v-----------+
                          |     GuardResult       |
                          |  (Unified Error Model)|
                          +-----------+-----------+
                                      |
          +---------------------------+---------------------------+
          |              |            |            |              |
    +-----v-----+ +-----v----+ +----v-----+ +----v----+ +------v------+
    | ASP.NET   | | MediatR  | | Blazor   | | gRPC    | | SignalR     |
    | Core      | | Pipeline | | EditForm | | Interc. | | Hub Filter  |
    +-----------+ +----------+ +----------+ +---------+ +-------------+
          |              |            |            |              |
          +--------------+------------+------------+--------------+
                                      |
                          +-----------v-----------+
                          |    OpenTelemetry      |
                          |  Metrics + Tracing    |
                          +-----------------------+
```

Every validation path -- whether a single `Ensure.That()` call, a full object graph traversal, or a JSON-driven dynamic rule -- produces the same `GuardResult`. This unified model is what makes the ecosystem packages possible: ASP.NET Core, MediatR, Blazor, gRPC, and SignalR all consume `GuardResult` without knowing how it was produced.

---

## Which API Should I Use?

OrionGuard provides multiple validation entry points. Choose based on your use case:

```text
Is it a hot path (middleware, tight loop)?
  YES --> FastGuard.*()           Zero-alloc, span-based, aggressively inlined
  NO  |
      v
Do you need all errors at once?
  YES --> Ensure.Accumulate()     Error accumulation mode, returns GuardResult
  NO  |
      v
Single value, throw-on-first?
  YES --> Ensure.That()           Fluent chain, CallerArgumentExpression
  NO  |
      v
Validating a full object (DTO)?
  YES --> Is it a flat DTO?
          YES --> Validate.For()       Property-level with expressions
          NO  --> Validate.Nested()    Deep nested with indexed paths
  NO  |
      v
Method preconditions/postconditions?
  YES --> Contract.Requires()     Design-by-contract
  NO  |                           Contract.Ensures()
      v
Rules change without deploy?
  YES --> DynamicValidator         JSON-driven runtime rules
  NO  |
      v
Pipeline (MediatR/ASP.NET)?
  YES --> AbstractValidator<T>    DI-integrated, RuleSet support
  NO  |
      v
DEBUG-only assertions?
  YES --> DebugGuard.Assert()     Compiles to no-op in Release
```

| API | Throws? | Accumulates? | Allocation | Best For |
|-----|:-------:|:------------:|:----------:|----------|
| `FastGuard.*()` | Yes | No | Zero | Middleware, hot loops, high-throughput |
| `Ensure.That()` | Yes | No | Minimal | Service layer, business logic |
| `Ensure.Accumulate()` | No | Yes | Minimal | API endpoints, user-facing errors |
| `Validate.For<T>()` | Optional | Yes | Expression cache | Flat DTO validation |
| `Validate.Nested<T>()` | Optional | Yes | Expression cache | Complex object graphs |
| `AbstractValidator<T>` | Optional | Yes | DI-scoped | Pipeline integration, DI |
| `DynamicValidator` | No | Yes | JSON parse | Runtime-configurable rules |
| `Contract.*()` | Yes | No | Zero | Internal method contracts |
| `DebugGuard.*()` | DEBUG only | No | Zero | Development assertions |
| `Guard.*()` | Yes | No | Minimal | Legacy v3 compatibility |

---

## Core Validation Engine

### Fluent Guard Clauses

The primary API. Parameter names are auto-captured via `CallerArgumentExpression` -- no `nameof()` ceremony.

```csharp
// Simple -- throws on first violation
Ensure.That(email).NotNull().NotEmpty().Email();
Ensure.That(age).InRange(18, 120);
Ensure.That(password).NotNull().MinLength(8).Matches(@"^(?=.*[A-Z])");

// Shorthand methods -- validate and return the value in one call
string validEmail = Ensure.NotNull(userInput.Email);
string validName  = Ensure.NotNullOrEmpty(userInput.Name);
string validTitle = Ensure.NotNullOrWhiteSpace(userInput.Title);
int validAge      = Ensure.InRange(userInput.Age, 18, 120);

// Transform pipeline -- clean input before validation
var normalized = Ensure.That(rawEmail)
    .Transform(e => e.Trim().ToLowerInvariant())
    .Default("unknown@example.com")
    .Email()
    .Value;

// TryValidate pattern -- no exceptions, tuple return
bool isValid = Ensure.Accumulate(email, "Email")
    .NotNull().Email()
    .TryValidate(out var validatedEmail, out var errors);

// Implicit conversion -- use directly where a T is expected
string cleaned = Ensure.That(rawInput).NotNull().NotEmpty().Transform(s => s.Trim());
```

**30+ built-in validations:**

| Category | Methods |
|----------|---------|
| **Null/Default** | `NotNull`, `NotDefault` |
| **String** | `NotEmpty`, `Length`, `MinLength`, `MaxLength`, `Email`, `Url`, `Matches`, `StartsWith`, `EndsWith`, `Contains` |
| **Numeric** | `GreaterThan`, `LessThan`, `InRange`, `Positive`, `NotNegative`, `NotZero` |
| **Collection** | `Count`, `MinCount`, `MaxCount`, `All`, `NoNullItems` |
| **DateTime** | `InPast`, `InFuture`, `DateBetween` |
| **Transform** | `Transform`, `Default` |
| **Custom** | `Must(predicate, message)` |
| **Control** | `When`, `Unless`, `Always` |

### Result Pattern -- Error Accumulation

For API endpoints where you need to return *all* validation failures in a single response:

```csharp
var result = GuardResult.Combine(
    Ensure.Accumulate(email, "Email").NotNull().Email().ToResult(),
    Ensure.Accumulate(password, "Password").MinLength(8).ToResult(),
    Ensure.Accumulate(age, "Age").InRange(18, 120).ToResult()
);

if (result.IsInvalid)
    return BadRequest(result.ToErrorDictionary());
// => { "Email": ["Email must be a valid email address."], "Password": ["..."] }

// Or throw an AggregateValidationException with all errors
result.ThrowIfInvalid();
```

`GuardResult` full API:

| Member | Description |
|--------|-------------|
| `IsValid` / `IsInvalid` | Boolean check |
| `Errors` | `IReadOnlyList<ValidationError>` with `ParameterName`, `Message`, `ErrorCode` |
| `SuggestedHttpStatusCode` | HTTP status hint for ProblemDetails mapping (400, 422, etc.) |
| `ThrowIfInvalid()` | Throws `AggregateValidationException` if invalid |
| `GetErrorSummary(separator)` | Human-readable error string |
| `ToErrorDictionary()` | `Dictionary<string, string[]>` for API responses |
| `Combine(params GuardResult[])` | Merge multiple results |
| `Merge(other)` | Combine with another result |
| `Success()` / `Failure(...)` | Static factory methods |
| `FailureWithStatus(httpStatus, ...)` | Failure with HTTP status hint |

### Conditional Validation

Apply rules only when business conditions are met. `.Always()` resets the condition scope.

```csharp
Ensure.That(age)
    .When(requireAge).InRange(18, 120)     // Only when requireAge is true
    .Unless(isAdmin).Positive()             // Skip when isAdmin is true
    .Always().NotZero();                    // Always enforced

// Lambda-based conditions
Ensure.That(discountCode)
    .When(order => order.Total > 100)
    .NotEmpty()
    .Matches(@"^[A-Z0-9]{8}$");
```

### Async Validation

For rules that require I/O -- database uniqueness checks, external API calls, blacklist lookups:

```csharp
var result = await EnsureAsync.That(email, "Email")
    .UniqueAsync(async e => await db.IsEmailUniqueAsync(e))
    .ExistsAsync(async e => await emailService.IsDeliverableAsync(e))
    .MustAsync(async e => await IsNotBlacklistedAsync(e), "Email is blacklisted")
    .ValidateAsync();

// Three return modes
var value = await guard.ValidateAndGetAsync();                    // Returns T, throws if invalid
var (isValid, value, errors) = await guard.TryValidateAsync();    // Tuple, no throw

// Run multiple async validators in parallel
var combined = await EnsureAsync.AllAsync(
    userValidator.ValidateAsync(user),
    addressValidator.ValidateAsync(address),
    paymentValidator.ValidateAsync(payment)
);
```

### Logical Composition (AND / OR)

Combine validation predicates with boolean semantics:

```csharp
// OR -- at least one must pass
contact.EitherOr("Contact")
    .Or(c => IsValidEmail(c), "valid email")
    .Or(c => IsValidPhone(c), "valid phone number")
    .Validate();

// AND -- all must pass, with optional short-circuit
input.AllOf("Input", shortCircuit: true)
    .And(v => v != null, "cannot be null")
    .And(v => v.Length > 0, "cannot be empty")
    .And(v => v.Length <= 100, "too long")
    .And(v => !v.Contains("<script>"), "no scripts allowed")
    .ToResult();
```

### Code Contracts

Design-by-contract for internal method boundaries. Preconditions guard inputs, postconditions guard outputs, invariants guard state.

```csharp
public decimal CalculateDiscount(decimal price, decimal discountPercent)
{
    Contract.Requires(price >= 0, "Price must be non-negative");
    Contract.Requires(discountPercent is >= 0 and <= 100, "Invalid discount");
    Contract.RequiresNotNull(currency, nameof(currency));

    var result = price * (1 - discountPercent / 100);

    Contract.Ensures(result >= 0, "Result cannot be negative");
    Contract.Ensures(result <= price, "Discounted price cannot exceed original");

    return result;
}

// Invariant -- state that must always hold
Contract.Invariant(order.Items.Count > 0, "Order must have items");
```

### Debug-Only Assertions

`DebugGuard` methods are decorated with `[Conditional("DEBUG")]` -- they compile to zero code in Release builds.

```csharp
DebugGuard.Assert(index >= 0, "Index must be non-negative");
DebugGuard.NotNull(connection, nameof(connection));
DebugGuard.Require(buffer.Length > 0, () => new InvalidOperationException("Empty buffer"));
```

Use these for internal invariants that are too expensive for production but valuable during development.

### GuardManager -- Batch Execution

Execute multiple guard clauses as a batch, collecting all failures:

```csharp
var manager = new GuardManager()
    .AddGuard(new EmailGuardClause(email))
    .AddGuard(new AgeGuardClause(age))
    .AddGuard(new PasswordGuardClause(password));

manager.Execute();                              // Throws AggregateException if any fail
List<Exception> errors = manager.ExecuteWithResults(); // Returns all exceptions, no throw
```

---

---

## Contextual Validation

Rules frequently need to consult data that lives outside the object being validated --
current tenant id, authenticated user role, feature flag state, correlation id. OrionGuard
exposes this through an immutable, thread-safe `ValidationContext`.

```csharp
// Build a context (immutable; every With* returns a new instance)
var tenantKey = new ValidationContextKey<string>("TenantId");
var roleKey = new ValidationContextKey<string>("UserRole");

var context = ValidationContext.Empty
    .With(tenantKey, "acme")
    .With(roleKey, "admin")
    .With("FeatureFlag.AdvancedValidation", true);

// Pass it to any IValidator<T>
var result = validator.Validate(user, context);
```

Rules inside an `AbstractValidator<T>` can accept the context as a second predicate argument:

```csharp
public class UserValidator : AbstractValidator<User>
{
    public UserValidator()
    {
        // Only admins may assign the "Admin" role
        RuleFor((u, ctx) =>
            ctx.Get<string>("UserRole") == "admin" || u.Role != "Admin",
            "Only administrators can assign the Admin role.",
            "Role");

        // Tenant-scoped uniqueness via an async rule
        RuleForAsync((u, ctx) =>
            repo.IsUniqueInTenantAsync(u.Email, ctx.Get<string>("TenantId")),
            "Email must be unique within the tenant.",
            "Email");
    }
}
```

| Feature | API |
|---------|-----|
| Untyped get | `context.Get<T>(string key)`, `TryGet<T>` |
| Strongly-typed key | `ValidationContextKey<T>`, `With<T>(key, value)` |
| Presence check | `Contains(key)`, `Count` |
| Empty marker | `ValidationContext.Empty` |

**Design.** The context is an `ImmutableDictionary<string, object?>` -- lock-free reads,
copy-on-write updates, safe to capture in closures. Context-less overloads (`Validate(T)`)
internally pass `ValidationContext.Empty`, so existing code continues to work unchanged.

---

## Multi-Severity Validation

Not every failed rule should block the operation. A weak (but acceptable) password is a
*warning*. A non-corporate email on a B2B signup is *info*. A null required field is an
*error*. OrionGuard supports all three via `Severity`.

```csharp
public class UserValidator : AbstractValidator<User>
{
    public UserValidator()
    {
        // Blocks submission
        RuleFor(u => u.Password, "Password", v => v.NotNull().MinLength(8));

        // Non-blocking -- surfaced to UI for encouragement
        RuleFor(u => u.Password, "Password", v => v.Must(HasUppercase, "Consider a stronger password"))
            .WithSeverity(Severity.Warning);

        // Informational only -- for telemetry / UX hints
        RuleFor(u => u.Email, "Email", v => v.Must(IsCorporate, "Personal emails are discouraged"))
            .WithSeverity(Severity.Info);
    }
}

var result = validator.Validate(user);

result.IsInvalid      // true only if Severity.Error entries exist
result.Errors         // Severity.Error only (backward-compatible)
result.Warnings       // Severity.Warning
result.Infos          // Severity.Info
result.AllIssues      // everything regardless of severity
result.HasWarnings    // quick boolean check
```

**Key property:** `IsInvalid` is driven exclusively by `Severity.Error`, so a result
carrying only warnings/infos is still considered *valid*. `ToErrorDictionary()` and
`GetErrorSummary()` also filter to errors -- your API response format never sees
warnings unless you opt in via `AllIssues`.

The `IRuleBuilder` returned from `RuleFor` supports chaining:

```csharp
RuleFor(u => u.Code, "Code", v => v.NotEmpty())
    .WithSeverity(Severity.Warning)
    .WithErrorCode("USER_CODE_RECOMMENDED");
```

---

## Validator Composition with `Include`

Share rule sets across multiple validators without re-declaring them. Classic use case:
a base `UserValidator` with email/name rules that both `CreateUserValidator` and
`UpdateUserValidator` should honor.

```csharp
public class BaseUserValidator : AbstractValidator<User>
{
    public BaseUserValidator()
    {
        RuleFor(u => u.Email, "Email", v => v.NotEmpty().Email());
        RuleFor(u => u.Name, "Name", v => v.NotEmpty().Length(2, 100));
    }
}

public class CreateUserValidator : AbstractValidator<User>
{
    public CreateUserValidator()
    {
        Include(new BaseUserValidator());                           // Import base rules
        RuleFor(u => u.Password, "Password", v => v.MinLength(8));  // Add create-only rules
    }
}

public class UpdateUserValidator : AbstractValidator<User>
{
    public UpdateUserValidator()
    {
        Include(new BaseUserValidator());                           // Same base rules
        RuleFor(u => u.Id, "Id", v => v.NotNull());                 // Plus update-specific
    }
}
```

**Semantics.** Imported rules preserve their rule-set grouping -- rules from the child's
`"default"` set join the parent's `"default"` set; rules from `RuleSet("create", ...)`
stay in `"create"`. Imported rules run *after* locally-declared rules, matching standard
inheritance "derived first, base second" ordering.

---

## Parallel Async Rule Execution

Async rules that hit independent I/O resources (DB uniqueness + external email service +
blacklist API) can run concurrently instead of serially. Opt in per rule with `.Parallel()`.

```csharp
public class UserValidator : AbstractValidator<User>
{
    public UserValidator(IUserRepo repo, IEmailApi email, IBlacklistApi blacklist)
    {
        // Runs sequentially (default) -- fast, in-memory
        RuleFor(u => u.Email, "Email", v => v.NotEmpty().Email());

        // Three independent I/O-bound rules -- batched and awaited via Task.WhenAll
        RuleForAsync(async u => await repo.IsEmailUniqueAsync(u.Email),
            "Email already registered.", "Email")
            .Parallel();
        RuleForAsync(async u => await email.IsDeliverableAsync(u.Email),
            "Email is undeliverable.", "Email")
            .Parallel();
        RuleForAsync(async u => await blacklist.IsCleanAsync(u.Email),
            "Email is blacklisted.", "Email")
            .Parallel();
    }
}

// Before: ~300ms (100ms x 3 sequential)
// After:  ~100ms (Task.WhenAll, bounded by the slowest)
var result = await validator.ValidateAsync(user);
```

**Batching rules.** Consecutive `Parallel`-marked async rules inside a single rule set
are batched and awaited together. A non-parallel rule flushes the current batch before
running sequentially -- this preserves any intentional ordering you rely on.

**When *not* to use.** Rules that share mutable state, depend on prior rule outcomes, or
have order-dependent side effects (e.g., an audit log). For those, leave the default
sequential behavior in place.

---

## Delta (PATCH) Validation

For `PATCH` endpoints you typically want to validate *only* the fields the client
actually changed -- not force-revalidate every property. `Validate.Delta` compares
a before/after snapshot and skips rules whose target property did not change.

```csharp
[HttpPatch("/api/users/{id}")]
public async Task<IActionResult> Patch(int id, [FromBody] User patch)
{
    var original = await repo.GetAsync(id);

    var result = Validate.Delta(original, patch)
        .ForChanged(u => u.Email, g => g.NotNull().Email())
        .ForChanged(u => u.Age, g => g.InRange(18, 120))
        .ForChanged(u => u.Password, g => g.MinLength(8))
        .WhenChanged(u => u.Role, d => d
            .Must(u => AllowedRoles.Contains(u.Role), "Role", "Role is not permitted."))
        .ToResult();

    if (result.IsInvalid) return BadRequest(result.ToErrorDictionary());
    // ... apply patch
}
```

| Method | Behavior |
|--------|---------|
| `ForChanged(selector, configure)` | Runs a `FluentGuard<TProperty>` only when the property differs |
| `WhenChanged(selector, configure)` | Runs a *block* of rules when the property differs -- group related checks |
| `Must(predicate, propertyName, message)` | Unconditional ad-hoc rule (runs regardless of delta state) |

**Change detection.** Uses `EqualityComparer<T>.Default` -- works correctly for value
types, reference types (`Equals`), and records (structural equality). Deep object-graph
comparison is intentionally out of scope; use `Validate.Nested` for that.

**Creation flow.** Pass `null` for `original` to treat every property as changed.
Useful when reusing a delta validator for both POST (create) and PATCH (update).

```csharp
// POST /api/users -- every property is "changed"
var result = Validate.Delta<User>(original: null, updated: newUser)
    .ForChanged(u => u.Email, g => g.Email())
    .ToResult();

// Introspect what changed for audit logs
foreach (var name in new Delta<User>(original, updated).GetChangedPropertyNames())
    logger.LogInformation("User field {Field} changed", name);
```

---

## Performance Engineering

### FastGuard -- Zero-Allocation Hot-Path Validation

Every method is `[MethodImpl(AggressiveInlining)]`. Throw logic is separated into `[DoesNotReturn]` + `[StackTraceHidden]` helper methods so the JIT keeps the happy path small and inlineable.

```csharp
FastGuard.NotNull(user, nameof(user));
FastGuard.NotNullOrEmpty(email, nameof(email));
FastGuard.Email(email, nameof(email));          // Span-based parsing, no regex
FastGuard.InRange(age, 0, 150, nameof(age));    // Branchless: (uint)(v-min) > (uint)(max-min)
FastGuard.ValidGuid(id.AsSpan(), nameof(id));   // Span overload, no string allocation
FastGuard.Positive(quantity, nameof(quantity));
FastGuard.Finite(temperature, nameof(temperature));
FastGuard.Ascii("hello123".AsSpan(), nameof(input));
FastGuard.AlphaNumeric("abc123".AsSpan(), nameof(input));
FastGuard.NumericString("123456".AsSpan(), nameof(input));
FastGuard.MaxLength("short".AsSpan(), 100, nameof(input));
```

### Why This Matters

In a typical web API processing 10K req/s, guard clauses in middleware execute millions of times per minute. The difference between a regex-based email check and a span-based one is measurable in p99 latency and GC pause frequency. FastGuard is designed for these paths.

### GeneratedRegex (NativeAOT Ready)

All 24+ regex patterns in the library use `[GeneratedRegex]` attributes via the `GeneratedRegexPatterns` class:

```csharp
// Source-generated at compile time -- zero runtime compilation
GeneratedRegexPatterns.Email()
GeneratedRegexPatterns.StrongPassword()
GeneratedRegexPatterns.Alphabetic()
GeneratedRegexPatterns.Numeric()
GeneratedRegexPatterns.AlphaNumeric()
GeneratedRegexPatterns.PhoneNumber()
GeneratedRegexPatterns.Ascii()
GeneratedRegexPatterns.Unicode()
GeneratedRegexPatterns.Emoji()
GeneratedRegexPatterns.UppercaseAlphanumeric()
GeneratedRegexPatterns.LowercaseUnderscore()
GeneratedRegexPatterns.SwiftCode()
GeneratedRegexPatterns.Vin()
GeneratedRegexPatterns.VatNumber()
GeneratedRegexPatterns.EmbeddedEmail()        // For PII detection in logs
GeneratedRegexPatterns.EmbeddedPhoneNumber()  // For PII detection in logs
GeneratedRegexPatterns.IPv4Address()          // For IP detection in logs
```

Compatible with NativeAOT `PublishAot=true` -- no reflection, no runtime compilation.

### Performance Techniques Summary

| Technique | Where It's Used | Impact |
|-----------|----------------|--------|
| `FrozenSet<string>` | Security guards, card prefixes, country codes, currency codes, dangerous extensions | O(1) immutable lookups |
| `FrozenDictionary<string, byte[][]>` | File upload magic byte signatures | O(1) extension-to-signature mapping |
| `ReadOnlySpan<T>` | FastGuard, LDAP injection detection | Zero heap allocation |
| `[MethodImpl(AggressiveInlining)]` | All FastGuard methods | JIT inlines the happy path |
| `[DoesNotReturn]` + `[StackTraceHidden]` | ThrowHelper (20+ throw methods) | Smaller JIT bodies, clean stack traces |
| `RegexCache` (bounded 1000) | Custom pattern matching | Prevents unbounded memory, reuses compiled regex |
| `ConcurrentDictionary` expression cache | ObjectValidator compiled property accessors | Avoids repeated `Expression.Compile()` |
| `PropertyCache<T>` | ObjectGuards | Static generic cache for PropertyInfo[] |
| `CachedValidator<T>` with TTL | Decorator for any IValidator | Skips re-validation for identical inputs |
| Branchless range check | `FastGuard.InRange` | `(uint)(v-min) > (uint)(max-min)` -- single comparison |

---

## Security Layer

Injection detection at application boundaries. Every pattern set uses `FrozenSet<string>` for O(1) membership checks.

### Threat Coverage

| Guard | Threat | Pattern Count | Technique |
|-------|--------|:------------:|-----------|
| `AgainstSqlInjection` | SQL injection (OWASP A03) | 28 | Keyword + operator matching |
| `AgainstXss` | Cross-site scripting (OWASP A03) | 28 | Event handlers, DOM sinks, script vectors |
| `AgainstPathTraversal` | Path traversal (OWASP A01) | 14 | Raw + URL-encoded `../` variants |
| `AgainstCommandInjection` | OS command injection (OWASP A03) | 14 | Shell metacharacters, known interpreters |
| `AgainstLdapInjection` | LDAP injection | 8 | Span-based special character detection |
| `AgainstXxe` | XML external entity (OWASP A05) | 3 | `<!DOCTYPE>`, `<!ENTITY>`, `SYSTEM` |
| `AgainstUnsafeFileName` | File upload attacks | -- | Path traversal + OS-invalid characters |
| `AgainstOpenRedirect` | Open redirect (OWASP A01) | -- | Domain allow-list enforcement |
| `AgainstInjection` | **All of the above combined** | 95+ | Single-call composite guard |

```csharp
// At the API boundary -- one call catches everything
userInput.AgainstInjection(nameof(userInput));

// Or granular when you need specificity
filePath.AgainstPathTraversal(nameof(filePath));
redirectUrl.AgainstOpenRedirect(nameof(redirectUrl), "myapp.com", "api.myapp.com");
uploadName.AgainstUnsafeFileName(nameof(uploadName));
xmlPayload.AgainstXxe(nameof(xmlPayload));
ldapFilter.AgainstLdapInjection(nameof(ldapFilter));
```

### Design Decision: Why Reject, Not Sanitize?

OrionGuard *rejects* rather than *sanitizes*. Sanitization (stripping tags, escaping quotes) is lossy and error-prone -- different contexts require different escaping. Guard clauses enforce the invariant "this input is safe" and throw if it isn't. Output encoding remains the responsibility of the rendering layer (Razor, JSON serializer, etc.), which is the correct architectural boundary.

---

## File Upload Security

Deep file validation beyond just checking extensions. Detects fake MIME types via magic byte inspection, enforces size limits, and scans for malicious content embedded in non-executable files.

### Magic Byte Verification

Prevents attackers from uploading `.exe` files renamed to `.jpg`:

```csharp
// Verify file content matches the claimed extension
fileBytes.AgainstFakeMimeType(".jpg", nameof(uploadedFile));

// Also works with streams (reads first 16 bytes, resets position)
fileStream.AgainstFakeMimeType(".png", nameof(uploadedFile));
```

Supported signatures: `.jpg`, `.jpeg`, `.png`, `.gif`, `.pdf`, `.zip`, `.docx`, `.xlsx`, `.exe`, `.dll`, `.bmp`, `.webp`, `.mp3`, `.mp4`, `.svg`.

### Dangerous Extension Blocking

30 dangerous extensions are blocked by default (`.exe`, `.dll`, `.bat`, `.ps1`, `.vbs`, `.sh`, etc.):

```csharp
fileName.AgainstDangerousFileExtension(nameof(fileName));

// Or whitelist only what you allow
fileName.AgainstDisallowedExtension(
    new[] { ".jpg", ".png", ".pdf" }, nameof(fileName));
```

### Size Enforcement

```csharp
fileSize.AgainstOversizedUpload(FileUploadGuards.FileSizeLimits.FiveMB, nameof(file));
fileBytes.AgainstOversizedUpload(FileUploadGuards.FileSizeLimits.TenMB, nameof(file));

// Built-in size constants
// FileUploadGuards.FileSizeLimits.OneKB / OneMB / FiveMB / TenMB /
// TwentyFiveMB / FiftyMB / OneHundredMB / OneGB
```

### Malicious Content Detection

Scans image files for embedded executables, scripts, and PHP payloads:

```csharp
// Detects MZ headers (exe/dll), <script> tags, javascript:, <?php in image files
fileBytes.AgainstMaliciousContent(".jpg", nameof(uploadedFile));
```

### Full Upload Validation Pipeline

```csharp
public void ValidateUpload(string fileName, byte[] content)
{
    var ext = Path.GetExtension(fileName);

    // 1. Block dangerous extensions
    fileName.AgainstDangerousFileExtension(nameof(fileName));

    // 2. Enforce allowed extensions
    fileName.AgainstDisallowedExtension(new[] { ".jpg", ".png", ".pdf" }, nameof(fileName));

    // 3. Block path traversal in filename
    fileName.AgainstUnsafeFileName(nameof(fileName));

    // 4. Verify content matches extension (magic bytes)
    content.AgainstFakeMimeType(ext, nameof(content));

    // 5. Size limit
    content.AgainstOversizedUpload(FileUploadGuards.FileSizeLimits.FiveMB, nameof(content));

    // 6. Scan for embedded malicious content
    content.AgainstMaliciousContent(ext, nameof(content));
}
```

---

## Sensitive Data & Compliance Guards

Guards for detecting PII leakage in logs, API responses, and unencrypted storage. Helps with **GDPR**, **KVKK**, and **PCI-DSS** compliance.

```csharp
// Detect credit card numbers (Luhn validation + BIN prefix matching)
logMessage.AgainstContainsCreditCardNumber(nameof(logMessage));

// Detect email addresses embedded in strings
errorMessage.AgainstContainsEmailAddress(nameof(errorMessage));

// Detect phone numbers
responseBody.AgainstContainsPhoneNumber(nameof(responseBody));

// Detect IP addresses
logEntry.AgainstContainsIpAddress(nameof(logEntry));

// Detect secrets: PEM private keys, AWS access keys, Azure storage keys, Bearer tokens
configDump.AgainstContainsSecret(nameof(configDump));

// All PII checks in one call (credit card + email + phone + secrets)
auditLog.AgainstContainsPii(nameof(auditLog));
```

### What It Detects

| Guard | Detects | Compliance |
|-------|---------|-----------|
| `AgainstContainsCreditCardNumber` | 13-19 digit Luhn-valid sequences with known BIN prefixes (Visa, MC, Amex, Discover, JCB, Diners) | PCI-DSS |
| `AgainstContainsEmailAddress` | Embedded email patterns via GeneratedRegex | GDPR, KVKK |
| `AgainstContainsPhoneNumber` | Embedded phone number patterns via GeneratedRegex | GDPR, KVKK |
| `AgainstContainsIpAddress` | IPv4 addresses | GDPR |
| `AgainstContainsSecret` | PEM keys, AWS `AKIA*` keys, Azure `AccountKey=`, Bearer tokens | Security best practice |
| `AgainstContainsPii` | All of the above combined | GDPR, KVKK, PCI-DSS |

### Typical Use: Log Sanitization Middleware

```csharp
public class PiiSafeLogger : ILogger
{
    public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex, Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, ex);
        message.AgainstContainsPii("logMessage"); // Throws before PII reaches the sink
        _inner.Log(level, id, state, ex, formatter);
    }
}
```

---

## Advanced Validation Patterns

### Deep Nested Object Graphs

Validate arbitrarily nested objects with full property path tracking in error messages:

```csharp
var result = Validate.Nested(order)
    .Property(o => o.OrderNumber, p => p.NotEmpty())
    .Property(o => o.Total, p => p.GreaterThan(0))
    .Nested(o => o.Customer, customer => customer
        .Property(c => c.Name, p => p.NotEmpty().Length(2, 100))
        .Property(c => c.Email, p => p.NotEmpty().Email())
        .Nested(c => c.Address, address => address
            .Property(a => a.Street, p => p.NotEmpty())
            .Property(a => a.City, p => p.NotEmpty())
            .Property(a => a.ZipCode, p => p.NotEmpty().Length(5, 10))))
    .Collection(o => o.Items, (item, index) => item
        .Property(i => i.ProductName, p => p.NotEmpty())
        .Property(i => i.Quantity, p => p.GreaterThan(0))
        .Property(i => i.UnitPrice, p => p.GreaterThan(0)))
    .Must(o => o.Items.Sum(i => i.Quantity * i.UnitPrice) == o.Total,
          "Total", "Order total must match sum of line items")
    .When(o => o.ShippingMethod == "Express", express => express
        .Property(o => o.ExpressDeliveryAddress, p => p.NotNull()))
    .ToResult();

// Error paths: "Customer.Address.City", "Items[2].ProductName", "Items[5].Quantity"
```

### Cross-Property Validation

Rules that span multiple properties on the same object:

```csharp
var result = Validate.CrossProperties(booking)
    .IsGreaterThan(b => b.EndDate, b => b.StartDate, "End date must be after start date")
    .AreNotEqual(b => b.Email, b => b.Username, "Email and username must differ")
    .AreEqual(b => b.Password, b => b.ConfirmPassword, "Passwords must match")
    .IsLessThan(b => b.DiscountAmount, b => b.TotalPrice, "Discount cannot exceed total")
    .AtLeastOneRequired(b => b.Phone, b => b.Email)
    .When(b => b.IsInternational, v => v
        .Must(b => !string.IsNullOrEmpty(b.PassportNumber),
              "PassportNumber", "Passport required for international bookings"))
    .ToResult();
```

### Polymorphic Validation

Type-discriminated validation for inheritance hierarchies -- common in payment processing, notification systems, and event sourcing:

```csharp
var validator = Validate.Polymorphic<Payment>()
    .When<CreditCardPayment>(p => Validate.Nested(p)
        .Property(x => x.CardNumber, v => v.NotEmpty().Length(16, 16))
        .Property(x => x.Cvv, v => v.NotEmpty().Length(3, 4))
        .Property(x => x.ExpiryMonth, v => v.InRange(1, 12))
        .ToResult())
    .When<BankTransferPayment>(p => Validate.Nested(p)
        .Property(x => x.Iban, v => v.NotEmpty())
        .Property(x => x.SwiftCode, v => v.NotEmpty())
        .ToResult())
    .When<CryptoPayment>(p => Validate.Nested(p)
        .Property(x => x.WalletAddress, v => v.NotEmpty().MinLength(26))
        .ToResult())
    .Otherwise(p => GuardResult.Failure("Payment", "Unsupported payment type."));

var result = validator.Validate(incomingPayment);    // Returns GuardResult
validator.ValidateAndThrow(incomingPayment);         // Throws if invalid
```

### Dynamic Rule Engine

Load validation rules from JSON at runtime. Rules can be stored in a database, config file, feature flag service, or CMS -- no redeployment required.

```csharp
var json = """
{
  "Name": "CreateUser",
  "Rules": [
    { "PropertyName": "Email", "RuleType": "NotEmpty" },
    { "PropertyName": "Email", "RuleType": "Email" },
    { "PropertyName": "Age", "RuleType": "Range", "Parameters": { "Min": 18, "Max": 120 } },
    { "PropertyName": "Country", "RuleType": "In", "Parameters": { "Values": ["US","UK","TR","DE"] } },
    { "PropertyName": "Password", "RuleType": "MinLength", "Parameters": { "Min": 8 },
      "WhenProperty": "IsNewUser", "WhenValue": true }
  ]
}
""";

var validator = DynamicValidator.FromJson(json);
var result = validator.Validate(userDto);

// Or from config object
var validator = DynamicValidator.FromConfig(config, "CreateUser");
```

**14 built-in rule types:** `NotNull`, `NotEmpty`, `Length`, `MinLength`, `MaxLength`, `Range`, `Email`, `Regex`, `In`, `NotIn`, `GreaterThan`, `LessThan`, `Equal`, `NotEqual`.

**Use cases:** Multi-tenant SaaS with per-tenant validation rules, A/B testing validation strictness, compliance rules that change by jurisdiction.

### RuleSets

Group validation rules by operation. Execute only the relevant subset:

```csharp
public class UserValidator : AbstractValidator<User>
{
    public UserValidator(IUserRepository userRepo)
    {
        // Default -- always runs
        RuleFor(u => u.Email, "Email", p => p.NotEmpty().Email());

        RuleSet("create", () =>
        {
            RuleFor(u => u.Password, "Password", p => p.NotEmpty().Length(8, 100));
            RuleFor(u => u.AcceptedTerms, "Terms", p => p.Must(v => v, "Must accept terms"));
            RuleForAsync(
                async x => await userRepo.IsEmailUniqueAsync(x.Email),
                "Email already registered", "Email");
        });

        RuleSet("update", () =>
        {
            RuleFor(u => u.Id, "Id", p => p.NotNull());
        });

        RuleSet("delete", () =>
        {
            RuleFor(u => u.Id, "Id", p => p.NotNull());
            RuleFor(u => u.DeletionReason, "DeletionReason", p => p.NotEmpty().MinLength(10));
        });
    }
}

// Execute selectively
validator.Validate(user, RuleSet.Default, RuleSet.Create);
validator.Validate(user, RuleSet.Default, RuleSet.Update);

// Async
await validator.ValidateAsync(user, cancellationToken, RuleSet.Create);

// Discover available rule sets
IReadOnlyCollection<string> names = validator.GetRuleSetNames();
```

### Source Generator (NativeAOT / Reflection-Free)

Compile-time validator generation. Zero reflection, zero startup cost, fully NativeAOT compatible:

```csharp
[GenerateValidator]
public sealed class CreateUserRequest
{
    [NotNull, NotEmpty, Length(3, 50)]
    public string Name { get; set; }

    [NotNull, Email]
    public string Email { get; set; }

    [Range(13, 120)]
    public int Age { get; set; }

    [Positive]
    public decimal Balance { get; set; }

    [Regex(@"^\+?[1-9]\d{1,14}$")]
    public string? Phone { get; set; }
}

// Auto-generated at compile time:
var result = CreateUserRequestValidator.Validate(request);
```

The generator emits a `partial class` with a static `Validate` method. Roslyn analyzers are included to catch common mistakes (e.g., `[Range]` on a `string` property).

Available attributes: `[NotNull]`, `[NotEmpty]`, `[Length(min, max)]`, `[Email]`, `[Range(min, max)]`, `[Regex(pattern)]`, `[Positive]`.

---

## Startup & Configuration Guards

Fail-fast guards for application bootstrap. Call during `Program.cs` / `Startup.cs` to detect misconfiguration before the first request.

```csharp
// Validate required environment variables -- throws with ALL missing vars listed
ConfigurationGuards.AgainstMissingEnvVars(
    "DATABASE_URL", "JWT_SECRET", "REDIS_CONNECTION", "SMTP_HOST");

// Validate individual env vars with type checking
string dbUrl    = ConfigurationGuards.AgainstMissingEnvVar("DATABASE_URL");
string jwtKey   = ConfigurationGuards.AgainstWeakEnvVar("JWT_SECRET", minLength: 32);
string connStr  = ConfigurationGuards.AgainstInvalidConnectionStringEnv("DATABASE_URL");
Uri apiEndpoint = ConfigurationGuards.AgainstInsecureUrl("PAYMENT_API_URL");  // Must be HTTPS
int port        = ConfigurationGuards.AgainstInvalidPort("APP_PORT");

// Certificate existence check
ConfigurationGuards.AgainstMissingCertificate("/certs/server.pfx", "TLS Certificate");

// Environment safety -- prevent debug config from running in production
ConfigurationGuards.AgainstEnvironment("Production");  // Throws if running in Prod
ConfigurationGuards.AgainstUnexpectedEnvironment("Development", "Staging"); // Whitelist
```

### Why This Matters

A missing environment variable discovered at 3 AM in production is a page. The same issue discovered at deploy time is a CI failure. These guards shift configuration errors left.

---

## API Contract Validation

Validate API responses and data contracts for structural correctness before processing.

```csharp
// Validate required fields exist and are non-null
response.AgainstMissingRequiredFields("response", "Id", "Name", "Email", "CreatedAt");

// Validate no unexpected nulls
apiResult.AgainstUnexpectedNullFields("result", "CustomerId", "OrderTotal");

// Validate JSON structure before deserialization
jsonString.AgainstMissingJsonFields("apiResponse", "data", "status", "timestamp");

// Validate API response envelope
jsonString.AgainstInvalidApiResponse("response", requireDataField: true);
// Checks for "data"/"Data"/"result"/"Result" field in JSON object

// Validate collection response bounds
items.AgainstEmptyApiResponse("searchResults", maxExpected: 1000);

// Validate schema types
response.AgainstSchemaViolation("response",
    ("Id", typeof(int)),
    ("Name", typeof(string)),
    ("CreatedAt", typeof(DateTime)));
```

---

## Idempotency Guard

Detect and prevent duplicate operation execution. Thread-safe with configurable TTL and bounded size.

```csharp
// Singleton -- default 1 hour TTL
var guard = IdempotencyGuard.Default;

// Throw if already processed
guard.AgainstDuplicateOperation(requestId, nameof(requestId));

// Async version
await guard.AgainstDuplicateOperationAsync(paymentId, nameof(paymentId));

// Check without throwing
if (guard.IsProcessed(operationId))
    return Conflict("Already processed");

// Manual lifecycle
guard.MarkProcessed(operationId);
guard.Reset(operationId);
guard.Clear();

// TryProcess pattern -- atomic check-and-mark
if (!guard.TryProcess(requestId))
    return Conflict("Duplicate request");

// Custom TTL and max size
var customGuard = new IdempotencyGuard(
    ttl: TimeSpan.FromMinutes(30),
    maxSize: 10_000);
```

---

## Ecosystem & Integrations

### 9 Packages -- One Validation Model

Every package produces or consumes `GuardResult`. Pick only what your stack needs:

| Package | Purpose |
|---------|---------|
| **OrionGuard** | Core: guard clauses, result pattern, validators, profiles, security, localization, file upload, PII detection, configuration guards, idempotency |
| **OrionGuard.AspNetCore** | Middleware, `[ValidateRequest]` attribute, `.WithValidation<T>()` Minimal API filter, RFC 9457 ProblemDetails, IOptions validation, IExceptionHandler, health check |
| **OrionGuard.MediatR** | `ValidationBehavior<TRequest, TResponse>` pipeline with assembly scanning |
| **OrionGuard.Generators** | `[GenerateValidator]` source generator + Roslyn analyzers |
| **OrionGuard.Swagger** | `OrionGuardSchemaFilter` -- auto-generates OpenAPI `minLength`, `maximum`, `pattern` from validation attributes |
| **OrionGuard.OpenTelemetry** | `InstrumentedValidator<T>` -- validation count/failure/duration metrics + distributed tracing spans |
| **OrionGuard.Blazor** | `<OrionGuardValidator />` and `<OrionGuardFluentValidator />` EditForm components |
| **OrionGuard.Grpc** | `OrionGuardInterceptor` server interceptor with streaming support |
| **OrionGuard.SignalR** | `OrionGuardHubFilter` for hub method parameter validation |

### ASP.NET Core -- Full Stack Integration

```csharp
// Program.cs
builder.Services.AddOrionGuardAspNetCore(options =>
{
    options.UseProblemDetails = true;            // RFC 9457 responses
    options.DefaultStatusCode = 422;             // Unprocessable Entity
    options.SuppressModelStateInvalidFilter = true;
});

// Minimal API -- automatic request validation
app.MapPost("/api/users", (CreateUserRequest req) => { /* validated */ })
   .WithValidation<CreateUserRequest>();

// MVC -- attribute-based
[ValidateRequest]
public IActionResult Create([FromBody] CreateUserRequest request) { ... }

// IOptions -- fail at startup, not at runtime
builder.Services.AddOptions<AppSettings>()
    .BindConfiguration("App")
    .ValidateWithOrionGuardOnStart();

// Health check
builder.Services.AddHealthChecks().AddOrionGuardCheck();

// Exception handler -- converts GuardException to ProblemDetails automatically
// (registered by AddOrionGuardAspNetCore)
```

### MediatR -- CQRS Pipeline

```csharp
// One line -- scans assembly for all IValidator<TRequest> implementations
builder.Services.AddOrionGuardMediatR(typeof(Program).Assembly);

// Your validator -- automatically runs before the handler
public class CreateUserValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserValidator(IUserRepository userRepo)
    {
        RuleFor(x => x.Email, "Email", v => v.NotEmpty().Email());
        RuleFor(x => x.Password, "Password", v => v.NotEmpty().MinLength(8));
        RuleForAsync(
            async x => await userRepo.IsEmailUniqueAsync(x.Email),
            "Email already registered", "Email");
    }
}
// Handler only executes if validation passes -- zero validation code in handlers.
```

### Blazor -- EditForm Integration

```razor
<EditForm Model="@user" OnValidSubmit="HandleSubmit">
    <OrionGuardValidator />
    <InputText @bind-Value="user.Name" />
    <ValidationMessage For="() => user.Name" />
    <InputText @bind-Value="user.Email" />
    <ValidationMessage For="() => user.Email" />
    <button type="submit">Register</button>
</EditForm>
```

### gRPC & SignalR

```csharp
// gRPC -- validates protobuf messages before they reach your service implementation
services.AddOrionGuardGrpc();
services.AddGrpc(o => o.Interceptors.Add<OrionGuardInterceptor>());

// SignalR -- validates hub method parameters automatically
services.AddOrionGuardSignalR();
```

### Dependency Injection

```csharp
// Register core + all validators in assembly
builder.Services.AddOrionGuard(registry =>
{
    registry.Register<CreateUserRequest, CreateUserRequestValidator>();
    registry.Register<UpdateUserRequest, UpdateUserRequestValidator>();
});

// Or register individually
builder.Services.AddValidator<CreateUserRequest, CreateUserRequestValidator>();

// Custom exception factory
builder.Services.AddOrionGuardExceptionFactory<DomainExceptionFactory>();

// Inject and use
public class UsersController(IValidator<CreateUserRequest> validator)
{
    [HttpPost]
    public async Task<IActionResult> Create(CreateUserRequest request)
    {
        var result = await validator.ValidateAsync(request);
        if (result.IsInvalid)
            return BadRequest(result.ToErrorDictionary());
        // ...
    }
}
```

---

## Roslyn Analyzer (OG0001)

`Moongazing.OrionGuard.Generators` ships with a diagnostic analyzer that flags public
properties on `[GenerateValidator]` types that lack any validation attribute. Unvalidated
input silently accepted by the generated validator is a common source of production bugs;
OG0001 makes it visible at compile time.

```csharp
[GenerateValidator]
public sealed class CreateUserRequest
{
    [NotNull, Email]
    public string Email { get; set; }

    public string Name { get; set; }      // warning OG0001: Property 'Name' on 'CreateUserRequest'
                                          // has no OrionGuard validation attribute

    public int Age { get; set; }          // warning OG0001
}
```

| Property | Value |
|----------|-------|
| ID | `OG0001` |
| Category | `Usage` |
| Severity | `Warning` (override via `.editorconfig`) |
| Scope | Types decorated with `[GenerateValidator]` only |
| Candidates | Public instance properties with a public getter |
| Recognised attributes | Anything in `Moongazing.OrionGuard.Attributes` |

**Opt-in via `[GenerateValidator]`.** The analyzer intentionally does not nag
general-purpose DTOs -- it only activates on types you explicitly marked as validator
targets. This keeps it quiet on models shared with serializers, ORMs, or UI frameworks.

**Suppress per-property.** Use `#pragma warning disable OG0001` or suppress in
`.editorconfig`:

```ini
[*.cs]
# Allow unvalidated audit fields
dotnet_diagnostic.OG0001.severity = none
```

---

## Observability & Diagnostics

### OpenTelemetry Integration

Wrap any validator with metrics and distributed tracing:

```csharp
builder.Services.AddOrionGuardOpenTelemetry();
```

`InstrumentedValidator<T>` emits:

| Metric | Type | Description |
|--------|------|-------------|
| `orionguard.validations.total` | Counter | Total validation executions |
| `orionguard.validations.failures` | Counter | Failed validations |
| `orionguard.validations.duration` | Histogram | Validation execution time |

Plus a distributed tracing span per validation call, tagged with validator type and result status. Feed this into Grafana, Datadog, or any OTLP-compatible backend.

### Swagger / OpenAPI

```csharp
builder.Services.AddOrionGuardSwagger();
```

`OrionGuardSchemaFilter` reads your validation attributes and generates OpenAPI schema constraints (`minLength`, `maxLength`, `minimum`, `maximum`, `format: email`). Frontend teams and API consumers get accurate constraints without manual documentation.

---

## Internationalization

### 14 Languages -- 420 Messages

| | | | |
|---|---|---|---|
| English (`en`) | French (`fr`) | Italian (`it`) | Korean (`ko`) |
| Turkish (`tr`) | Spanish (`es`) | Chinese (`zh`) | Russian (`ru`) |
| German (`de`) | Portuguese (`pt`) | Japanese (`ja`) | Dutch (`nl`) |
| Arabic (`ar`) | Polish (`pl`) | | |

Thread-safe with per-request culture scoping via `AsyncLocal<CultureInfo>`:

```csharp
// Global
ValidationMessages.SetCulture("tr");

// Per-request scope (safe in concurrent web apps)
ValidationMessages.SetCultureForCurrentScope(new CultureInfo("de-DE"));

// Retrieve localized message
var msg = ValidationMessages.Get("NotNull", "Email");
// de: "Email darf nicht null sein."
// tr: "Email bos olamaz."
// zh: "Email 不能为空。"
// ja: "Email は null にできません。"
// ar: ".Email لا يمكن أن يكون فارغًا"

// With CultureInfo overload
var msg = ValidationMessages.Get("Email", new CultureInfo("fr"), "E-mail");
```

### Custom Message Resolver

Integrate with your existing localization infrastructure:

```csharp
// Use ASP.NET Core IStringLocalizer
ValidationMessages.SetMessageResolver((key, culture) =>
    _localizer[key].Value);

// Or add your own language pack
ValidationMessages.AddMessages("vi", new Dictionary<string, string>
{
    ["NotNull"] = "{0} khong duoc de trong.",
    ["Email"] = "{0} phai la dia chi email hop le.",
    // ... 30 keys
});
```

---

## Domain-Specific Guards

### 130+ Extension Methods Across 19 Guard Classes

OrionGuard ships with extension method guards organized by domain:

| Guard Class | Domain | Example Methods |
|-------------|--------|----------------|
| `StringGuards` | String validation | `AgainstNullOrEmpty`, `AgainstInvalidLength`, `AgainstRegexMismatch`, `AgainstNonPalindrome`, `AgainstContainingWhitespace`, `AgainstNonEmojiCharacters` |
| `NumericGuards` | Numeric validation | `AgainstNegative`, `AgainstZero`, `AgainstOutOfRange`, `AgainstNonInteger`, `AgainstCustomCondition` |
| `CollectionGuards` | Collection validation | `AgainstNullOrEmpty`, `AgainstExceedingCount`, `AgainstNullItems` |
| `DateTimeGuards` | DateTime validation | `AgainstPastDate`, `AgainstFutureDate`, `AgainstWeekend`, `AgainstTimeRange`, `AgainstNonToday` |
| `BooleanGuards` | Boolean checks | `AgainstFalse`, `AgainstTrue` |
| `FileGuards` | File system | `AgainstEmptyFile`, `AgainstInvalidFileExtension`, `AgainstFileNotExists` |
| `NetworkGuards` | Network | `AgainstInvalidIpAddress`, `AgainstInvalidPort` |
| `ObjectGuards` | Object state | `AgainstNull`, `AgainstUninitializedProperties` (with PropertyCache) |
| `EnvironmentGuards` | Environment | `AgainstMissingEnvironmentVariable` |
| `SecurityGuards` | Injection attacks | SQL, XSS, Path Traversal, Command, LDAP, XXE, Redirect |
| `SensitiveDataGuards` | PII / compliance | Credit card, email, phone, IP, secrets, composite PII |
| `FileUploadGuards` | Upload security | Magic bytes, dangerous extensions, size, malicious content |
| `FormatGuards` | Universal formats | Geo coordinates, MAC, hostname, CIDR, country codes, timezone, language tags, JWT, connection strings, Base64 |
| `InternationalGuards` | International IDs | SWIFT/BIC, ISBN-10/13, VIN, EAN-13, EU VAT, IMEI |
| `BusinessGuards` | Business logic | Money, currency, percentage, SKU, coupon, minimum order, quantity, business hours, rating, review, subscription, status transition, expiration |
| `AdvancedStringGuards` | Complex formats | Credit card (Luhn), IBAN, Turkish ID, JSON, XML, Base64, Hex color, SemVer, URL slug, phone (E.164) |
| `RateLimitGuards` | Rate limiting | Rate limit, too many requests, sliding window, concurrent limit, daily quota |
| `ConfigurationGuards` | Startup config | Env vars, connection strings, certificates, HTTPS URLs, ports, environments |
| `ApiContractGuards` | API contracts | Required fields, null fields, JSON fields, API response envelope, schema validation |

### Financial & International Identifiers

```csharp
// Payment card validation (Luhn algorithm)
"4111111111111111".AgainstInvalidCreditCard("card");
"5500000000000004".AgainstInvalidMasterCard("masterCard");
"DE89370400440532013000".AgainstInvalidIban("iban");
"DEUTDEFF".AgainstInvalidSwiftCode("swift");

// International identifiers with checksum verification
"978-3-16-148410-0".AgainstInvalidIsbn("isbn");     // ISBN-10/13 with check digit
"1HGCM82633A004352".AgainstInvalidVin("vin");       // 17-char, no I/O/Q
"4006381333931".AgainstInvalidEan("ean");            // EAN-13 modulo 10
"DE123456789".AgainstInvalidVatNumber("vat");        // EU VAT format
"490154203237518".AgainstInvalidImei("imei");        // 15-digit Luhn
"10000000146".AgainstInvalidTurkishId("tcKimlik");   // TC Kimlik No
```

### Common Profiles

Pre-built, configurable validation profiles for recurring patterns:

```csharp
CommonProfiles.Email("user@example.com");
CommonProfiles.Password("SecureP@ss1",
    minLength: 8, maxLength: 128,
    requireUppercase: true, requireLowercase: true,
    requireDigit: true, requireSpecialChar: true);
CommonProfiles.Username("john_doe", minLength: 3, maxLength: 30);
CommonProfiles.PhoneNumber("+905551234567");
CommonProfiles.BirthDate(birthDate, minAge: 18, maxAge: 120);
CommonProfiles.PersonName("John Doe", minLength: 2, maxLength: 100);
CommonProfiles.MonetaryAmount(99.99m, min: 0, max: 10000);
CommonProfiles.Percentage(15.5m);
CommonProfiles.Slug("my-article-title");
CommonProfiles.GuidId(userId);
CommonProfiles.IntegerId(productId);
CommonProfiles.Url("https://example.com");
CommonProfiles.NonEmptyList(items);
CommonProfiles.ListWithCount(items, minCount: 1, maxCount: 100);
```

### Custom Guard Profiles

Register reusable validation logic:

```csharp
GuardProfileRegistry.Register<string>("TR_Phone", value =>
    Ensure.That(value).NotNull().NotEmpty().Matches(@"^0[2-5]\d{9}$"));

// Use anywhere
GuardProfileRegistry.Execute("TR_Phone", phoneNumber);

// Safe check
if (GuardProfileRegistry.IsRegistered("TR_Phone"))
    GuardProfileRegistry.TryExecute("TR_Phone", phoneNumber);

// Lifecycle
GuardProfileRegistry.Remove("TR_Phone");
```

---

## Extensibility

### Custom Exception Factory

Replace the default exception types with your domain-specific exceptions:

```csharp
public class DomainExceptionFactory : IExceptionFactory
{
    public Exception CreateException(string errorCode, string parameterName, string message, Exception? inner)
        => new DomainValidationException(errorCode, parameterName, message);
}

// Register globally
ExceptionFactoryProvider.Configure(new DomainExceptionFactory());

// Or via DI
services.AddOrionGuardExceptionFactory<DomainExceptionFactory>();
```

### Validation Caching

For high-throughput scenarios where the same input is validated repeatedly:

```csharp
var cachedValidator = validator.WithCaching(ttl: TimeSpan.FromMinutes(5));
var result = cachedValidator.Validate(request);

// Cache management
cachedValidator.ClearCache();
int size = cachedValidator.CacheSize;
```

### Custom Validator with DI

```csharp
public class OrderValidator : AbstractValidator<CreateOrderCommand>
{
    public OrderValidator(IProductRepository products, IDiscountService discounts)
    {
        RuleFor(x => x.Items, "Items", v => v.NotNull().MinCount(1));
        RuleFor(x => x.CustomerId, "CustomerId", v => v.NotNull());

        RuleForAsync(
            async x => await products.AllExistAsync(x.Items.Select(i => i.ProductId)),
            "One or more products do not exist", "Items");

        RuleSet("premium", () =>
        {
            RuleForAsync(
                async x => await discounts.IsEligibleAsync(x.CustomerId),
                "Customer not eligible for premium pricing", "CustomerId");
        });
    }
}
```

---

## Exception Architecture

OrionGuard provides **32 sealed exception types**, each with `ErrorCode` and `ParameterName` properties for structured error handling:

| Category | Exceptions |
|----------|-----------|
| **Null / Empty** | `NullValueException`, `EmptyStringException`, `EmptyFileException` |
| **Numeric** | `NegativeException`, `NegativeDecimalException`, `ZeroValueException`, `OutOfRangeException`, `GreaterThanException`, `LessThanException` |
| **Format** | `InvalidEmailException`, `InvalidUrlException`, `InvalidIpException`, `InvalidGuidException`, `InvalidXmlException`, `RegexMismatchException`, `InvalidEnumValueException`, `InvalidFileExtensionException` |
| **Date** | `PastDateException`, `FutureDateException`, `WeekendException`, `SpecifyDayException`, `UnrealisticBirthDateException` |
| **Boolean** | `TrueException`, `FalseException` |
| **Security** | `CharactersOutsideSetException`, `OnlyAlphanumericCharacterException`, `WeakPasswordException` |
| **Collection** | `ExceedingCountException` |
| **File** | `FileNotExistsException` |
| **Object** | `UninitializedPropertyException` |
| **Rate Limit** | `RateLimitExceededException` |
| **Aggregate** | `AggregateValidationException` (contains `IReadOnlyList<ValidationError>`) |

Base class: `GuardException` with `ErrorCode` and `ParameterName`.

All exceptions are `sealed` for JIT optimization and use `[StackTraceHidden]` on throw helpers for clean stack traces.

---

## Thread Safety Guarantees

| Component | Mechanism | Safe for Concurrent Use? |
|-----------|-----------|:------------------------:|
| `FastGuard` | Static methods, no state | Yes |
| `Ensure.That()` | Creates new `FluentGuard<T>` per call | Yes |
| `RegexCache` | `ConcurrentDictionary` + bounded eviction | Yes |
| `ValidationMessages` | `ConcurrentDictionary` + `AsyncLocal<CultureInfo>` | Yes |
| `GuardProfileRegistry` | `ConcurrentDictionary` | Yes |
| `IdempotencyGuard` | `ConcurrentDictionary` | Yes |
| `FrozenSet` / `FrozenDictionary` | Immutable after creation | Yes |
| `ObjectValidator<T>` expression cache | `ConcurrentDictionary` | Yes |
| `CachedValidator<T>` | `ConcurrentDictionary` + TTL | Yes |
| `PropertyCache<T>` | Static generic class (JIT-guaranteed once) | Yes |
| `AbstractValidator<T>` | Instance per scope, rule definitions immutable after ctor | Yes |

Per-request culture scoping uses `AsyncLocal<CultureInfo>` -- each request/thread gets its own culture without affecting others.

---

## Real-World Architecture Scenarios

### Scenario 1: Clean Architecture CQRS API

```text
Request --> ASP.NET Core Middleware (OrionGuard.AspNetCore)
         --> MediatR Pipeline (OrionGuard.MediatR)
         --> Command Handler (business logic only)
         --> Response
```

```csharp
// Program.cs
builder.Services.AddOrionGuardAspNetCore();
builder.Services.AddOrionGuardMediatR(typeof(Program).Assembly);

// Handler -- no validation code, guaranteed valid input
public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, OrderResult>
{
    public async Task<OrderResult> Handle(CreateOrderCommand request, CancellationToken ct)
    {
        // request is guaranteed valid -- focus on business logic
    }
}
```

### Scenario 2: Multi-Tenant SaaS with Dynamic Rules

```csharp
// Load tenant-specific rules from database
var tenantRules = await db.GetValidationRulesAsync(tenantId, "CreateUser");
var validator = DynamicValidator.FromJson(tenantRules);
var result = validator.Validate(userDto);

// Tenant A: requires phone number, min age 18
// Tenant B: phone optional, min age 13
// No redeployment needed when rules change
```

### Scenario 3: Secure File Upload Endpoint

```csharp
app.MapPost("/api/upload", async (IFormFile file) =>
{
    var name = file.FileName;
    var bytes = await file.OpenReadStream().ReadAllBytesAsync();
    var ext = Path.GetExtension(name);

    name.AgainstDangerousFileExtension(nameof(name));
    name.AgainstUnsafeFileName(nameof(name));
    name.AgainstDisallowedExtension(new[] { ".jpg", ".png", ".pdf" }, nameof(name));
    bytes.AgainstFakeMimeType(ext, nameof(bytes));
    bytes.AgainstOversizedUpload(FileUploadGuards.FileSizeLimits.FiveMB, nameof(bytes));
    bytes.AgainstMaliciousContent(ext, nameof(bytes));

    // File is safe to store
}).WithValidation<UploadRequest>();
```

### Scenario 4: Startup Configuration Validation

```csharp
// Program.cs -- fail immediately if misconfigured
ConfigurationGuards.AgainstMissingEnvVars(
    "DATABASE_URL", "JWT_SECRET", "REDIS_URL", "SMTP_HOST");

var jwtSecret = ConfigurationGuards.AgainstWeakEnvVar("JWT_SECRET", minLength: 32);
var dbConn = ConfigurationGuards.AgainstInvalidConnectionStringEnv("DATABASE_URL");
var apiUrl = ConfigurationGuards.AgainstInsecureUrl("PAYMENT_API_URL");
ConfigurationGuards.AgainstMissingCertificate("/certs/tls.pfx", "TLS");

// In staging, prevent production config from leaking
if (builder.Environment.IsStaging())
    ConfigurationGuards.AgainstEnvironment("Production");
```

### Scenario 5: PII-Safe Logging

```csharp
public class SafeLoggingMiddleware(RequestDelegate next, ILogger<SafeLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try { await next(context); }
        catch (Exception ex)
        {
            var message = ex.Message;
            message.AgainstContainsPii("errorMessage"); // Block PII from reaching logs
            logger.LogError(ex, "Request failed: {Message}", message);
            throw;
        }
    }
}
```

### Scenario 6: Full-Stack Blazor with Shared Validators

```csharp
// Shared project -- validators used on both client and server
public class RegistrationValidator : AbstractValidator<RegistrationModel> { ... }

// Server -- MediatR pipeline validates automatically
builder.Services.AddOrionGuardMediatR(typeof(Program).Assembly);

// Blazor Client -- EditForm validates in the browser
<EditForm Model="@model" OnValidSubmit="Submit">
    <OrionGuardValidator />
    ...
</EditForm>
// Same validation rules, same error messages, client + server.
```

---

## Migration Path

### From FluentValidation

Drop-in compatibility layer:

```csharp
// Before
using FluentValidation;

// After
using Moongazing.OrionGuard.Compatibility;

// Your existing code works as-is
public class UserValidator : FluentStyleValidator<User>
{
    public UserValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Age).InclusiveBetween(18, 120);
    }
}
```

Then gradually adopt native OrionGuard APIs at your own pace.

### From Ardalis.GuardClauses / Dawn.Guard

Replace `Guard.Against.Null(value)` with `Ensure.That(value).NotNull()` or `FastGuard.NotNull(value, nameof(value))`. The fluent API is a superset.

### Legacy OrionGuard v3

The `Guard.*()` static API is fully backward compatible:

```csharp
Guard.AgainstNull(value, nameof(value));
Guard.AgainstNullOrEmpty(text, nameof(text));
Guard.AgainstOutOfRange(age, 18, 60, nameof(age));
Guard.For(email, "email").NotNull().NotEmpty().Matches(@"^[^@\s]+@[^@\s]+\.[^@\s]+$");
```

---

## Performance Benchmarks

A full benchmark suite is included using BenchmarkDotNet:

```bash
dotnet run -c Release --project benchmarks/Moongazing.OrionGuard.Benchmarks
```

| Operation | OrionGuard Approach | Advantage |
|-----------|-------------------|-----------|
| Null check | `ThrowHelper` + `[StackTraceHidden]` | Smaller JIT body, faster unwind |
| Email validation | Span-based parsing (FastGuard) | Zero allocation vs regex match |
| Range check | Branchless unsigned arithmetic | Single comparison instruction |
| Security patterns | `FrozenSet` O(1) lookup | Immutable, no lock contention |
| Object validation | Compiled expression cache | One-time `Expression.Compile()` per type |
| Source-generated | Zero-reflection compile-time code | No startup cost, NativeAOT compatible |
| Regex patterns | `[GeneratedRegex]` source-generated | No runtime JIT, AOT safe |

---

## Competitive Analysis

| Capability | OrionGuard | FluentValidation | Ardalis.GuardClauses | Dawn.Guard |
|------------|:----------:|:----------------:|:--------------------:|:----------:|
| Fluent guard clauses | Yes | -- | Yes | Yes |
| Object graph validation | Yes | Yes | -- | -- |
| Deep nested validation | Yes | Yes | -- | -- |
| Cross-property rules | Yes | Yes | -- | -- |
| Polymorphic validation | Yes | Yes | -- | -- |
| Result pattern (error accumulation) | Yes | Yes | -- | -- |
| Async validation | Yes | Yes | -- | -- |
| ASP.NET Core middleware | Yes | Yes | -- | -- |
| Minimal API filters | Yes | Yes | -- | -- |
| MediatR pipeline | Yes | Yes | -- | -- |
| Source generators (NativeAOT) | Yes | -- | -- | -- |
| Blazor EditForm | Yes | Yes | -- | -- |
| gRPC interceptor | Yes | -- | -- | -- |
| SignalR hub filter | Yes | -- | -- | -- |
| OpenTelemetry metrics + tracing | Yes | -- | -- | -- |
| OpenAPI schema generation | Yes | -- | -- | -- |
| Security guards (SQL/XSS/injection) | Yes | -- | -- | -- |
| **File upload security (magic bytes)** | Yes | -- | -- | -- |
| **PII / compliance detection** | Yes | -- | -- | -- |
| **API contract validation** | Yes | -- | -- | -- |
| **Startup config guards** | Yes | -- | -- | -- |
| **Idempotency guard** | Yes | -- | -- | -- |
| Dynamic JSON rule engine | Yes | -- | -- | -- |
| Span-based zero-alloc guards | Yes | -- | -- | -- |
| GeneratedRegex (NativeAOT) | Yes | -- | -- | -- |
| 14-language localization | Yes | 20+ | -- | -- |
| Validation result caching | Yes | -- | -- | -- |
| Rate limit guards | Yes | -- | -- | -- |
| Code contracts | Yes | -- | -- | -- |
| Debug-only assertions | Yes | -- | -- | -- |
| Custom exception factory | Yes | -- | -- | -- |
| 32 typed exception classes | Yes | -- | -- | -- |
| FluentValidation migration layer | Yes | N/A | -- | -- |
| Business domain guards | Yes | -- | -- | -- |
| International format guards | Yes | -- | -- | -- |
| Backward-compatible legacy API | Yes | N/A | N/A | N/A |
| **Contextual validation (`ValidationContext`)** | Yes | Yes | -- | -- |
| **Multi-severity (Error/Warning/Info)** | Yes | Yes | -- | -- |
| **Validator composition (`Include`)** | Yes | Yes | -- | -- |
| **Parallel async rules (`Task.WhenAll`)** | Yes | -- | -- | -- |
| **Delta / PATCH validation** | Yes | -- | -- | -- |
| **Roslyn analyzer (OG0001)** | Yes | -- | -- | -- |

---

## Quick Start

```bash
dotnet add package OrionGuard
```

```csharp
using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.Extensions;

// Guard clause -- throws on invalid
Ensure.That(email).NotNull().NotEmpty().Email();

// High performance -- zero allocation
FastGuard.NotNullOrEmpty(name, nameof(name));

// Security -- one-call injection defense
userInput.AgainstInjection(nameof(userInput));

// File upload safety
fileBytes.AgainstFakeMimeType(".jpg", nameof(file));

// PII detection
logMessage.AgainstContainsPii(nameof(logMessage));

// Result pattern -- collect all errors
var result = GuardResult.Combine(
    Ensure.Accumulate(email, "Email").NotNull().Email().ToResult(),
    Ensure.Accumulate(password, "Password").MinLength(8).ToResult()
);
if (result.IsInvalid)
    return BadRequest(result.ToErrorDictionary());
```

---

**License:** MIT
**Author:** Tunahan Ali Ozturk -- [GitHub](https://github.com/tunahanaliozturk)
**Repository:** [github.com/tunahanaliozturk/OrionGuard](https://github.com/tunahanaliozturk/OrionGuard)
