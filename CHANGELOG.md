# Changelog

All notable changes to OrionGuard will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- `OrionGuard`: `ValidatorInvoker.ValidateAsync(IServiceProvider, object, ValidationContext?, CancellationToken)`
  in `Moongazing.OrionGuard.DependencyInjection` runs every `IValidator<T>` registered for an object's runtime
  type through `ValidateAsync` and returns the combined result, or `null` when no validator is registered. The
  SignalR, gRPC, MVC, and Hangfire integrations now share it.
- `OrionGuard.MediatR`: `StreamValidationBehavior<TRequest, TResponse>`, an `IStreamPipelineBehavior<,>` that
  validates stream requests.
- **New package `OrionGuard.MassTransit`.** `cfg.UseOrionGuardValidation(context)` adds
  `OrionGuardConsumeFilter<TMessage>` as a scoped consume filter, on the bus or on one receive endpoint. It runs
  every `IValidator<TMessage>` registered for the consumed type from the consume scope, and throws
  `MessageValidationException` (all errors in `Errors`) before the consumer runs, so the message goes through
  MassTransit's retry and error pipeline to the `_error` queue. Add `r.Ignore<MessageValidationException>()` to
  the retry policy, because a validation failure fails the same way on every retry. Built against MassTransit
  **8.5.10**, the last Apache-2.0 line; MassTransit 9 requires a commercial license.

### Changed

- **Dependencies refreshed across the solution.** Package versions are bumped in the release PR.
  - `Microsoft.Extensions.*` → **10.0.12** in every shipped package, including net8.0/net9.0 targets.
  - `OrionGuard.EntityFrameworkCore`: EF Core **10.0.12** on net10.0, **9.0.20** on net8.0/net9.0
    (EF Core 10 only supports net10.0).
  - `OrionGuard.Swagger`: `Swashbuckle.AspNetCore.SwaggerGen` 6.6.2 → **10.2.3** (Microsoft.OpenApi 2.x,
    OpenAPI 3.1 schema model). `OrionGuardSchemaFilter` now implements `Apply(IOpenApiSchema, ...)`;
    `[NotNull]` removes the `null` type flag instead of setting `Nullable`, `[Range]` writes invariant
    string bounds, and `[Positive]` sets `exclusiveMinimum: 0`. Consumers must be on Swashbuckle 10.
  - `OrionGuard.Locks.Redis`: `OrionLock.Redis` 0.2.3 → **2.0.0**.
  - `OrionGuard.Outbox.PostgresNotify`: `Npgsql` 8.0.5 → **10.0.3**.
  - `OrionGuard.Outbox.SqlServerBroker`: `Microsoft.Data.SqlClient` 5.2.2 → **7.1.0**.
  - `OrionGuard.Generators` / `OrionGuard.OpenApi`: `Microsoft.CodeAnalysis.CSharp` 4.8.0 → **5.9.0**. The
    generators now need Roslyn 5.9, i.e. **.NET SDK 10.0.400 or newer**; older SDKs (including every .NET 8
    and .NET 9 SDK) report `CS9057` and skip the generators. The `OrionGuard.Migration` tool moves to the same
    Roslyn version; it runs on the .NET 10 runtime, so it is unaffected.
  - `OrionGuard.Grpc` (`Grpc.AspNetCore.Server` 2.83.0), `OrionGuard.Hangfire` (`Hangfire.Core` 1.8.25,
    `Newtonsoft.Json` 13.0.4), `OrionGuard.OpenTelemetry` (`OpenTelemetry.Api` 1.19.0).
  - `OrionGuard.MediatR` stays on MediatR **12.4.1**, the last Apache-2.0 release; MediatR 13+ requires a
    commercial license key.
- **Every package's NuGet README was rewritten against the current code.** Each snippet was compiled against
  the sources, and claims that were no longer true were removed or corrected: Swagger's registration call and
  attribute names, gRPC's status code (`InvalidArgument`), OpenTelemetry's instrument names and registration,
  Blazor's hosting support (not WebAssembly), the Dashboard's endpoints and authorization defaults, and the
  localization count (14 languages). Known defects found along the way are listed as known issues in the
  affected READMEs until they are fixed. The Blazor and Generators package descriptions were corrected too.
- Test and benchmark tooling: xUnit 2.9.3, `xunit.runner.visualstudio` 4.0.0, `Microsoft.NET.Test.Sdk` 18.10.1,
  coverlet 10.0.1, BenchmarkDotNet 0.15.8.

### Fixed

- `OrionGuard.SignalR`: `OrionGuardHubFilter` threw `AmbiguousMatchException` for every hub call whose argument
  had a registered validator, because it looked up `Validate` by name and `IValidator<T>` has two overloads. It
  now validates the argument and throws `HubException` with a `Validation failed: <field>: <message>; ...`
  message, errors listed in argument order and then validator registration order.
- `OrionGuard.SignalR`: the filter is a singleton and resolved validators from the root service provider, so
  scoped validators failed, and it called the synchronous `Validate`, so `RuleForAsync` rules never ran.
  Validators now come from the hub invocation's scope and run through `ValidateAsync`.
- `OrionGuard.AspNetCore`: `[ValidateRequest]` did nothing. `OrionGuardMvcFilter` was registered in DI but never
  added to the MVC filter pipeline, and when added by hand it threw `AmbiguousMatchException`. The attribute now
  adds the filter to the action's pipeline. The filter honours `OrionGuardAspNetCoreOptions.DefaultStatusCode`
  and `UseProblemDetails` the same way the Minimal API endpoint filter does, and runs once per request when the
  attribute is on both the controller and the action.
- `OrionGuard.AspNetCore`: the Minimal API endpoint filter and the MVC filter now respond with the status a
  validator suggests through `GuardResult.FailureWithStatus` (for example 409), and fall back to
  `DefaultStatusCode` only when none is suggested. The endpoint filter previously always used `DefaultStatusCode`.
- `OrionGuard.Grpc`: `OrionGuardInterceptor` is a singleton and resolved validators from the root service
  provider, so scoped validators failed, and it called the synchronous `Validate`, so `RuleForAsync` rules never
  ran. Validators now come from the call's request scope (`HttpContext.RequestServices`) and run through
  `ValidateAsync` for unary and all streaming calls. The status code and `validation-errors-json` trailer are
  unchanged.
- The SignalR hub filter, gRPC interceptor, MVC filter, and Hangfire client filter ran only the last
  `IValidator<T>` registered for a type. They now run every registered validator and combine the errors, as the
  MediatR behavior already did.
- `OrionGuard.MediatR`: `ValidationBehavior` ran validators concurrently with `Task.WhenAll`, so two validators
  sharing a scoped `DbContext` failed with "A second operation was started on this context instance". Validators
  now run one after another.
- `OrionGuard.MediatR`: stream requests (`IStreamRequest<T>` sent with `IMediator.CreateStream`) were never
  validated, because only `IPipelineBehavior<,>` was registered. `AddOrionGuardMediatR` now also registers
  `StreamValidationBehavior<,>`, which validates the request before the handler yields its first item.
- **Numeric comparison rules now compare by value across numeric types.** `GreaterThan`, `LessThan` and `InRange`
  (on `Ensure`, `Validate.For`, `Validate.Nested` and `AbstractValidator`'s `RuleFor`) took their type from the
  literal, so `GreaterThan(0)` on a `decimal`, `long` or `double` silently passed every value, negative ones
  included. All built-in numeric types (`sbyte` through `decimal`) now compare with each other: integers
  exactly (also against floating-point values, so a `long` above 2^53 is not rounded), and a `decimal` against a
  `double` literal by the literal's decimal value, so `0.1m` equals `0.1`. A value
  that cannot be compared with the threshold (for example a string against a number) now fails the rule instead
  of being skipped, and `NaN` fails every comparison. `Positive()`, `NotNegative()` and `NotZero()` now also
  check `short`, `byte`, `sbyte`, `ushort`, `uint` and `ulong`, and fail for `NaN`; on a non-numeric value they
  now fail instead of passing. The `IComparable` overloads of `Validate.Nested` no longer throw
  `ArgumentException` when the types differ.
- **`Validate.For` and `Validate.Delta` no longer read the wrong property.** Their compiled-accessor cache was
  keyed by the last member name, so after `o => o.Customer.Name` a rule on `o => o.Name` read
  `Customer.Name` (and `o => o.Items.Count` / `o => o.Tags.Count`, or any two non-member selectors, shared one
  accessor). `Delta` keyed by the expression text, so one lambda capturing different variables read the first
  variable every time. Accessors are now shared only between selectors that read the same members; other
  selectors are compiled per call.
- **`CachedValidator` no longer serves a result cached for a different input.** Its key was built from each
  property's `ToString()`, so objects differing only in a collection, or `null` versus the string `"null"`,
  shared one cached result. Without a key selector a result is now cached only when the model is a record with
  compiler-synthesized equality; other models, including types with a hand-written `IEquatable<T>` such as an
  entity compared by Id, are validated on every call. The new
  `validator.WithCaching(keySelector)` overload caches any model by an explicit key. Calls with a non-empty
  `ValidationContext` now reach the inner validator's context overloads and are never cached; previously the
  context was dropped and results could be shared across tenants.
- **`InstrumentedValidator` (OrionGuard.OpenTelemetry) now passes the `ValidationContext` to the validator it
  wraps.** Enabling instrumentation used to drop the context, so context-aware rules (tenant, role, feature
  flags) stopped running.
- **`AddOrionGuardOpenTelemetry()` no longer breaks the service provider.** An open-generic `IValidator<>`
  registration made `BuildServiceProvider` throw, and a keyed registration made the call itself throw. Both are
  now left unchanged and are not instrumented.
- **Dynamic rules read numbers correctly in every culture.** `Range`, `GreaterThan`, `LessThan` and the length
  rules turned numbers into text with the current culture, so under tr-TR or de-DE `0.5` was read as `5`
  (`Range[1,1000]` accepted `0.5` and rejected `150.75`). Values and parameters are now converted without text;
  string values are parsed with the invariant culture, as before. A `NaN` value now fails a range or
  comparison rule.
- **Date guards now handle `DateTime.Kind`.** `Guard.AgainstPastDate` / `AgainstFutureDate` /
  `AgainstUnrealisticBirthDate`, the `DateTimeGuards` and `AgainstExpired` / `AgainstNotYetActive` extensions,
  and `Ensure(...).InPast()` / `InFuture()` compared raw ticks with `DateTime.UtcNow`, so a local time was off by
  the machine's UTC offset (`AgainstFutureDate(DateTime.Now)` threw east of UTC). Local values are now converted
  to UTC first. `Unspecified` values are treated as UTC, as before, and that is now documented.
- **Transient entities are no longer equal to each other.** Two `Entity<TId>` instances whose `Id` is still the
  default (`0`, `Guid.Empty`, `null`) compared equal and collapsed to one item in a `HashSet`. Such an entity is
  now equal only to itself and hashes by reference; note that its hash code changes once an `Id` is assigned.
- **`GuardResult.Combine` and `Merge` keep `SuggestedHttpStatusCode`.** The first non-null status code of the
  inputs is carried over instead of being dropped.
- **`ValidationMessages` follows the calling thread's culture when none is set.** The culture of whichever
  thread touched the class first used to become the default for every thread. Without `SetCulture`, messages
  now use the calling thread's `CurrentCulture`.
- **`EnsureAsync(...).ValidateAsync()` can be called more than once.** Each call re-ran every async rule and
  appended its errors again, so one failing rule reported two errors on the second call. Each rule now runs
  once, and later calls return the same result.
- **`AddOrionGuard(registry => ...)` now registers the validators.** Validators added with
  `ValidatorRegistry.Register<T, TValidator>()` were stored but never resolved; `IValidatorFactory` returned
  `null` for them. Each is now registered as a transient `IValidator<T>`, like `AddValidator<T, TValidator>()`.
- **Regex anchors.** Every anchored `GeneratedRegexPatterns` pattern ends with `\z` instead of `$`, which also
  matched before a trailing newline, so `"abc\n"` passed `AgainstNonAlphanumericCharacters` and
  `"a@b.co\n"` passed the email guards. `\d` became `[0-9]`, so `AgainstNonNumericCharacters`, the phone, Turkish
  phone, SemVer and strong-password patterns no longer accept non-ASCII digits such as `١٢٣`. The inline patterns in
  `GuardProfiles`, `CommonProfiles` and the obsolete `RegexPatterns` constants got the same treatment.
- **`AgainstCharactersOutsideSet`** (`Guard` and `StringGuards`) no longer builds a regex character class: every
  character of the allowed set is literal. `"+-="` used to allow `0`-`9` (a range), `"ab]"` rejected `"a"`, and
  `"z-a"` threw `RegexParseException`. An empty value is still rejected.
- **`AgainstContainingWhitespace`** rejects every `char.IsWhiteSpace` character, not only the space.
- **`FormatGuards`:** `AgainstInvalidHostname` accepts ASCII letters, digits and hyphens only (pass IDNs in punycode;
  this rejects look-alike hosts such as Cyrillic `exаmple.com`). `AgainstInvalidBase64String` rejects non-ASCII letters
  and misplaced padding that `Convert.FromBase64String` would reject (`ÄÄÄÄ`, `A=BC`, `====`).
  `AgainstInvalidJwtFormat` rejects an empty header or payload (`..`, `header..`) and non-ASCII characters; only the
  signature may be empty. `AgainstInvalidConnectionString` requires a key used by a common ADO.NET provider,
  OLE DB/ODBC or an Azure SDK connection string, as documented; `a=b` and Redis-style `host:6379,password=...` strings
  are rejected. `AgainstInvalidTimeZoneId` turns every lookup failure into `ArgumentException`; on Windows
  `Pacific Standard Time\Dynamic DST` used to throw `NullReferenceException`.
- **`AgainstInvalidCreditCard`** requires 12 to 19 ASCII digits after spaces and dashes are removed; `"-"`, `"0"` and
  Unicode-digit strings used to pass. The Visa and Mastercard guards share the ASCII-only digit check.
- **`AgainstInvalidMonetaryAmount`** compares the value with itself rounded to `maxDecimalPlaces`, so trailing zeros
  no longer count: `10.5000m` is a valid 2-place amount.
- **`AgainstInvalidBase64`** uses `Convert.TryFromBase64String` instead of catching `FormatException`; the accepted
  set is unchanged.
- **`OrionGuard.EntityFrameworkCore`: a synchronous `SaveChanges()` now handles domain events.** Only
  `SaveChangesAsync()` was intercepted, so a synchronous save wrote no outbox row (Outbox) and dispatched nothing
  (Inline); the events stayed on the aggregate and were lost or went out with a later, unrelated save. Both modes now
  work with `SaveChanges()`. In Inline mode the handlers run on a thread-pool thread and `SaveChanges()` blocks until
  they finish, the same contract as the async path; prefer `SaveChangesAsync()` when handlers do I/O.
- **The outbox dispatcher no longer retries a row forever when a handler's writes cannot be saved.** A handler that
  succeeded but left a write violating a constraint made every save of the row fail without counting the attempt, so
  the row blocked the outbox forever. Each row is now dispatched in its own DI scope: the handler's writes commit
  together with the row's processed stamp, and when they cannot be saved they are discarded and the row is recorded as
  a failed attempt that counts towards `MaxRetries` and dead-lettering. The writes of a handler that throws are no
  longer saved along with its failure either.
- **An `OperationCanceledException` from a handler no longer stops the outbox dispatcher or the archival worker.** A
  handler's `HttpClient` timeout (or an archiver's) ended the background worker for good while the host kept running.
  Only cancellation of the host's stopping token stops the workers now; any other cancellation is an ordinary failed
  row or batch.
- **A failed dispatcher poll is logged.** Faults outside any single row (an unreachable database, a missing table or
  `DbContext` registration) were swallowed without a trace. They are now logged at Error and counted by the new
  `orionguard.outbox.dispatcher.batch_faults` counter.
- **The outbox dispatcher and archival worker drain a backlog without waiting for the polling interval.** Each
  processed one batch per interval, capping dispatch at `BatchSize` rows per `PollingInterval` (about 1,000 rows an
  hour for archival). Both now poll again straight away while a full batch left the queue, and wait only after a
  partial or empty batch, or, for the dispatcher, one in which a row failed and will be retried.
- **Trace metadata can no longer fail the business save.** A hierarchical activity id (a legacy `Request-Id` parent)
  or a long `tracestate` was written unchecked into the 64- and 256-character `TraceParent`/`TraceState` columns, so
  SQL Server and PostgreSQL rejected the whole `SaveChanges`. Only a W3C id is stored now, and a `tracestate` longer
  than its column keeps the leading list members that fit. Column sizes are unchanged.
- **Inline mode waits for an explicit transaction to commit.** Inside `Database.BeginTransaction()`, Inline handlers
  ran as soon as `SaveChanges` returned, before the commit, and still ran when the transaction rolled back. The events
  are now held until the transaction commits and dropped if it rolls back or is disposed. An ambient `TransactionScope`
  or a transaction passed to `UseTransaction()` cannot be observed; there events are still dispatched after the save
  and a warning is logged once.
- **`AddDbContextPool` and `AddDbContextFactory` work with the domain-event interceptor.** Their options callback
  receives the root provider, so one "scoped" collector and dispatcher were shared by every context in the process:
  one request's save could dispatch another request's events, and with scope validation on the first save threw.
  Per-save state is now kept per `DbContext` instance, and with the root provider each Inline dispatch gets a DI scope
  of its own.
- **`orionguard.outbox.enqueued_rows_per_save` is recorded.** The count was lost between `SavingChanges` and
  `SavedChanges`, so the histogram never had a sample.
- **A dashboard replay or discard is no longer undone by an in-flight dispatch.** The dispatcher wrote a row's whole
  state back after dispatching, overwriting a replay or discard made meanwhile (a replayed row could end up
  dead-lettered), and the dashboard's read-then-write could re-queue a row the dispatcher had just finished. Both sides
  now use conditional updates on the row's expected state. Every dispatcher update, the successful one included, only
  applies while the row is still unprocessed with the `RetryCount` and `Error` it was read with, so a replay made while
  its handler runs wins: the row stays queued for the replayed delivery, and that attempt's handler writes are rolled
  back rather than applied twice. Replaying a row that was processed successfully in the meantime returns 409. No
  schema change is needed. The dashboard's replay and discard now need a relational EF Core provider.
- **`OrionGuard.Outbox.PostgresNotify` and `OrionGuard.Outbox.SqlServerBroker` no longer break the dispatcher on
  shutdown.** Stopping the listener completed its wake channel; when it stopped before the dispatcher, the Postgres
  signal faulted the dispatcher and the Service Broker signal made it poll in a tight loop. The channel is no longer
  completed, and waits fall back to the polling interval.
- **The Service Broker listener wakes the dispatcher only when a message arrived.** `SELECT @h` returns a row even when
  `WAITFOR` times out, so every timeout (every 30 seconds by default) woke the dispatcher; a NULL handle is now ignored.
- **`SkipLockedDistributedLock` reports a lost INSERT race as contention, and only that.** Raw SQL surfaces the
  provider's `DbException`, which the `DbUpdateException` handler never caught, so the losing replica threw instead of
  returning null and recording `lock_contended`. A failed INSERT now counts as contention only when the winning
  replica's lock row exists afterwards; any other failure (a lock key longer than its column, a constraint or trigger
  error, a transient fault) propagates, so the dispatcher logs it as a failed poll instead of idling silently as a
  standby.
- **`SkipLockedDistributedLock` works on PostgreSQL.** Its raw SQL used unquoted names, which PostgreSQL folds to lower
  case, so it never found the `"OrionGuard_OutboxLocks"` table and outbox dispatch never started. The table, schema and
  column names now come from the `OutboxLock` mapping in the model and are quoted by the provider (renamed tables and
  naming conventions work too); values stay parameterized. A missing mapping or table is logged with its cause.
- **The dashboard's `/failed` endpoint handles very large page numbers.** `(page - 1) * size` overflowed into a negative
  OFFSET, which SQL Server and PostgreSQL reject with a 500 and SQLite treats as the first page. A page past the end now
  returns an empty page.
- **`CopyToTableOutboxArchiver` works with a retrying execution strategy.** It opened its own transaction outside the
  strategy, which EF Core rejects under `EnableRetryOnFailure`, so every archival batch failed. The copy-and-delete now
  runs through the context's execution strategy.
- **`OutboxArchivalHealthCheck` no longer flaps between hourly archival batches.** Its fixed 15-minute Unhealthy
  threshold fired on every healthy worker at the default 1-hour interval. Unset thresholds now follow the archival
  polling interval (Degraded after two intervals, Unhealthy after three, never below 5 and 15 minutes); explicit
  `DegradedAfter`/`UnhealthyAfter` values are still used as given.
- **The outbox dispatcher, archival worker and `SkipLockedDistributedLock` read the time from a `TimeProvider`.** They
  used `DateTime.UtcNow`, so their timestamps could not be controlled in tests. New constructor overloads take a
  `TimeProvider`, and the DI registrations pass one when it is registered; existing constructors are unchanged.
- **The FluentValidation compatibility builder (`FluentStyleValidator<T>`) now gives FluentValidation's results.**
  - `NotEmpty()` passed every non-string value. It now also fails an empty collection or sequence and the default
    value of a value type (`0`, `Guid.Empty`, `DateTime.MinValue`, `false`). A nullable property holding `0` still
    passes, as in FluentValidation.
  - `GreaterThan`, `GreaterThanOrEqualTo`, `LessThan`, `LessThanOrEqualTo`, `InclusiveBetween` and
    `ExclusiveBetween` compare numbers by value across numeric types. `RuleFor(x => x.Price).GreaterThan(0)` on a
    `decimal` threw `ArgumentException: Object must be of type Decimal` on every call.
  - A pair that cannot be compared (`NaN`, a `null` threshold, or unrelated types such as a `DateTime` against
    `0`) now fails the rule instead of throwing. A non-null value that is not `IComparable` now fails instead of
    passing. A `null` value still passes.
  - `Length(min, max)` is now a single rule, so `WithMessage` and `WithErrorCode` also cover a value that is too
    short. Before, such a value kept the default minimum-length message and code. Without an override the codes
    stay `MIN_LENGTH` and `MAX_LENGTH`.
- **`FluentStyleValidator<T>` no longer recompiles its property accessors for every instance.** Validators are
  registered as transient, so every resolution compiled every `RuleFor` expression again. Member selectors now
  come from the shared accessor cache and are compiled once per process.
- **`OrionGuard.Migration` now rewrites a rule only when the OrionGuard equivalent gives the same result.**
  - `Matches(...)` and `EmailAddress()` are reported instead of rewritten. FluentValidation checks empty strings
    against both rules, and its email check only requires one `@`. The compatibility builder skips blank values
    and uses a stricter email pattern.
  - `WithMessage` is migrated only when its argument is a string literal without `{`. FluentValidation fills in
    placeholders such as `{PropertyName}`, and the compatibility builder prints them as written.
  - These overloads are now reported, because the rewritten code did not compile: `Must`, `When` and `Unless`
    with a two- or three-parameter lambda, and `Length` with `Func<T, int>` bounds.
  - FluentValidation's exact-length rule `Length(n)` is now migrated to `Length(n, n)` when `n` is a numeric
    literal (any other argument would be evaluated twice, and a method group is the `Func<T, int>` overload). It used to be reported
    as an unknown rule.
  - `ExactLength(n)` is not a FluentValidation rule, so it is now reported instead of rewritten.
- **The outbox dispatcher finishes the processed stamp when the host stops.** A row whose handlers had already
  run left `ProcessedOnUtc` null if shutdown was requested before the stamp was written, so the event was
  dispatched again on the next start. The stamp is no longer cancelled by shutdown; the database command
  timeout and the host's shutdown timeout still bound it.
  The rest of the batch is left for the next start: once shutdown is requested, no further row is dispatched.

### Deprecated

- `AddOrionGuardExceptionFactory<TFactory>()`, `ExceptionFactoryProvider.Configure` / `Reset` and
  `DefaultExceptionFactory` are marked `[Obsolete]` and will be removed in v7. No guard ever called
  `IExceptionFactory`, so registering a factory never changed the exceptions guards throw, contrary to the
  documentation. Catch `GuardException` (or the specific exception type) at your boundary instead.

### Security

- **`OrionGuard.Outbox.Dashboard` is no longer anonymous by default.** When neither `AuthorizationPolicyName`
  nor `AllowAnonymous` was set and the host had no `AuthorizationOptions.FallbackPolicy`, every dashboard
  endpoint, including `POST /{id}/replay` and `POST /{id}/discard`, accepted anonymous callers, contrary to
  the XML documentation. The group now calls `RequireAuthorization()` in that case, so the host's default
  policy (an authenticated user) applies. A configured fallback policy still applies unchanged, and
  `AllowAnonymous = true` remains the explicit opt-out. Hosts that relied on anonymous access must now
  authenticate operators or set `AllowAnonymous = true`.
- **Regex rules now always run with a match timeout.** `[Regex]` (`RegexAttribute`), the compatibility layer's
  `Matches(pattern)`, `PropertyValidator<T>.Email()`, and the `[GenerateValidator]` source generator's email
  and pattern checks called the static `Regex.IsMatch`, which has no timeout, so a catastrophic-backtracking
  pattern could pin a request thread on hostile input (ReDoS). They now use `RegexCache` or the source-generated
  `GeneratedRegexPatterns.Email()`, both bounded at 1 second, instead of hanging. Validators generated by
  `OrionGuard.Generators` pick this up on the next build. In the core package a match that times out is reported
  as a validation failure (see "A regex timeout is a validation failure" below); generated validators still throw
  `RegexMatchTimeoutException`.
- `Testcontainers.Redis` 4.0.0 → 4.15.0 in the Redis lock tests drops the transitive `SSH.NET` 2023.0.0
  ([GHSA-mggc-4xg6-vcxf](https://github.com/advisories/GHSA-mggc-4xg6-vcxf),
  [GHSA-q939-rpr3-3284](https://github.com/advisories/GHSA-q939-rpr3-3284), High). Test only.
- **The injection guards are documented as heuristics.** The XML documentation of `AgainstSqlInjection`,
  `AgainstXss`, `AgainstCommandInjection`, `AgainstLdapInjection`, `AgainstXxe` and `AgainstInjection`, and both
  READMEs, now say plainly that these are denylists that miss payloads and reject some ordinary text, and name the
  actual defences: parameterized queries, contextual output encoding, `ProcessStartInfo.ArgumentList` without a
  shell, RFC 4515/4514 escaping, and an XML reader with DTD processing prohibited. No API was removed or renamed.
- **`AgainstOpenRedirect` follows ASP.NET Core's `IsLocalUrl`.** A value is accepted only if it is a local path
  (starts with `/` not followed by `/`, or with `~/` not followed by `/`) or an absolute `http`/`https` URL that is
  not a UNC path and whose host is in the allow-list or a subdomain of one. Newly rejected:
  - any value containing whitespace (including a plain space; percent-encode it), a control character, or `\`,
    which closes `/\t/evil.com`, `/\n/evil.com` and `/\evil.com`, turned into `//evil.com` by browsers;
  - `javascript:`, `data:` and every other non-http scheme, and `https:evil.com`;
  - relative paths without a leading `/` (`page.html`, `../x`);
  - every absolute URL when the allow-list is empty. Previously an empty allow-list accepted any absolute URL, and
    `//evil.com`, `/\evil.com` and `\\evil.com` passed, although the documentation said they were always rejected.

  Fixed: local paths are accepted on Linux and macOS when an allow-list is supplied; they were rejected because
  `Uri` parses `/path` as a `file:` URI there.
- **`AgainstPathTraversal` catches encoded, normalized and rooted forms.** The value is checked as given, after
  each of up to three URL decodes, and after Unicode NFKC normalization, so `.%2e/`, `%2e./`, `%252e%252e%252f`
  and fullwidth `．．／` are rejected. It now also rejects rooted paths on every OS: a leading `/` or `\`
  (`/etc/hosts`, `\\attacker\share`, `//attacker/share`) and a drive prefix (`C:\inetpub\...`, `C:x`), a value
  that still decodes further after three passes, and ill-formed UTF-16. `AgainstUnsafeFileName` inherits all of
  this. `AgainstInjection` gets the decoding and normalization but not the rooted-path rule, so text such as
  `/help` still passes it.
- **New `AgainstPathEscape(root)` guard.** `value.AgainstPathEscape(root, nameof(value))` resolves
  `Path.GetFullPath(Path.Join(root, value))`, throws unless the result lies strictly inside `root`, rejects rooted
  and UNC values, and returns the resolved full path. Additive API.
- **`AgainstSqlInjection` / `AgainstInjection`:** now reject quoted tautologies (`admin' OR '1'='1`, `' OR ''='`,
  `'a' = 'a'`), GRANT statements (`GRANT ... TO`), and `OPENROWSET`, `OPENQUERY`, `OPENDATASOURCE`, `OPENXML`.
  The bare words `GRANT` and `OPEN` are no longer matched, so names and text such as "Grant Smith", "Hugh Grant",
  "granted", "open" and "reopened" are no longer rejected.
- **`AgainstXss` / `AgainstInjection`:** the pattern list is also checked after HTML character references are
  decoded (`jav&#x61;script:`, `&#106;`, `&colon;`, `&Tab;`, `&NewLine;`), tab/CR/LF are removed, and whitespace
  before `=` is dropped (`onerror =`). About 110 event-handler attributes are recognized (previously 11), including
  `ontoggle`, `onbegin`, `onanimationstart` and the pointer events.
- **`AgainstCommandInjection`:** now also rejects `&`, `<`, `>`, `$`, `'`, `"`, CR and LF anywhere, and a value that
  starts with `-` after leading whitespace (option injection such as `--output=...`). `AgainstInjection` keeps
  checking only the shell operators and interpreters (`| || && ; `` ` `` `$(`, `cmd.exe`, ...), so apostrophes,
  `&` and line breaks in free text still pass it.
- **File uploads:**
  - `AgainstDangerousFileExtension` also rejects `.aspx`, `.ashx`, `.asmx`, `.php`, `.phtml`, `.jsp`, `.config`
    (including `web.config`), `.html`, `.htm`, `.svg`, `.lnk` and `.jar`.
  - `AgainstDangerousFileExtension` and `AgainstDisallowedExtension` read the extension the way Windows stores the
    name: trailing dots and spaces are removed (`evil.exe.` and `"evil.exe "` are `.exe`), and a name containing `:`
    is rejected (`evil.exe::$DATA`, and `evil.exe:.jpg`, which used to pass an allow-list of `.jpg`). A name such as
    `photo.jpg.` now passes an allow-list of `.jpg`.
  - `AgainstMaliciousContent` rejects every `.svg` (SVG is active content and can carry script) and scans the whole
    byte array; it used to read only the first 8 KB, so `<?php` at byte 8200 passed. Its documentation no longer
    claims macro detection, which was never implemented.
  - New overload `AgainstMaliciousContent(Stream, claimedExtension, parameterName, maxScanBytes)` scans the whole
    stream and rejects a stream longer than `maxScanBytes` instead of scanning part of it. Additive API.
  - `AgainstFakeMimeType` is unchanged: extensions without a known signature (`.txt`, `.csv`, `.html`, ...) still
    pass. Its documentation now says so and points to `AgainstDisallowedExtension` as the control.
- **Email validation is linear and consistent.** The `GeneratedRegexPatterns.Email()` pattern backtracked
  quadratically on inputs such as `"a@" + "a." * n + " "`; 40 KB took a full second and ended in
  `RegexMatchTimeoutException` (an HTTP 500). The new pattern, `^(?=.{1,254}\z)[^@\s]+@[^@\s.]+(?:\.[^@\s.]+)+\z`,
  runs in linear time and caps the address at 254 characters before anything else. Every email check in the core
  package now uses it: `Guard.AgainstInvalidEmail`, `string.AgainstInvalidEmail`, `Ensure...Email()`,
  `FastGuard.Email`, `[Email]`, `PropertyValidator<T>.Email()`, `Validate.Nested(...).Email()`, the compatibility
  layer's `EmailAddress()`, the dynamic `Email` rule, `CommonProfiles.Email`, `GuardProfiles.Email` and the obsolete
  `RegexPatterns.Email` constant; `OrionGuard.OpenApi` emits the same pattern for `format: email`. Newly rejected
  everywhere: addresses longer than 254 characters, a trailing newline, and empty domain labels (`a@.b.com`,
  `a@b..com`, `a@b.com.`). `FastGuard.Email` additionally stops accepting a second `@` (`a@b@c.com`) and tabs or
  line breaks.
- **A regex timeout is a validation failure.** APIs that return a result report a match that exceeds the regex
  timeout as an invalid value instead of throwing `RegexMatchTimeoutException`: `Ensure.Accumulate(...).Matches()`
  and `.Email()`, `[Regex]` and `[Email]`, the compatibility layer's `Matches()` and `EmailAddress()`,
  `PropertyValidator<T>.Email()`, `Validate.Nested(...).Email()`, and the dynamic `Regex`/`Pattern` and `Email`
  rules. Throwing guards throw their own validation exception: `Ensure.That(...).Matches()` throws
  `GuardException`, `Guard.For(...).Matches()` throws `RegexMismatchException`, `AgainstRegexMismatch` throws
  `ArgumentException`, and the email guards throw `InvalidEmailException`. `RegexCache.IsMatch` itself is unchanged.
- **URL rules accept only absolute `http`/`https` URLs**, like `Guard.AgainstInvalidUrl` already did:
  `Ensure...Url()`, `CommonProfiles.Url` and the dynamic `Url` rule now reject `javascript:`, `data:`, `file:`,
  `ftp:` and Unix paths (which `Uri` parses as `file:` URIs).
- **`AgainstInvalidXml` no longer processes DTDs.** Both `string.AgainstInvalidXml` and `Guard.AgainstInvalidXml`
  read the document with `DtdProcessing.Prohibit` and no `XmlResolver`. A 314-byte billion-laughs document used to
  allocate about 174 MB before it was accepted. Any document with a `<!DOCTYPE>` is now rejected as invalid.
- **`SensitiveDataGuards` detects more, on a best-effort basis:** `AgainstContainsCreditCardNumber` (and
  `AgainstContainsPii`) finds card numbers written with single spaces or dashes between digit groups, even next to
  other numbers, recognizes Mastercard 2221-2720 and UnionPay `62` prefixes, and only counts ASCII digits.
  `AgainstContainsSecret` (and `AgainstContainsPii`) also rejects bare JWTs, GitHub tokens (`ghp_`, `gho_`, `ghs_`,
  `ghu_`, `ghr_`, `github_pat_`) and Stripe live keys (`sk_live_`, `rk_live_`).

## [6.8.1] - 2026-07-21

### Security

- **`OrionGuard.OpenTelemetry` 6.7.0 → 6.7.1** — bumps its direct `OpenTelemetry.Api` dependency
  from 1.9.0 to **1.15.3** to clear [GHSA-g94r-2vxg-569j](https://github.com/advisories/GHSA-g94r-2vxg-569j)
  / CVE-2026-40894 (Moderate): a denial-of-service via excessive memory allocation when parsing
  OpenTelemetry propagation headers (baggage / B3 / Jaeger). This is the only shipped package
  affected, and it is the only package whose version changes in this release.
- Pinned `SQLitePCLRaw.bundle_e_sqlite3` to 2.1.12 in the demo, EF Core test, and AOT-probe
  projects to clear [GHSA-2m69-gcr7-jv3q](https://github.com/advisories/GHSA-2m69-gcr7-jv3q)
  (High). Test/probe only — no shipped package references SQLite.

### Changed

- **NuGet audit is re-armed.** `NuGetAuditMode=all` now audits the whole transitive dependency
  graph (the default is direct references only), and the per-project `NoWarn` lists no longer
  suppress `NU1901`–`NU1904`, so package-vulnerability advisories surface in every restore. A new
  root `Directory.Build.props` holds these two settings so every project inherits them; advisories
  are downgraded to warnings via `WarningsNotAsErrors` so a newly-disclosed upstream CVE surfaces
  loudly without turning CI red on an external event with no code change. It was this change that
  surfaced the two advisories above — the previous blanket suppression had hidden them.

## [6.8.0] - 2026-07-20

> **Packaging.** This is the first release to publish from a solution-level pack. The publish job
> previously enumerated projects by hand, which had held two packages back: `OrionGuard.Hangfire`
> and `OrionGuard.OpenApi` (both new in 6.7.0) were built but never pushed to NuGet, so 6.7.0
> never fully shipped. This release packs the whole solution and verifies the package count, so
> those two ship now alongside the 6.8.0 `OrionGuard.Migration` tool, and every future
> sub-package ships automatically. Each package keeps its own version — the core suite is 6.7.0,
> the Migration tool is 6.8.0.

### Added

#### `OrionGuard.Migration` — FluentValidation to OrionGuard migration codemod (R1)

New tool package `OrionGuard.Migration` (PackageId `OrionGuard.Migration`), packaged as a dotnet tool (`PackAsTool`, command `orionguard`). It ships the long-planned R1 codemod: `dotnet orionguard migrate <path> [--report|--apply]` reads C# sources with Roslyn, finds classes deriving from `AbstractValidator<T>`, and rewrites their `RuleFor(...)` chains onto OrionGuard's `Moongazing.OrionGuard.Compatibility.FluentStyleValidator<T>` compatibility surface. The migration is additive: no existing OrionGuard package changed.

- The codemod targets the compatibility builder shipped in the core `OrionGuard` package, so it is a class-header rewrite (`using FluentValidation;` to `using Moongazing.OrionGuard.Compatibility;`, base `AbstractValidator<T>` to `FluentStyleValidator<T>`) plus per-rule method rewrites. Because the emitted methods all exist on `FluentRuleBuilder<T, TProperty>`, the migrated output compiles against the real OrionGuard API; the test suite proves this by compiling and running a corpus of migrated validators.
- **Rules covered (24):** `NotNull`, `NotEmpty`, `Equal`, `NotEqual`, `Length(min,max)`, `MinimumLength`, `MaximumLength`, `ExactLength` (rewritten to `Length(n, n)`), `Matches`, `EmailAddress`, `GreaterThan`, `GreaterThanOrEqualTo`, `LessThan`, `LessThanOrEqualTo`, `InclusiveBetween`, `ExclusiveBetween`, `Must(predicate)`, `WithMessage`, `WithErrorCode`, `When`, `Unless`.
- **Reported, not migrated.** Anything with no safe one-to-one equivalent is left byte-for-byte untouched, annotated with a `// TODO: OrionGuard migration - <reason>` marker, and listed in the run summary (file, line, rule, reason): `Null`, `Empty`, `WithName` / `OverridePropertyName`, `Cascade`, `ScalePrecision` / `PrecisionScale`, `MustAsync`, `SetValidator`, `RuleForEach`, `Include`, `ChildRules`, `DependentRules`, `Custom`, unsupported overload shapes (for example `EmailAddress(mode)`, the `WithMessage(Func<T,string>)` factory, or a two-argument `Must`), and any unrecognised custom rule extension. A `RuleFor` chain is migrated all-or-nothing: if any rule in the chain is unsupported the whole chain is left untouched, so a rule is never silently dropped or partially translated.
- **Modes and exit codes.** `--report` (alias `--dry-run`) prints a unified-style diff and the summary without writing; `--apply` writes the migrated files. A bare invocation defaults to `--report` so nothing is written by accident. `--include <glob>` filters the directory scan (default `*.cs`). Exit codes: `0` clean, `1` manual follow-up required, `2` usage error.
- The only third-party dependency is `Microsoft.CodeAnalysis.CSharp` 4.8.0 (matching `OrionGuard.Generators`); the CLI parser is hand-rolled. Targets `net10.0`.

### Tests

- `RuleMapperTests`: every supported rule maps to its expected target method; `ExactLength` maps to `Length` with argument duplication; each known-unsupported rule is reported with a reason; unsupported overload arities (two-argument `Must`, `EmailAddress(mode)`) are reported rather than mistranslated.
- `MigrationEngineTests`: the `using` directive and base type are rewritten; supported chains are preserved verbatim; `ExactLength(n)` becomes `Length(n, n)`; conditional `When`, custom `WithMessage`, and multiple `RuleFor` on different properties all migrate; an unsupported rule leaves the chain untouched with a TODO and a finding; a chain mixing supported and unsupported rules is left whole (no partial rewrite); a non-validator file is unchanged; re-running is idempotent and does not stack TODO markers.
- `MigratedOutputCompilationTests`: a corpus of migrated validators is compiled against the real `OrionGuard` assembly (proving every emitted method exists), and one migrated validator is emitted, instantiated, and run to confirm it actually validates.
- `MigrationRunnerTests`: `--report` writes nothing and prints a diff; `--apply` writes the migrated file; an unsupported construct yields exit code `1` in both modes (apply still writes the partial migration); a missing path yields exit code `2`; a directory scan migrates only matching files.
- `CliParserTests` / `CliEntryPointTests`: argument parsing (mode flags, `--include`, help, mutually exclusive modes, unknown command/option, missing/duplicate path) and the console entry point's help and usage-error output.

### Notes

- This release ships only the new `OrionGuard.Migration` tool package at `6.8.0`. The existing OrionGuard packages are unchanged and remain at `6.7.0`.

## [6.7.0] - 2026-06-23

### Added

#### `OrionGuard.Hangfire` integration

New add-on package `OrionGuard.Hangfire` (PackageId `OrionGuard.Hangfire`) that validates a background job's arguments at enqueue time, so a job that is invalid by construction is rejected by the enqueue call instead of being persisted and later failing inside a worker.

- `OrionGuardClientFilter` is a Hangfire `IClientFilter` whose `OnCreating` walks `context.Job.Args`, resolves the matching `IValidator<TArg>` for each argument's runtime type from the application's `IServiceProvider`, and runs it. This is the same service-provider-based validator resolution the MediatR, ASP.NET Core, gRPC, and SignalR integrations use; because job argument types are only known at runtime, resolution and invocation go through reflection exactly as the SignalR hub filter does.
- Blocking validation errors are accumulated across all arguments in a single pass. On failure the filter throws `JobArgumentValidationException`, which carries the full `IReadOnlyList<ValidationError>` plus the target job `Type` and method name. Hangfire surfaces an exception thrown from `OnCreating` to the caller that enqueued the job, so the enqueue is rejected at the call site.
- Arguments that are `null`, or whose type has no registered validator, pass through untouched. A validator that itself throws has its real exception surfaced (unwrapped from the reflection `TargetInvocationException`) rather than masked.
- `OnCreated` is a no-op: enqueue-time validation is complete once `OnCreating` returns.
- Registration helpers `GlobalConfigurationExtensions.UseOrionGuardValidation(this IGlobalConfiguration, IServiceProvider)` (fluent `GlobalConfiguration` chain) and `GlobalConfigurationExtensions.AddOrionGuardClientFilter(this JobFilterCollection, IServiceProvider)` (the `GlobalJobFilters.Filters` collection) wire the filter to OrionGuard's validator resolution.
- Depends on `Hangfire.Core` 1.8.22 (the abstractions package, not the full server) and the core `OrionGuard` package. Targets .NET 8.0, 9.0, and 10.0.

#### `OrionGuard.OpenApi` — OpenAPI-first validation (R2)

New add-on package `OrionGuard.OpenApi` (PackageId `OrionGuard.OpenApi`): a Roslyn incremental source generator that reads an OpenAPI 3 schema at compile time and emits an OrionGuard validator enforcing its constraints. This is the inverse of `OrionGuard.Swagger` (which goes validator to OpenAPI), closing the contract-first loop: schema to validator to controller.

- `[OpenApiValidator("openapi.json", "#/components/schemas/Customer")]` on a `partial class` names an OpenAPI document (supplied to the compiler as an `AdditionalFile`) and a JSON pointer to a schema within it. The generator resolves the schema, binds its properties to the validated type's members, and emits the class body implementing `Moongazing.OrionGuard.DependencyInjection.IValidator<T>` returning a real `GuardResult`. The validated type `T` is inferred from the `IValidator<T>` interface (or an `AbstractValidator<T>` / `FluentStyleValidator<T>` base); if the annotated class has neither, its own properties are validated. A generated validator is interchangeable with a hand-written one anywhere an `IValidator<T>` is consumed (the ASP.NET Core endpoint filter, the MediatR behaviour, a manual call).
- Constraints enforced: `type` (string / integer / number / boolean / array / object), `required` (`REQUIRED`), `nullable` (a null value skips the value constraints), string `minLength` / `maxLength` (`MIN_LENGTH` / `MAX_LENGTH`), string `pattern` (`PATTERN`), string `format` for `email`, `uuid`, `date-time`, `date`, `uri`, `hostname`, and `ipv4` (`FORMAT`), numeric `minimum` / `maximum` including `exclusiveMinimum` / `exclusiveMaximum` across both the OpenAPI 3.0 boolean form and the JSON Schema 2020-12 numeric form (`MINIMUM` / `MAXIMUM`), `enum` over string or numeric values (`ENUM`), array `minItems` / `maxItems` (`MIN_ITEMS` / `MAX_ITEMS`), and intra-document `$ref` resolution for both the root pointer and individual properties (with reference-cycle detection). Schema property names bind to C# members case-insensitively, so `firstName` maps to `FirstName`.
- Each constraint is gated on the bound member's C# type category (string checks only for string members, numeric comparisons only for numeric members, count checks only for collections), so the generated code compiles cleanly under the consumer's `TreatWarningsAsErrors` even when the document and the POCO disagree about a property's shape.
- The generator bundles its own minimal JSON reader; it takes no unbundled NuGet dependency in the analyzer and adds no runtime dependency to the consumer's output. Targets `netstandard2.0` (Roslyn-compatible), consumed by any `net8.0+` project. Packaged as an analyzer (`analyzers/dotnet/cs`, `IncludeBuildOutput=false`, `DevelopmentDependency=true`) exactly like `OrionGuard.Generators`.
- Diagnostics are clean, OG-prefixed, and non-fatal (the build never crashes): `OG1001` (the named document was not supplied as an `AdditionalFile`), `OG1002` (the document is not parseable JSON), `OG1003` (the pointer did not resolve), `OG1004` (a `$ref` did not resolve), `OG1005` (the target is not a `partial class`), and `OG1006` (an unsupported construct was skipped while the rest of the schema was still enforced).
- **Deferred, by design:** YAML documents (only JSON is read; a YAML document raises `OG1002` rather than pulling a heavy, unbundleable YAML parser into the analyzer) and polymorphism / composition (`discriminator`, `oneOf`, `anyOf`, `allOf`), which raise `OG1006` and are skipped rather than half-implemented. Both are tracked as follow-ups.

### Changed

- Uniform family version bump to `6.7.0` across all packages, including the new `OrionGuard.Hangfire` and `OrionGuard.OpenApi`.

### Tests

- `OrionGuardClientFilterTests`: valid arguments enqueue without throwing; invalid arguments are rejected at enqueue with the accumulated validation errors and the job identity on the exception; an argument type with no registered validator passes through; the filter resolves validators from DI (the same invalid payload is rejected only when the validator is registered); mixed validated/unvalidated arguments enforce only the registered type; no-argument jobs pass; a `null` argument value is skipped; a throwing validator surfaces its real exception; `OnCreated` is a no-op; and the constructor / `OnCreating` null-argument guards. Tests drive a real Hangfire `CreatingContext` built from `Job.FromExpression` over a fake job storage, so no Hangfire server runs.
- `OrionGuard.OpenApi.Tests`: a Roslyn harness compiles a sample consumer plus an OpenAPI `AdditionalFile`, runs the generator, then emits and executes the generated validator so each constraint is exercised against real input through the real `GuardResult`. Coverage: a fully valid instance passes; each constraint kind fails individually (required, email/uuid/uri/date-time format, string min/max length, pattern, numeric minimum/maximum, exclusive minimum at and above the bound, string and numeric enum, array min/max items, nullable null-skips-then-present-fails); multiple violations accumulate; a property `$ref` and a root-pointer `$ref` resolve and enforce the referenced schema; the generated code compiles warning-clean (TreatWarningsAsErrors parity), implements the real `IValidator<T>` contract, and is deterministic; and each diagnostic (`OG1001`, `OG1002` on a YAML document, `OG1003`, `OG1005`, `OG1006` while still enforcing the rest) fires without breaking the build.
- `GlobalConfigurationExtensionsTests`: both registration helpers add the filter, the fluent helper returns a chainable `IGlobalConfiguration`, and their null-argument guards.

## [6.6.2] - 2026-06-20

### Performance

- Removed per-call heap allocations from the digit-validation hot paths used by the financial and identity guards. `AdvancedStringGuards.IsValidTurkishId` previously ran `tcNo.All(char.IsDigit)`, `tcNo.Select(c => c - '0').ToArray()`, and `digits.Take(10).Sum()`, allocating a delegate, multiple enumerators, and an `int[]` on every call; it now validates and materializes the eleven digits in a single pass into a `stackalloc` buffer. Measured on the valid-input path: 292 ns and 176 B/call to 27 ns and 0 B/call (about 11x faster, zero allocation). `IsValidLuhn` (credit card / Visa / MasterCard / IMEI), `IsValidIsbn13`, `AgainstInvalidEan`, and `AgainstInvalidImei` now use an allocation-free manual digit scan in place of `value.All(char.IsDigit)` (about 2x faster on the Luhn path). `char.IsDigit` semantics and all arithmetic are preserved exactly, so validation results are unchanged.

## [6.6.1] - 2026-06-20

### Changed

- All diagnostics meter versions now derive from the package version automatically. Each assembly that constructs a `Meter` resolves the instrumentation version from its own `AssemblyInformationalVersion` (build metadata stripped) instead of a hardcoded literal, so the meter version always equals the package version and can no longer drift. This aligns several previously-stale meter versions (`6.0.0`, `6.4.0`, `6.5.13`, `6.5.16`) with the current package version.

## [6.6.0] - 2026-06-19

### Added

#### Asynchronous validation pipeline on `ObjectValidator<T>`

`Validate.For(...)` / `Validate.ForStrict(...)` gain a first-class async path so rules that must perform I/O (a database uniqueness check, a remote lookup) participate in the same guard pipeline as the synchronous rules, with full `CancellationToken` flow, short-circuit parity, and one merged `GuardResult`.

- `MustAsync<TProperty>(selector, Func<TProperty, CancellationToken, Task<bool>>, message, errorCode?)` registers a property-scoped async rule. `MustAsync(Func<T, CancellationToken, Task<bool>>, message, parameterName, errorCode?)` registers an instance-scoped async rule. Default error code is `ASYNC_PREDICATE`.
- `WhenAsync(condition, configure)` conditionally registers async rules, mirroring the synchronous `When` overload.
- `ToResultAsync(CancellationToken)` runs the synchronous rules already collected during the fluent chain plus the deferred async rules, surfacing sync errors first and appending async errors into the same `GuardResult`. `BuildAsync` / `ThrowIfInvalidAsync` are the async counterparts of `Build` / `ThrowIfInvalid`.
- Short-circuit parity: a strict validator (`Validate.ForStrict`) throws `AggregateValidationException` on the first failure, including the first async failure, after which no further async rules run, exactly matching the synchronous strict path. A non-strict validator (`Validate.For`) runs every rule and aggregates all failures.
- Cancellation is honored: the token is observed before and during each async rule and `OperationCanceledException` propagates to the caller rather than being recorded as a validation error.
- The synchronous terminals (`ToResult` / `Build` / `ThrowIfInvalid`) now throw `InvalidOperationException` when async rules are pending, so async rule results are never silently discarded.
- The synchronous API is unchanged and remains source- and binary-compatible.

### Fixed

- `ObjectValidator<T>.NotNull<TProperty>` was constrained to `where TProperty : class`, which forced callers validating a nullable reference property (for example `string?`) to suppress CS8634/CS8621 nullability warnings even though checking a nullable reference for null is the method's exact purpose. The constraint is relaxed to `class?`. The change is source- and binary-compatible: the emitted IL constraint is unchanged and only the compile-time nullability annotation is relaxed.

### Tests

- `ObjectValidatorAsyncTests`: async rule pass/fail, default error code, property/instance scope, value passthrough, cancellation honored (before and inside a rule, never converted to an error), mixed sync+async aggregation and ordering, multiple async failures, strict short-circuit on first async failure (and that later rules do not run), non-strict full aggregation, the async terminals, the pending-async-rule guard on the sync terminals, `WhenAsync`, and argument-null validation.

## [6.5.30] - 2026-06-18

### Changed
- Set the NuGet package icon to the navy Moongazing mark across every sub-package, and the README logo to the white Moongazing mark.

## [6.5.29] - 2026-06-15

### Added

#### `orionguard.outbox.dispatcher.lock_contended` counter

`Counter<long>` increments each dispatcher cycle in which this replica fails to acquire the multi-instance distributed lock because another replica holds the lease.

- Recorded by the default `SkipLockedDistributedLock` on its GENUINE contention paths only (an INSERT race lost, or a live owner found), and deliberately NOT when the lock table is missing (migration not applied), so a broken dispatcher setup is never mis-reported as healthy standby contention (codex P2).
- Distinct from the v6.5.17 `poll.idle` counter, which fires only AFTER the lock is held and the backlog is found empty. `lock_contended` fires when the replica never became the active dispatcher at all.
- Operators graph it per replica to confirm exactly one replica is dispatching (the others should sit mostly contended), to spot a stuck or dead leader, and to right-size the dispatcher replica count.
- Public `OutboxDispatcherDiagnostics.RecordLockContended()` helper for consumer-owned lock backends (Redis, etc.) to emit from their own genuine contention path.

### Tests

- `LockContendedCounterTests`: `RecordLockContended` increments the counter.
- `SkipLockedDistributedLockTests.TryAcquireAsync_WhenSlotIsHeld_RecordsLockContended`: a contended acquire against a live owner records the counter.

## [6.5.28] - 2026-06-15

### Added

#### `orionguard.outbox.dispatcher.dead_lettered` counter

`Counter<long>` increments once for every row the dispatcher permanently abandons: an unresolvable or non-`IDomainEvent` type, a payload that fails to deserialize, or a transient failure that finally exhausted `MaxRetries`.

- Distinct from the v6.5.18 `errors` counter, which fires on EVERY swallowed failure (transient retries included). Operators alert on the dead-letter rate as the SLO signal that rows are being lost, which the much higher errors rate dilutes.
- Emitted only AFTER the row's terminal state is persisted (the post-`SaveChangesAsync` block, alongside the deferred success metrics): `NotifyRowFailureAsync` now returns the terminal exception type and the loop records it post-persist, so a `SaveChanges` failure that re-dispatches the row does not double-count (codex/CodeRabbit P2).
- Tag: `exception_type` (the terminal cause), for triage.
- Public `OutboxDispatcherDiagnostics.RecordDeadLetter(string)` helper.

### Tests

- `DeadLetteredCounterTests`: emits a measurement tagged with the exception type.
- `OutboxDispatcherTests.ProcessBatch_DeadLetter_EmitsTheDeadLetteredCounterAfterPersistence`: a real dead-letter driven through the loop emits the counter via the post-persist path.

## [6.5.27] - 2026-06-15

### Added

#### `orionguard.outbox.dispatcher.retries_before_success` histogram

`Histogram<int>` records the retry count a row had accumulated at the moment it dispatched successfully (its `RetryCount` on the success path: 0 = succeeded on the first attempt). It measures the successful side of the dispatch loop, complementing the v6.5.18 `errors` counter (failures) and the dead-letter path (terminal only), so operators can answer "are retries quietly papering over downstream flakiness?".

- A healthy system sits at p50 = 0; a rising upper percentile means rows are increasingly succeeding only after transient downstream failures.
- Unlike the batch-size histograms, the zero sample IS recorded: the fraction of first-try successes is exactly the signal, so dropping zeros would erase the healthy baseline.
- Emitted post-persist (after `SaveChangesAsync`), alongside `queue_lag` (v6.5.16) and `row_size_bytes` (v6.5.19), so a SaveChanges failure that re-dispatches the row does not double-count.
- Public `OutboxDispatcherDiagnostics.RecordRetriesBeforeSuccess(int)` helper (negatives clamped to 0).

### Tests

- `RetriesBeforeSuccessHistogramTests`: first-try zero is recorded, the retry count is emitted, negatives clamp to 0.

## [6.5.26] - 2026-06-13

### Added

#### `orionguard.outbox.archival.failures` counter

`Counter<long>` increments when an archival batch throws and is swallowed by the worker's catch block. Operators alert on the rate to catch a stuck archival pipeline that the v6.5.14 liveness gauge alone cannot distinguish from a healthy-but-idle worker.

- Tag: `exception_type`.
- Public `OutboxArchivalDiagnostics.RecordArchiveFailure(string)` helper.
- Completes the archival health picture: batch_size (v6.5.20) + duration_ms (v6.5.21) + liveness (v6.5.14) + failures (v6.5.26).

### Tests

1 fact.

### Migration from v6.5.25

Source-compatible.

## [6.5.25] - 2026-06-12

### Added

#### `orionguard.outbox.dispatcher.batch_size` histogram

`Histogram<int>` of rows claimed per dispatcher poll cycle. Operators graph p99 to spot a dispatcher consistently maxing out `BatchSize` (raise the batch / parallelism) or staying near zero (over-sized polling cadence).

- Zero-row cycles do NOT emit (idle polling is the v6.5.17 idle-poll counter's job).
- Mirrors v0.7.18 Audit and v0.2.16 Patch batch_size shapes on the Guard side - all three outbox families now expose the same poll-outcome triple (batch_size + idle + errors).
- Public `OutboxDispatcherDiagnostics.RecordDispatcherBatchSize(int)` helper.

### Tests

2 facts.

### Migration from v6.5.24

Source-compatible.

## [6.5.24] - 2026-06-12

### Added

#### `orionguard.outbox.dispatcher.dispatch_duration_ms` histogram

`Histogram<double>` measuring per-row `IDomainEventDispatcher.DispatchAsync` wall-clock. Operators graph p99 to isolate consumer-side dispatch cost from queue_lag (which sums queue time + dispatch + commit).

- ALL outcomes emit (try/finally).
- Negative values clamped to 0.
- Public `RecordDispatchDuration(double)` helper.

### Tests

2 facts.

### Migration from v6.5.23

Source-compatible.

## [6.5.23] - 2026-06-12

### Added

#### `IOutboxRowFailureObserver` extensibility

Consumer-supplied observer invoked when the dispatcher swallows a per-row failure. Mirror of v0.2.18 Patch `IDeadLetterSink` on the Guard side.

- `IOutboxRowFailureObserver` interface + `NullOutboxRowFailureObserver` default.
- Optional 8th ctor parameter on `OutboxDispatcherHostedService`.
- Fires for EVERY swallowed failure with `attempt` + `isTerminal` flags.
- Observer fires BEFORE `SaveChangesAsync` so the notification is best-effort.

### Tests

2 facts.

### Migration from v6.5.22

Source-compatible.

## [6.5.22] - 2026-06-11

OrionGuard `orionguard.outbox.enqueued_rows_per_save` histogram (post-commit AsyncLocal pattern).

## [6.5.21] - 2026-06-11

### Added

#### `orionguard.outbox.archival.duration_ms` histogram

`Histogram<double>` measuring `OutboxArchivalHostedService.ArchiveBatchAsync` wall-clock per cycle. Operators graph p99 to spot a backend whose archive write throughput has regressed independently of row count (slow blob sink keeps the dispatcher honest but hurts throughput).

- ALL cycles emit including zero-row (poll cost matters too).
- Recorded around the full `archiver.ArchiveAsync` round-trip.
- Public `OutboxArchivalDiagnostics.RecordArchiveCycleDuration(double)` helper; negative inputs are clamped to 0 to tolerate clock skew across hosts.

### Tests

2 facts.

### Migration from v6.5.20

Source-compatible.

## [6.5.17] - [6.5.20]

Released to NuGet; see GitHub release notes for `orionguard.outbox.dispatcher.poll.idle` counter (v6.5.17), `dispatcher.errors` counter (v6.5.18), `dispatcher.row_size_bytes` histogram (v6.5.19), `archival.batch_size` histogram (v6.5.20).

## [6.5.16] - 2026-06-11

### Added

#### `orionguard.outbox.dispatcher.queue_lag` histogram

`Histogram<double>` exposed via the new `Moongazing.OrionGuard.Outbox.Dispatcher` Meter. Records per-row dispatch lag (`OccurredOnUtc -> ProcessedOnUtc`) on the success path so operators graph p50/p99 and spot dispatcher slowdown BEFORE rows pile up beyond the steady-state dispatched-count rate.

- Recorded in `OutboxDispatcherHostedService` immediately after `dispatcher.DispatchAsync` succeeds.
- Clock-skew negative deltas are clamped to 0 so they do not pull p50 down.
- Public `OutboxDispatcherDiagnostics.RecordQueueLag(double)` so consumer-owned dispatchers can opt in.
- Dead-letter paths do NOT emit; that latency is a separate operational signal.

### Tests

2 new facts.

### Migration from v6.5.15

Source-compatible.

## [6.5.15] - 2026-06-11

### Added

#### `OutboxDashboardOptions.SecurityHeaders`

Every dashboard response now carries production-friendly hardening headers via an endpoint filter on the route group.

Defaults:
- `X-Frame-Options: DENY` (clickjacking guard)
- `X-Content-Type-Options: nosniff` (MIME-sniff guard)
- `Referrer-Policy: no-referrer`
- `Cache-Control: no-store`

The filter writes headers BEFORE the response body so they apply even when the endpoint short-circuits via `Results.NotFound()`.

Consumers can set `SecurityHeaders` to an empty dictionary to disable defaults, or assign a custom dictionary to replace them entirely (no merge).

### Tests

3 new facts.

### Migration from v6.5.14

Source-compatible. Existing deployments inherit the defaults transparently.

## [6.5.14] - 2026-06-11

### Added

#### `OutboxArchivalHealthCheck` and `OutboxArchivalState`

`IHealthCheck` that watches the `OutboxArchivalHostedService` liveness via a shared `OutboxArchivalState` singleton. Operators wire it into ASP.NET Core / generic-host pipelines so a stuck archival worker downgrades the `/health` probe before rows pile up.

- `OutboxArchivalState.RecordSuccessfulBatch(DateTime)` called by the hosted service after every batch (including 0-row batches - the goal is liveness, not throughput).
- `OutboxArchivalHealthCheck` returns `Healthy` / `Degraded` / `Unhealthy` based on the elapsed time since the last successful batch.
- `OutboxArchivalHealthCheckOptions` (`DegradedAfter` default 5 min, `UnhealthyAfter` default 15 min) validated at construction.
- Pre-startup state (no batch yet) reported as `Degraded` so operators can tell "warming up" apart from "stuck".
- New 6-arg hosted-service ctor wires the optional state mirror; existing 5- and 4-arg ctors still work (zero-binary-break).
- `Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions` added as a package reference.

### Tests

6 new facts.

### Migration from v6.5.13

Source-compatible.

```csharp
services.AddSingleton<OutboxArchivalState>();
services.AddHealthChecks()
    .AddCheck<OutboxArchivalHealthCheck>("orionguard-outbox-archival");
```

## [6.5.13] - 2026-06-11

### Added

#### `orionguard.outbox.archive.bytes_written` counter

OTel `Counter<long>` exposed via the new `Moongazing.OrionGuard.Outbox.Archival` Meter. Lets operators graph archival throughput / storage growth without scraping S3 / blob listings.

- Tag: `sink` - short identifier each sink supplies (`local-file`, `rotating-file`).
- `OutboxArchivalDiagnostics.RecordBytes(long bytes, string sinkName)` public so consumer-owned sinks (Azure Blob, S3, GCS) can opt in.
- `LocalFileOutboxArchiveSink` and `RotatingFileOutboxArchiveSink` now record on every successful flush.
- Non-positive payloads ignored.

### Tests

3 new facts.

### Migration from v6.5.12

Source-compatible. Custom sinks opt-in by calling `RecordBytes` after their inner write succeeds.

## [6.5.12] - 2026-06-11

### Added

#### `CompositeOutboxArchiveSink` - fan-out to multiple sinks

Deployments wanting archival redundancy can now wire two or more `IOutboxArchiveSink` instances (e.g. S3 + local rotating file) and the composite forwards every write to each in registration order.

- `CompositeOutboxArchiveSink(IEnumerable<IOutboxArchiveSink>, CompositeOutboxArchiveSinkMode)`.
- `FailFast` (default): first failure aborts and bubbles. Use when cross-sink consistency matters.
- `BestEffort`: every sink is called; AggregateException is thrown ONLY if every sink threw. Use when archive needs to land in at least one destination.
- Cancellation propagates without consuming remaining sinks.
- Sequential fan-out by design - cloud-object-store clients benefit from serial calls more than from fan-out concurrency.

### Tests

8 new facts.

### Migration from v6.5.11

Source-compatible.

## [6.5.11] - 2026-06-11

### Added

#### `RetryingOutboxArchiveSink` decorator

Decorator that wraps any `IOutboxArchiveSink` with jittered exponential retry. Useful for cloud-object-store sinks (S3, Azure Blob, GCS) where transient 503 / socket errors are common - instead of failing the archival sweep on the first transient failure, the decorator retries up to `MaxAttempts` times before giving up.

- `RetryingOutboxArchiveSink(inner, options)`.
- `RetryingOutboxArchiveSinkOptions`: `MaxAttempts` (default 5), `BaseDelay` (100 ms), `MaxDelay` (5 s), `IsRetryable` predicate (default retries everything), `RandomFactory` for seeded testing.
- Backoff: `BaseDelay * 2^(attempt-1)` capped at `MaxDelay`, jittered in `[0.5x, 1.0x]` of the computed value.
- Cancellation propagates immediately without consuming a retry slot.
- Options validated at construction.

### Tests

6 new facts.

### Migration from v6.5.10

Source-compatible.

## [6.5.10] - 2026-06-11

### Added

#### `RotatingFileOutboxArchiveSink` - production-grade local archive sink

Extends the v6.5.9 `IOutboxArchiveSink` abstraction. v6.5.9 shipped `LocalFileOutboxArchiveSink` as a single-file reference; v6.5.10 ships the production-grade rotating variant for deployments that ship archives to a local NFS / EFS mount + a downstream batch job that uploads to cold storage.

- `RotatingFileOutboxArchiveSink` writes JSON Lines payloads under `{Root}/{yyyy-MM-dd}/{keyHint}-{shard:D4}.jsonl`.
- One subdirectory per UTC day so the operator can prune by date.
- Within a day, files split at `MaxFileBytes` (default 64 MiB). The sink appends to the lowest-numbered shard whose size + payload fits under the cap; otherwise it rolls to the next shard.
- `MaxShardsPerDay` (default 9999) bounds the per-day shard count - reaching the cap throws so a runaway archive load surfaces as an explicit configuration error rather than silently overwriting.

### Tests

6 new facts.

### Migration from v6.5.9

Source-compatible.

## [6.5.9] - 2026-06-10

### Added

#### `IOutboxArchiveSink` + `BlobOutboxArchiver` for off-box archival

Extends the v6.5.6 `IOutboxArchiver` strategy hook. v6.5.6 introduced the `CopyToTableOutboxArchiver` archive-table pattern; v6.5.9 ships the off-box version: rows leave the database entirely after archival, landing in a consumer-supplied blob sink (S3, Azure Blob, GCS, local filesystem).

- `IOutboxArchiveSink` abstraction with a single `WriteAsync(string keyHint, ReadOnlyMemory<byte> payload, ct)` call.
- `BlobOutboxArchiver` orchestrates SELECT eligible rows -> serialise to newline-delimited JSON (`.jsonl`) -> `sink.WriteAsync` -> `ExecuteDelete` with re-checked eligibility (matches v6.5.6 safety).
- `LocalFileOutboxArchiveSink` reference implementation writes `.jsonl` files to a local directory.
- Sink failure aborts the sweep WITHOUT deleting; rows stay on the live table for the next tick.

### Tests

6 new facts; 30 dashboard + 15 archival facts total.

### Migration from v6.5.8

Source-compatible.

## [6.5.8] - 2026-06-10

### Added

#### Outbox dashboard cursor-based pagination

The v6.5.4-6.5.7 `/failed` endpoint used offset pagination (`?page=N&size=M`). Offset pagination grows expensive on large failed-message tables (each page does an OFFSET scan of every preceding row) and is unstable when new rows arrive mid-paging (the same row can appear on consecutive pages, or be skipped entirely). v6.5.8 adds a cursor variant that uses keyset pagination: the next page's WHERE predicate seeks past the last-seen `(sortKey, Id)` pair so the database does an index seek instead of an OFFSET scan.

- **`GET /_orion/outbox/failed/cursor?cursor=<token>&size=N&sort=<axis>`**: paginated endpoint that returns up to `size` rows past the cursor's last position, plus the `nextCursor` for the page after. The first call sends no cursor; subsequent calls send the `nextCursor` from the previous response.
- **`OutboxFailedCursor`** opaque token: base64Url-encoded `(LastOccurredOnUtcTicks, LastId, LastRetryCount, Sort)`. Internal-only - clients treat it as opaque. The sort axis is encoded INTO the cursor so a caller cannot switch sort mid-paging and get duplicate / skipped rows.
- **One-extra-row peek**: the endpoint fetches `size + 1` rows; if the peek row exists, slice it off and emit a `nextCursor` based on the LAST returned row (not the peek). Detects "is there another page" without a separate COUNT round-trip.
- **Invalid / garbage cursors** fall back to the no-cursor path (start of results) rather than 400. Matches the dashboard's existing forgiving query-string behaviour (invalid `sort` also falls back rather than failing the request).
- **Stable tiebreaker**: every sort axis appends `Id` as a secondary key so rows with identical `OccurredOnUtc` / `RetryCount` stay in deterministic order across pages.

### Tests

5 new facts: cursor pages through 25 rows in chunks of 10 with no duplicates / no skips, `hasNextPage=true` + `nextCursor` set when more rows remain, `hasNextPage=false` + `nextCursor=null` on last page, invalid cursor falls back to beginning, cursor locks sort axis (switched `?sort=` on a follow-up call is ignored). 30 dashboard facts total.

### Migration from v6.5.7

Source-compatible. The existing `/failed` offset endpoint continues to work; the cursor endpoint is an additive new route. Operator UIs that page through large tables should switch to the cursor variant:

```
GET /_orion/outbox/failed/cursor?size=50&sort=MostRetries
-> { items: [...], nextCursor: "AAAB...", hasNextPage: true, ... }
GET /_orion/outbox/failed/cursor?cursor=AAAB...&size=50
-> next page
```

## [6.5.7] - 2026-06-10

### Added

#### Dashboard sort + richer pagination metadata

The v6.5.4 `/failed` listing only supported `OccurredOnUtc` ascending and returned `{page, size, total, items}`. v6.5.7 adds an explicit sort axis and the navigation metadata operators expect for paged tables.

- **`OutboxFailedListingSort`** enum: `OldestFirst` (default), `NewestFirst`, `MostRetries`. Query string: `?sort=newestfirst` (case-insensitive enum name match).
- **`OutboxDashboardOptions.DefaultSort`** controls the default when the consumer omits the query string. Default `OldestFirst` so operators triage the longest-failing rows first.
- **Response shape extended** with `totalPages`, `hasNextPage`, `hasPreviousPage`, and `sort` (the resolved enum name). Existing `page` / `size` / `total` / `items` fields unchanged; consumers depending on the v6.5.4-6.5.6 shape continue to parse cleanly.
- **`MostRetries`** order falls back to `OccurredOnUtc` ascending as a stable tiebreaker so rows with the same retry count stay in deterministic order.

### Tests

4 new facts: pagination metadata returned (total/totalPages/hasNextPage/hasPreviousPage), `NewestFirst` orders descending by `OccurredOnUtc`, `MostRetries` orders descending by `RetryCount`, invalid sort falls back to default. 24 dashboard facts total.

`ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))` added to the in-memory test fixture - additional fixtures pushed the EF Core internal-service-provider count past the framework's 20-instance warning threshold.

### Migration from v6.5.6

Source-compatible. Existing query strings without `?sort=...` use the configured `DefaultSort`.

```csharp
app.MapOutboxDashboard<AppDbContext>(o =>
{
    o.DefaultSort = OutboxFailedListingSort.MostRetries; // triage most-retried first by default
});
```

## [6.5.6] - 2026-06-10

### Added

#### `IOutboxArchiver` strategy hook

Outbox archival worker (pre-existing) gains a pluggable strategy hook so consumers can swap the default delete-on-retention behaviour for compliance-friendly archive-table copies without forking the hosted service.

- **`IOutboxArchiver`** interface: `Task<int> ArchiveAsync(DbContext, DateTime cutoff, OutboxArchivalOptions, CancellationToken)`. Implementations honour `OutboxArchivalOptions.PreserveDeadLetters` and `BatchSize`; return value is the row count for the hosted service's logging / metrics.
- **`DeleteOutboxArchiver`**: the default. Same `ExecuteDeleteAsync` semantics as the pre-v6.5.6 inline code; ordering by `ProcessedOnUtc` so the oldest rows leave first. Honours `PreserveDeadLetters`.
- **`CopyToTableOutboxArchiver<TArchiveRow>`** (generic): COPIES eligible rows into a consumer-owned archive table, then deletes the originals in the SAME transaction so no row is lost across the boundary. Consumer supplies a `Func<OutboxMessage, TArchiveRow>` projection. Useful for retention regimes that must keep the dispatched-event trail past the live-table window.
- **`OutboxArchivalHostedService`** ctor gains an optional `IOutboxArchiver?` parameter (position 5, after the existing `ILogger?`). Null defaults to `DeleteOutboxArchiver` so existing wiring keeps working without changes. `ArchiveBatchAsync` now delegates to the archiver instead of executing the delete inline.

### Tests

2 new facts cover the strategy hook: `CopyToTableOutboxArchiver` copies rows to a separate table AND deletes originals; a custom no-op archiver is honoured (default delete path is bypassed). 9 archival facts total.

### Migration from v6.5.5

Source-compatible. Existing `new OutboxArchivalHostedService(opts, scopeFactory, distributedLock, logger)` calls keep the delete-on-retention behaviour. Opt into copy-to-table archival:

```csharp
services.AddSingleton<IOutboxArchiver>(_ => new CopyToTableOutboxArchiver<OutboxArchiveRow>(m => new OutboxArchiveRow
{
    Id = m.Id,
    EventType = m.EventType,
    Payload = m.Payload,
    OccurredOnUtc = m.OccurredOnUtc,
    ArchivedOnUtc = DateTime.UtcNow,
}));
```

## [6.5.5] - 2026-06-10

### Added

#### Outbox dashboard mutation surface

Completes the operator workflow started in v6.5.4. The read surface listed failed / dead-lettered messages; v6.5.5 lets operators actually act on them.

- **`POST /_orion/outbox/{id:guid}/replay`** finds the row, clears `RetryCount`, clears `Error`, clears `ProcessedOnUtc` (in case the dispatcher had already dead-lettered it), commits via `SaveChangesAsync`, and returns `200 OK` with `{ id, action: "replay" }`. Unknown id -> `404 Not Found`.
- **`POST /_orion/outbox/{id:guid}/discard`** stamps `ProcessedOnUtc = UtcNow` so the dispatcher loop skips the row on its next pass; `Error` and `RetryCount` stay intact so future operators see the history. Idempotent: a row that already has `ProcessedOnUtc` set returns `200 OK` with `{ id, action: "discard", note: "already processed" }` and the audit hook does NOT fire again. Unknown id -> `404 Not Found`.
- **`OutboxDashboardOptions.OnMutation`** optional `Func<OutboxMutationEvent, Task>` audit hook fires after a successful replay / discard. The dashboard itself never writes audit rows so consumers stay in control of storage shape and retention. `OutboxMutationEvent` carries `Action`, `OutboxMessageId`, `HttpContext`, and `OccurredAtUtc` so consumers stamp `User.Identity.Name`, claims, IP, etc. Throwing from the hook does NOT roll back the database commit.
- **`OutboxDashboardOptions.EnableMutations`** (default `true`) - set `false` for strictly read-only mounts. When false the `POST` endpoints are not registered at all (return `404`) while the existing `/failed` listing stays available.

### Tests

5 new endpoint tests (replay path, discard path, replay 404, discard 404, discard idempotency) + 3 mutation-hook tests (fires on replay, does NOT fire on 404, does NOT fire on already-processed discard) + 2 read-only-mode tests (POST returns 404 when disabled, GET still works). 18 facts total in the dashboard test suite.

### Migration from v6.5.4

Source-compatible. Existing dashboard registrations get `replay` and `discard` automatically; opt out with `o.EnableMutations = false`.

```csharp
app.MapOutboxDashboard<AppDbContext>(o =>
{
    o.OnMutation = async evt =>
    {
        await auditWriter.WriteAsync(new
        {
            evt.Action,
            evt.OutboxMessageId,
            User = evt.HttpContext.User.Identity?.Name,
            Ip = evt.HttpContext.Connection.RemoteIpAddress?.ToString(),
            evt.OccurredAtUtc,
        });
    };
});
```

## [6.5.4] - 2026-06-09

### Added

#### `Moongazing.OrionGuard.Outbox.Dashboard` (NEW PACKAGE)

Read-only operator dashboard for the OrionGuard outbox. Maps an authorized HTTP endpoint group that lists failed / poisoned messages from the consumer's DbContext.

- **`MapOutboxDashboard<TDbContext>(this IEndpointRouteBuilder, configure?)`**: registers a route group under the configured `RoutePrefix` (default `/_orion/outbox`). The group calls `RequireAuthorization()` by default so the host's fallback policy applies; consumers pass a named policy or opt anonymous (NOT recommended).
- **`GET /_orion/outbox/failed?page=N&size=M`**: paginated listing of rows where `RetryCount >= FailedRetryThreshold` (default 3, matching v6.5.0 dispatcher default) AND `ProcessedOnUtc IS NULL`. Pagination clamps to `MaxPageSize` (default 100); response includes `{page, size, total, items[]}`.
- **`OutboxFailedMessageRow`**: read-only projection excluding `Payload` to limit blast-radius if authorization is misconfigured. Error text is truncated to `ErrorTruncationLength` (default 1024 chars); full text remains in the database.
- **`OutboxDashboardOptions` validation**: empty `RoutePrefix`, non-positive page sizes / retry threshold, and negative truncation length all throw `InvalidOperationException` at endpoint registration time so misconfigured deployments fail fast.

### Deferred

- **Replay / discard actions** -> v6.5.5 (originally targeted v6.5.4; the read-only surface ships now so operators see poisoned messages while the mutation surface gets a focused review)

### Migration from v6.5.3

Source-compatible. The dashboard is an opt-in add-on package: install `OrionGuard.Outbox.Dashboard`, register your DbContext via `AddDbContext<TDbContext>(...)`, then call `app.MapOutboxDashboard<TDbContext>()` from your endpoint configuration.

```csharp
app.MapOutboxDashboard<AppDbContext>(o =>
{
    o.AuthorizationPolicyName = "OutboxOps";
});
```

## [6.5.3] - 2026-06-09

### Added

#### `Moongazing.OrionGuard.Outbox.SqlServerBroker` (NEW PACKAGE)

SQL Server Service Broker backed `IOutboxWakeSignal` for `OrionGuard.EntityFrameworkCore`. Sibling to v6.5.2's PostgresNotify add-on for the SQL Server provider.

- **`SqlServerBrokerOutboxWakeSignal`**: `BackgroundService` that holds a dedicated `SqlConnection` and runs `WAITFOR (RECEIVE ... FROM <queue>) TIMEOUT <ms>`. On RECEIVE, the in-process channel wakes the dispatcher. Reconnect loop with exponential back-off bounded by `MaxReconnectDelay`.
- **`SqlServerBrokerOptions`**: `ConnectionString` (required), `QueueName` (default `OrionGuardOutboxQueue`), `ServiceName` (default `OrionGuardOutboxService`), `ReceiveTimeout` (default 30s), reconnect-delay tuning.
- **`SqlServerBrokerSetupSql.Create / Drop`**: idempotent T-SQL helpers that install the Service Broker message type, contract, queue, service, and AFTER INSERT trigger. The package deliberately does NOT auto-install schema changes; consumers run the SQL once via an EF Core migration AFTER enabling Service Broker on the database (`ALTER DATABASE [...] SET ENABLE_BROKER`).
  - Bracketed-identifier escape (`]` -> `]]`) for table / queue / service names.
  - Quoted-literal escape (`'` -> `''`) for service / contract / message-type names.
  - Double-quoted escape (`'` -> `''''`) for the EXEC-nested trigger body's SEND TO SERVICE literal, so a service name containing a single quote does not malform the EXEC string.
- **DI**: `services.AddSqlServerBrokerOutboxWakeSignal(o => o.ConnectionString = "...")` registers signal + hosted service in one call and replaces the default `NullOutboxWakeSignal`.

### Fixed

- CI `Pack All Projects` step now packs the new `SqlServerBroker` add-on. PostgresNotify and Locks.Redis lines are preserved.

### Deferred from v6.5.3

- **Outbox dead-letter UI surface** -> v6.5.4 (unchanged from the v6.5.2 deferral list)

### Migration from v6.5.2

Source-compatible. Add-on is opt-in: install `OrionGuard.Outbox.SqlServerBroker`, enable Service Broker on the database once, install the trigger via SQL migration, register the signal:

```csharp
services.AddSqlServerBrokerOutboxWakeSignal(o =>
{
    o.ConnectionString = "Server=db;Database=app;User Id=app;Password=app;TrustServerCertificate=true";
});
services.AddOrionGuardEfCore<AppDbContext>(opts => opts.UseOutbox());
```

## [6.5.2] - 2026-06-09

### Added

#### `Moongazing.OrionGuard.Outbox.PostgresNotify` (NEW PACKAGE)

Postgres LISTEN/NOTIFY backed `IOutboxWakeSignal` for `OrionGuard.EntityFrameworkCore`. Lifts the v6.5.1 polling-only default into an event-driven wake on every committed outbox row when the consumer's database is PostgreSQL.

- **`PostgresNotifyOutboxWakeSignal`** - `BackgroundService` that holds a dedicated `NpgsqlConnection`, runs `LISTEN "<channel>";`, and signals the dispatcher via the in-process `Channel<bool>` on every notification. Reconnect loop with exponential back-off bounded by `PostgresNotifyOptions.MaxReconnectDelay`.
- **`PostgresNotifyOptions`** - `ConnectionString` (required), `ChannelName` (default `orionguard_outbox`), `InitialReconnectDelay`, `MaxReconnectDelay`.
- **`PostgresNotifyTriggerSql`** - static helpers `Create(tableName, channelName)` / `Drop(tableName, channelName)` returning SQL that installs a `pg_notify`-emitting AFTER INSERT trigger. The package does NOT auto-install the trigger; consumers run the SQL once via an EF Core migration. Channel name is sanitised into a SQL identifier for the function / trigger names so two outbox tables in the same database do not collide.
- **DI**: `services.AddPostgresNotifyOutboxWakeSignal(o => o.ConnectionString = "...");` registers the signal + hosted service in one call, replacing the default `NullOutboxWakeSignal`.

### Fixed

- CI pack list: `Moongazing.OrionGuard.Locks.Redis` was reintroduced into the `Pack All Projects` step. The line was lost during the v6.5.0 -> v6.5.1 rebase; this restores it alongside the new `OrionGuard.Outbox.PostgresNotify` pack call so both add-on packages publish to NuGet on release.

### Deferred from v6.5.2

- **`Moongazing.OrionGuard.Outbox.SqlServerBroker`** add-on (SQL Server Service Broker push backend) -> v6.5.3
- **Outbox dead-letter UI surface** -> v6.5.4

`docs/ROADMAP.md` reflects the targets.

### Migration from v6.5.1

Source-compatible. The new add-on package is opt-in: install `OrionGuard.Outbox.PostgresNotify`, install the trigger via SQL migration, register the signal:

```csharp
services.AddPostgresNotifyOutboxWakeSignal(o =>
{
    o.ConnectionString = "Host=db;Database=app;Username=app;Password=app";
});
services.AddOrionGuardEfCore<AppDbContext>(opts => opts.UseOutbox());
```

Consumers staying on the v6.5.1 in-process channel signal or the v6.5.0 polling default see no behaviour change.

## [6.5.1] - 2026-06-04

### Added

#### Push-based dispatch contract (`Moongazing.OrionGuard.EntityFrameworkCore`)

The push-based outbox dispatcher promised in v6.5.0 lands as a contract + in-process implementation in v6.5.1. The concrete cross-process backends (Postgres `LISTEN/NOTIFY` and SQL Server Service Broker) ship as separate add-on packages in v6.5.2 and v6.5.3 respectively. The dispatcher loop now honours the new contract; consumers who do not opt in see identical v6.5.0 behaviour because the default implementation is polling-only.

- **`IOutboxWakeSignal`** abstraction in `Moongazing.OrionGuard.EntityFrameworkCore.Outbox.Push`. Two methods: `WaitForNextTickAsync(pollingInterval, ct)` (upper-bounded wait the dispatcher uses between batches) and `SignalAsync(ct)` (called by enqueue paths or push backends to wake the dispatcher immediately).
- **`NullOutboxWakeSignal`** - default registration. Polling-only; behaviour byte-for-byte identical to v6.5.0.
- **`ChannelOutboxWakeSignal`** - in-process bounded `Channel<bool>` implementation. Useful for unit tests and for single-process deployments where the `SaveChangesInterceptor` can publish a wake directly. Signals coalesce: repeated `SignalAsync` calls while one wait is pending all complete a single wake.
- **`OutboxDispatcherHostedService` constructor** gains an optional `IOutboxWakeSignal` parameter (defaults to `NullOutboxWakeSignal` when null). Polling-interval upper-bound is always honoured, so a misbehaving signal cannot stall dispatch indefinitely.
- **DI default** in `AddOrionGuardEfCore(...)` registers `NullOutboxWakeSignal` via `TryAddSingleton<IOutboxWakeSignal, NullOutboxWakeSignal>()`. Consumers replace it before `AddOrionGuardEfCore` to opt in (`services.AddSingleton<IOutboxWakeSignal, ChannelOutboxWakeSignal>()`).

### Deferred from v6.5.1

The concrete push backends will land as add-on packages so consumers can adopt one without dragging the others into their dependency graph:

- **`Moongazing.OrionGuard.Outbox.PostgresNotify`** package (Postgres `LISTEN/NOTIFY`-backed `IOutboxWakeSignal`) -> v6.5.2.
- **`Moongazing.OrionGuard.Outbox.SqlServerBroker`** package (SQL Server Service Broker-backed `IOutboxWakeSignal`) -> v6.5.3.

The outbox dead-letter UI from the original v6.5.0 plan stays at v6.5.4.

`docs/ROADMAP.md` reflects the new targets.

### Migration from v6.5.0

Source-compatible. No DI registration change is required. Consumers that opt into the new contract:

```csharp
// Single-process - in-process push so SaveChangesInterceptor can wake the dispatcher.
services.AddSingleton<IOutboxWakeSignal, ChannelOutboxWakeSignal>();
services.AddOrionGuardEfCore<AppDbContext>(o => o.UseOutbox());
```

The dispatcher continues to acquire the distributed lock before each batch, so multi-instance correctness is unchanged.

## [6.5.0] - 2026-06-01

### Added

#### `OrionGuard.Locks.Redis` (NEW PACKAGE)

- Redis backend for the `IDistributedLock` primitive introduced in v6.4.0. Bridges OrionGuard's `IDistributedLock` / `IDistributedLockHandle` to OrionLock's raw `IDistributedLockProvider`, with `RedisLockProvider` from `OrionLock.Redis` (v0.2.3) as the default wiring.
- `OrionLockBridgeDistributedLock` adapter — non-blocking acquire, owner-token release via Lua compare-and-delete on Redis. No watchdog renewal, no reentrancy. Aligns with OrionGuard's at-least-once outbox semantics where lease loss is tolerated and consumer event handlers must be idempotent.
- DI extensions on `OrionGuardEfCoreOptions`:
  - `UseOrionLockRedis(connectionString, configure)` — builds a singleton `IConnectionMultiplexer` from the supplied connection string.
  - `UseOrionLockRedis(configure)` — uses an already-registered `IConnectionMultiplexer` from DI (preferred when the application already shares a Redis connection).
- `RedisLockOptions` (re-exported via the OrionLock.Redis transitive dependency) configures `KeyPrefix` (default `orionlock:`) and `Database` (default -1).

### Changed

- `Moongazing.OrionGuard.EntityFrameworkCore` now declares `InternalsVisibleTo` for `Moongazing.OrionGuard.Locks.Redis` and its test assembly so the bridge can use the internal `OrionGuardEfCoreOptions.ServiceCustomizations` hook the same way the built-in `UseDistributedLock<T>()` does. No public API change. Other future `OrionGuard.Locks.*` backends (Consul, Postgres advisory locks, etc.) will be added to the same list when they ship.

### Deferred from v6.5.0

The original v6.5.0 milestone in `docs/ROADMAP.md` listed four features. To keep this minor focused and reviewable, two were de-scoped from v6.5.0 and re-targeted:

- **Push-based outbox dispatcher** — now targets **v6.5.1**. Will replace the v6.4 polling loop with a `PostgresLISTEN` / `SqlServerBrokerNotification` push backend on EF Core providers that support it, with a clean fallback to polling.
- **Outbox dead-letter UI surface** — now targets **v6.5.2**. A read-only `MapOutboxDashboard` endpoint listing failed/poisoned messages with replay and discard actions; authorization-required by default.

### Migration from v6.4.2

- **No breaking source changes.**
- **Adopting the Redis lock backend (multi-instance consumers who already use Redis):**
  ```csharp
  services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect("localhost:6379"));
  services.AddOrionGuardEfCore<AppDbContext>(opts => opts
      .UseOutbox()
      .UseOrionLockRedis(o => o.KeyPrefix = "myapp:outbox:"));
  ```
  Or with a connection string:
  ```csharp
  services.AddOrionGuardEfCore<AppDbContext>(opts => opts
      .UseOutbox()
      .UseOrionLockRedis("localhost:6379"));
  ```
- **Consumers staying on the default DB-backed `SkipLockedDistributedLock`** require no changes — it is still wired automatically by `AddOrionGuardEfCore` in Outbox mode.

## [6.4.2] - 2026-05-26

### Changed

- Logo now ships with a cream (#F7F1E3) background instead of transparent. Improves contrast against dark-mode README rendering and NuGet package card backgrounds. No functional change.

## [6.4.1] - 2026-05-23

### Changed

- New minimalist family-style logo (shield with an Orion-star center, indigo line-art, no badge ring or curved text) replaces the v6.x circular emblem. Applied to the README and to every package's NuGet icon. No code changes.

## [6.4.0] - 2026-05-20

### Added

#### Business Rule ergonomics (`Moongazing.OrionGuard`)
- `BusinessRule` and `AsyncBusinessRule` abstract base classes implementing `IBusinessRule` / `IAsyncBusinessRule`. `MessageKey` defaults to the CLR type name.
- `Guard.AgainstBrokenRule(IBusinessRule)` and `Guard.AgainstBrokenRuleAsync(IAsyncBusinessRule, CancellationToken)` static helpers. `Entity.CheckRule` / `CheckRuleAsync` now delegate to these helpers (behaviour unchanged).

#### ASP.NET Core ProblemDetails (`Moongazing.OrionGuard.AspNetCore`)
- `OrionGuardExceptionHandler` now produces a 422 `ValidationProblemDetails` for `BusinessRuleValidationException` (previously fell through to the framework default 500).
- New `OrionGuardAspNetCoreOptions.BusinessRuleStatusCode` (default 422) for clients that require 400.
- New `OrionGuardProblemDetailsFactory.Create(BusinessRuleValidationException)` overload — `errors` keyed by `RuleName`, `Type` = `https://moongazing.dev/orionguard/problems/business-rule-violation`.

#### Outbox production-hardening (`Moongazing.OrionGuard.EntityFrameworkCore`)
- `IDistributedLock` / `IDistributedLockHandle` abstractions. Default `SkipLockedDistributedLock` uses an `OutboxLock` row per key in `OrionGuard_OutboxLocks` (provider-agnostic via EF Core raw SQL). Multi-instance outbox workers no longer double-dispatch.
- `NullDistributedLock` no-op implementation for single-instance consumers who do not want to apply the new migration. Wire with `opts.UseOutbox().UseDistributedLock<NullDistributedLock>()`.
- `OutboxTypeMapRegistry` — opt-in logical-name to CLR type mapping. The `SaveChanges` interceptor prefers logical names when registered; the dispatcher resolves them on read. Falls back to AQN when no mapping exists (toggle via `OutboxTypeMapOptions.AllowAssemblyQualifiedNameFallback`).
- `OutboxArchivalHostedService` — opt-in periodic deletion of processed outbox rows. Default 30-day retention, 1-hour polling, dead-letter rows preserved.
- `OutboxOptions.LockKey` (default `"orion_guard_outbox_dispatcher"`) and `OutboxOptions.LockLeaseDuration` (default 30s).
- `OrionGuardEfCoreOptions.UseDistributedLock<T>()`, `UseOutboxTypeMap(...)`, `UseOutboxArchival(...)`.

### Changed
- `Entity.CheckRule` / `Entity.CheckRuleAsync` internally delegate to `Guard.AgainstBrokenRule` / `Guard.AgainstBrokenRuleAsync`. Public behaviour unchanged.
- `OutboxDispatcherHostedService` constructor expanded with `IDistributedLock`, `OutboxTypeMapRegistry`, and `OutboxTypeMapOptions` parameters (optional, defaulted). The DI factory in `AddOrionGuardEfCore` updates accordingly; consumers using only DI are unaffected.

### Deprecated

- The `[StronglyTypedId<TValue>]` source generator is soft-deprecated in favour of the standalone **OrionKey** package (`[OrionId<TValue>]` / `[OrionId<TValue, TStrategy>]`). Existing usages keep compiling and the generator keeps emitting; each `[StronglyTypedId]` usage now raises a CS0618 warning with migration guidance. The generator will be removed in v7.0.0. The manual `StronglyTypedId<TValue>` record, `IStronglyTypedId<TValue>`, and the related guards are unaffected. See `docs/migrations/stronglytypedid-to-orionkey.md`.

### Migration from v6.3.0

- **No breaking source changes.**
- **Distributed locking (recommended for multi-instance deployments):**
  Add an EF Core migration that creates `OrionGuard_OutboxLocks` — see `docs/migrations/v6.4.0-outbox-locks.md`.
  No code change needed when using `AddOrionGuardEfCore` — `SkipLockedDistributedLock` is wired automatically.
- **Single-instance consumers who do NOT want to apply the migration:**
  `opts.UseOutbox(...).UseDistributedLock<NullDistributedLock>()`.
- **Type-safe outbox payloads (optional):**
  `opts.UseOutbox(...).UseOutboxTypeMap(r => r.Map<UserRegistered>("user.registered"));`
- **Outbox archival (optional):**
  `opts.UseOutbox(...).UseOutboxArchival(a => a.RetentionPeriod = TimeSpan.FromDays(60));`
- **`BusinessRule` base class (optional):** existing `IBusinessRule` implementations work unchanged.
- **`Guard.AgainstBrokenRule` (additive):** `Guard.AgainstBrokenRule(new OrderMustHaveItems(order));`
- **`BusinessRuleValidationException` to 422 ProblemDetails (automatic):** customize via `OrionGuardAspNetCoreOptions.BusinessRuleStatusCode`.

### Roadmap

- v6.5+: Redis / Consul `IDistributedLock` implementations as extension packages. Push-based outbox dispatch (`LISTEN/NOTIFY`, `SqlDependency`). Audit-trail copy-before-delete for archival.

## [6.2.0] - 2026-04-19

### Added

- `Moongazing.OrionGuard.Domain.Primitives.IStronglyTypedId<TValue>` marker interface implemented by both the `StronglyTypedId<TValue>` abstract record and source-generated strongly-typed id structs.
- `Moongazing.OrionGuard.Domain.Events.DomainEventBase` abstract record — auto-assigns `EventId` (new `Guid`) and `OccurredOnUtc` (UTC timestamp) at construction, with `init` accessors for test overrides via `with` expressions.
- Source-generated strongly-typed ids now implement `IParsable<TSelf>` and `ISpanParsable<TSelf>` — ASP.NET Core minimal API route/query/form binding works out of the box.

### Changed

- `AgainstDefaultStronglyTypedId` guard receiver widened from `StronglyTypedId<TValue>` to `IStronglyTypedId<TValue>`. Source-compatible with v6.1.0 callers.
- The `[StronglyTypedId<TValue>]` source generator no longer emits its EF Core `ValueConverter` companion when the consumer project does not reference `Microsoft.EntityFrameworkCore`. JSON and TypeConverter companions emit unconditionally.
- **NuGet PackageIds for sub-packages** dropped the `Moongazing.` prefix. Install as `OrionGuard.AspNetCore`, `OrionGuard.Blazor`, `OrionGuard.Generators`, `OrionGuard.Grpc`, `OrionGuard.MediatR`, `OrionGuard.OpenTelemetry`, `OrionGuard.SignalR`, `OrionGuard.Swagger`. The v6.0.0 and v6.1.0 packages remain published under the old IDs; v6.2.0 ships under the new ones. **C# namespaces are unchanged** — `using Moongazing.OrionGuard.AspNetCore;` continues to work.

### Migration from v6.1.0

- Update package references:
  ```bash
  dotnet remove package Moongazing.OrionGuard.AspNetCore
  dotnet add package OrionGuard.AspNetCore
  ```
  Repeat for each sub-package you use. Source code (`using` statements, type names) stays the same — only the NuGet ID changes.

### Roadmap

- v6.3.0 (next): Domain event dispatcher, MediatR bridge, EF Core `SaveChanges` interceptor.
- v6.4.0: Full `BusinessRule` base class, `Guard.Against.BrokenRule`, ASP.NET Core ProblemDetails mapping.

## [6.3.0] - 2026-05-10

### Added

#### Domain event dispatcher (`Moongazing.OrionGuard.Domain.Events`)

- `IDomainEventDispatcher` and `IDomainEventHandler<TEvent>` abstractions.
- `ServiceProviderDomainEventDispatcher` — default implementation resolving handlers from `IServiceProvider`.
- `DomainEventDispatchOptions` with `DispatchMode.SequentialFailFast` (default), `SequentialContinueOnError`, and `Parallel`.
- `services.AddOrionGuardDomainEvents()` and `services.AddOrionGuardDomainEventHandlers(...)` DI helpers — idempotent (use `TryAdd*` internally) and safely composable.

#### MediatR bridge (`OrionGuard.MediatR`)

- `MediatRDomainEventDispatcher` delegates to MediatR's `IPublisher`. Consumer events opt in by adding `: INotification` to their record declaration; the bridge throws `InvalidOperationException` for events that do not. No wrapper types — handlers stay as natural `INotificationHandler<TEvent>`, so MediatR pipeline behaviours compose naturally.
- `services.AddOrionGuardMediatRDomainEvents()` swaps the registered dispatcher.

#### `OrionGuard.EntityFrameworkCore` (NEW PACKAGE)

- `DomainEventSaveChangesInterceptor` — pulls events from tracked `IAggregateRoot` instances at `SavingChangesAsync` and either dispatches them post-commit (`Inline` mode, default) or persists them as `OutboxMessage` rows in the same transaction (`Outbox` mode).
- `OutboxMessage` entity, `OutboxMessageEntityTypeConfiguration`, and `OutboxOptions` (`PollingInterval`, `BatchSize`, `MaxRetries`, `TableName`).
- `OutboxDispatcherHostedService` — `BackgroundService` that polls unprocessed rows, deserializes events, dispatches via `IDomainEventDispatcher`, increments `RetryCount` on failure, dead-letters after `MaxRetries`.
- W3C trace context propagation — outbox rows record `TraceParent` and `TraceState`; the worker resumes the parent activity context per message so end-to-end traces span the worker boundary.
- `services.AddOrionGuardEfCore<TDbContext>(o => o.UseInline() | o.UseOutbox())`.

#### `OrionGuard.Testing` (NEW PACKAGE)

- `DomainEventCapture` and `DomainEventAssertions` for fluent unit-test assertions.
- `InMemoryDomainEventDispatcher` for integration tests.
- Framework-agnostic — no xUnit / NUnit / FluentAssertions dependency. Throws `DomainEventAssertionException`, which any test runner treats as a failure.

#### `OrionGuard.OpenTelemetry`

- `OrionGuardDomainEventTelemetry` — `ActivitySource` + `Meter` under `Moongazing.OrionGuard.DomainEvents`, with `EventsDispatched`, `EventsFailed`, `OutboxProcessed`, `OutboxRetries` counters and the `DispatchDuration` histogram.
- `InstrumentedDomainEventDispatcher` decorator — opens a span per dispatch, records counters, sets activity status on exception.
- `services.WithOpenTelemetryDomainEvents()`.

#### AOT compatibility

- `ServiceProviderDomainEventDispatcher` and `OutboxDispatcherHostedService` annotated with `[RequiresUnreferencedCode]` + `[RequiresDynamicCode]`. AOT consumers should use the MediatR bridge or root event/handler types via `[DynamicDependency]`.
- All other v6.3.0 additions (`IDomainEventDispatcher`, `IDomainEventHandler<T>`, `MediatRDomainEventDispatcher`, `DomainEventCapture`, `InMemoryDomainEventDispatcher`, `InstrumentedDomainEventDispatcher`) are reflection-free.

### Migration from v6.2.0

- No breaking changes. Source-compatible.
- Existing `RaiseEvent` / `PullDomainEvents` aggregate code continues to work; events simply do not dispatch unless `AddOrionGuardDomainEvents()` is wired.
- MediatR consumers add `, INotification` to their event records (one-line per event).
- Outbox consumers add an EF Core migration for the `OrionGuard_Outbox` table.

### Roadmap

- v6.4.0: `BusinessRule` base class + `Guard.Against.BrokenRule` + ASP.NET Core ProblemDetails mapping (carries the original v6.3.0 plan); plus distributed locking for multi-instance outbox workers, `OutboxTypeMapRegistry`, archival job.
- v6.5+: Push-based outbox dispatch (`LISTEN/NOTIFY`, `SqlDependency`); event sourcing primitives.

## [6.1.0] - 2026-04-19

### Added

#### DDD Domain Primitives (`Moongazing.OrionGuard.Domain`)

- `ValueObject` abstract base class with component-wise equality via `GetEqualityComponents()`.
- `IValueObject` marker interface for record-based value objects (records get structural equality from the compiler).
- `Entity<TId>` base class with identity equality and `protected static CheckRule` / `CheckRuleAsync` helpers that throw `BusinessRuleValidationException` when a rule is broken.
- `IAggregateRoot` non-generic marker interface — enables consumers (e.g., EF Core interceptors) to discover aggregates without knowing `TId`.
- `AggregateRoot<TId>` base class with `RaiseEvent` (protected) and `PullDomainEvents` (public, atomically returns and clears the buffer).
- `StronglyTypedId<TValue>` abstract positional record (manual-use base; constraint `where TValue : notnull, IEquatable<TValue>`).

#### Abstractions for v6.2.0 / v6.3.0

- `IDomainEvent` interface (`EventId`, `OccurredOnUtc`). Dispatcher abstraction arrives in v6.2.0.
- `IBusinessRule` and `IAsyncBusinessRule` interfaces (`IsBroken`/`IsBrokenAsync`, `MessageKey`, `DefaultMessage`, optional `MessageArgs`). Full `BusinessRule` base class + `Guard.Against.BrokenRule` helpers arrive in v6.3.0.
- `BusinessRuleValidationException` — resolves messages through the existing `ValidationMessages` subsystem with fallback to `DefaultMessage`.
- `DomainInvariantException` — for raw invariant violations outside named rules.

#### `[StronglyTypedId<TValue>]` Source Generator (`OrionGuard.Generators`)

- Incremental generator using `ForAttributeWithMetadataName` with `RegisterPostInitializationOutput` to inject the attribute.
- Supported value types: `System.Guid`, `int`, `long`, `string`, `System.Ulid` (net9.0+).
- For each decorated `readonly partial struct`, emits four companion sources:
  - Partial body: `IEquatable<T>`, operators, `GetHashCode`, `ToString`, `Value` property + ctor, `New()` / `Empty` (for Guid and Ulid).
  - EF Core `ValueConverter<TId, TValue>` (namespace `Microsoft.EntityFrameworkCore.Storage.ValueConversion`).
  - `System.Text.Json.Serialization.JsonConverter<TId>` with proper per-type reader/writer methods.
  - `System.ComponentModel.TypeConverter` for ASP.NET Core route/query/form binding.

#### Guard Extensions

- `AgainstDefaultStronglyTypedId<TValue>(this StronglyTypedId<TValue> id, ...)` — throws `NullValueException` when `id` is null or `ZeroValueException` when its wrapped value equals the default of `TValue` (including empty string).

#### Dependency Injection

- `services.AddOrionGuardStronglyTypedIds(params Assembly[] assemblies)` — scans assemblies for source-generated `*EfCoreValueConverter` types and registers each as a singleton.

#### Localization

- 3 new keys added to all 14 bundled languages (42 new translations):
  - `DefaultStronglyTypedId`
  - `BusinessRuleBroken`
  - `DomainInvariantViolated`

#### Benchmarks

- `DomainPrimitivesBenchmark` — compares `ValueObject` class equality vs record equality, and measures `AggregateRoot.RaiseEvent` + `PullDomainEvents` overhead on net8.0 and net9.0.

### Notes

- The DDD toolkit is the first of a three-phase rollout. v6.2.0 will add the domain-event dispatcher + MediatR bridge + EF Core `SaveChanges` interceptor. v6.3.0 will add the full `BusinessRule` base class, `Guard.Against.BrokenRule`, `Validate.Rule` / `Validate.Rules`, and ASP.NET Core `BusinessRuleValidationException` → RFC 9457 ProblemDetails mapping.
- No new NuGet packages — all additions land in existing packages (`Moongazing.OrionGuard` core, `OrionGuard.Generators`).

## [6.0.0] - 2026-04-05

### Added

#### GeneratedRegex Migration
- All 24 regex patterns migrated to .NET 8+ `[GeneratedRegex]` for NativeAOT compatibility. Zero runtime compilation overhead.

#### 14-Language Localization
- Added Chinese (zh), Korean (ko), Russian (ru), Dutch (nl), Polish (pl).
- Completed all 30 message keys for German, French, Spanish, Portuguese, Arabic, Japanese.
- Total: 14 languages x 30 keys = 420 messages.

#### Rate Limit Guards
- `AgainstRateLimitExceeded()` -- general rate limit validation.
- `AgainstTooManyRequests()` -- HTTP 429-style request throttling.
- `AgainstSlidingWindowExceeded()` -- sliding window rate limit check.
- `AgainstConcurrentLimitExceeded()` -- concurrent request limit validation.
- `AgainstDailyQuotaExceeded()` -- daily quota enforcement.
- New `RateLimitExceededException` exception type.

#### International Guards
- `AgainstInvalidSwiftCode()` -- SWIFT/BIC code validation.
- `AgainstInvalidIsbn()` -- ISBN-10/ISBN-13 validation.
- `AgainstInvalidVin()` -- Vehicle Identification Number validation.
- `AgainstInvalidEan()` -- European Article Number (barcode) validation.
- `AgainstInvalidVatNumber()` -- VAT number format validation.
- `AgainstInvalidImei()` -- IMEI device identifier validation.

#### Business Guards
- `AgainstExpired()` -- token/subscription/license expiration check.
- `AgainstNotYetActive()` -- validates that an activation date has been reached.

#### Dynamic Rule Engine
- JSON-configurable runtime validation with 14 rule types: NotNull, NotEmpty, Length, Range, Email, Regex, In, NotIn, and more.
- `DynamicValidator.FromJson()` for loading rules from JSON configuration.
- `DynamicValidatorFactory` for creating validators from rule definitions.

#### Custom Exception Factory
- `IExceptionFactory` interface for pluggable exception creation.
- `DefaultExceptionFactory` implementation.
- `ExceptionFactoryProvider` for registering and resolving custom factories.

#### Deep Nested Validation
- `Validate.Nested(obj)` with unlimited depth traversal.
- `.Nested()` for validating child objects.
- `.Collection()` for validating collection items with indexed paths (e.g., `Items[0].Name`).

#### Cross-Property DSL
- `Validate.CrossProperties(obj)` entry point.
- `.AreEqual()`, `.AreNotEqual()` -- property equality comparisons.
- `.IsGreaterThan()`, `.IsLessThan()` -- relational comparisons.
- `.AtLeastOneRequired()` -- ensures at least one of the specified properties has a value.

#### Polymorphic Validation
- `Validate.Polymorphic<TBase>()` with `.When<TDerived>()` for type-discriminated validation rules.

#### Validation Result Caching
- `CachedValidator<T>` decorator with configurable TTL.
- `.WithCaching()` extension method for wrapping any validator.

#### RuleSets
- `RuleSet("create", () => ...)` for grouping validation rules.
- Execute selectively: `validator.Validate(obj, RuleSet.Create)`.

#### IRequestValidator<T>
- Pipeline-ready validator interface for middleware/MediatR integration.

#### GuardResult.SuggestedHttpStatusCode
- HTTP status code hints on `GuardResult` for ProblemDetails mapping.

### New Packages

#### Moongazing.OrionGuard.AspNetCore
- Validation middleware and `[ValidateRequest]` attribute.
- `.WithValidation<T>()` Minimal API filter.
- MVC action filter for automatic model validation.
- RFC 9457 ProblemDetails response formatting.
- `IExceptionHandler` integration.
- IOptions validation with `.ValidateWithOrionGuard()`.

#### Moongazing.OrionGuard.MediatR
- `ValidationBehavior<TRequest, TResponse>` pipeline behavior.
- Assembly scanning for automatic validator registration.

#### Moongazing.OrionGuard.Generators
- `[GenerateValidator]` source generator for compile-time, reflection-free, NativeAOT-compatible validation.

#### Moongazing.OrionGuard.Swagger
- `OrionGuardSchemaFilter` for automatic OpenAPI constraint generation from validation attributes.

#### Moongazing.OrionGuard.OpenTelemetry
- `InstrumentedValidator<T>` decorator with metrics (total/failures/duration) and distributed tracing.

#### Moongazing.OrionGuard.Blazor
- `<OrionGuardValidator />` EditForm component.
- `<OrionGuardFluentValidator TModel="..." />` EditForm component.

#### Moongazing.OrionGuard.Grpc
- `OrionGuardInterceptor` server interceptor with streaming support.

#### Moongazing.OrionGuard.SignalR
- `OrionGuardHubFilter` for automatic hub method parameter validation.

### Deprecated

- `RegexPatterns` class -- use `GeneratedRegexPatterns` instead. Will be removed in v7.0.

### Internal

- Benchmark suite with BenchmarkDotNet (NullCheck, Email, Regex, Security, ObjectValidator comparisons).

---

## [5.0.0] - 2026-04-02

### Breaking Changes

- All exception classes are now `sealed` and include `ErrorCode` and `ParameterName` properties.
- `Validate.Object<T>()` renamed to `Validate.For<T>()`.
- `Validate.ObjectStrict<T>()` renamed to `Validate.ForStrict<T>()`.
- `FastGuard.Guid()` renamed to `FastGuard.ValidGuid()`.
- Removed `TurkishGuards` -- replaced by the universal `FormatGuards` class.
- `AgainstEmptyCollection` now throws `NullValueException` instead of `EmptyStringException` (bug fix).
- `AgainstNotAllLowercase` behavior corrected -- previously always passed due to a self-comparison bug.

### Added

#### ThrowHelper Pattern
- New `ThrowHelper` static class with `[DoesNotReturn]` and `[StackTraceHidden]` attributes.
- All hot-path guard methods delegate throwing to ThrowHelper for smaller JIT-compiled method bodies.

#### Span-Based FastGuard Methods
- `FastGuard.Email(string, string)` -- validates email format using zero-allocation span parsing.
- `FastGuard.Ascii(ReadOnlySpan<char>, string)` -- ensures all characters are within the ASCII range.
- `FastGuard.AlphaNumeric(ReadOnlySpan<char>, string)` -- rejects non-alphanumeric characters.
- `FastGuard.NumericString(ReadOnlySpan<char>, string)` -- digits-only span validation.
- `FastGuard.MaxLength(ReadOnlySpan<char>, int, string)` -- maximum length check on spans.
- `FastGuard.ValidGuid(ReadOnlySpan<char>, string)` -- GUID format and non-empty check.
- `FastGuard.Finite(double, string)` -- rejects NaN and Infinity.

#### Security Guards (new file)
- `AgainstSqlInjection` -- detects 28 common SQL injection patterns using a `FrozenSet`.
- `AgainstXss` -- detects 28 cross-site scripting vectors including event handlers and DOM sinks.
- `AgainstPathTraversal` -- catches directory traversal sequences and common encoded variants.
- `AgainstCommandInjection` -- blocks shell metacharacters, pipe operators, and known interpreters.
- `AgainstLdapInjection` -- span-based detection of LDAP-special characters.
- `AgainstXxe` -- detects DOCTYPE and ENTITY declarations indicative of XXE attacks.
- `AgainstInjection` -- combined check that runs SQL, XSS, path traversal, and command injection in one call.
- `AgainstUnsafeFileName` -- validates filenames against path traversal and invalid OS characters.
- `AgainstOpenRedirect` -- validates redirect URLs against an allow-list of trusted domains.

#### Format Guards (new file, replaces TurkishGuards)
- `AgainstInvalidLatitude` / `AgainstInvalidLongitude` / `AgainstInvalidCoordinates` -- geographic coordinate validation.
- `AgainstInvalidMacAddress` -- validates MAC addresses in colon, hyphen, or dot-separated notation.
- `AgainstInvalidHostname` -- RFC 1123 hostname validation including label length and character rules.
- `AgainstInvalidCidr` -- validates CIDR notation for both IPv4 and IPv6 addresses.
- `AgainstInvalidCountryCode` -- checks against the full ISO 3166-1 alpha-2 set (249 codes).
- `AgainstInvalidTimeZoneId` -- validates against the system's IANA/Windows time zone database.
- `AgainstInvalidLanguageTag` -- BCP 47 / IETF language tag validation using CultureInfo.
- `AgainstInvalidJwtFormat` -- structural validation of JWT tokens (three Base64URL segments).
- `AgainstInvalidConnectionString` -- checks for well-formed key=value connection string pairs.
- `AgainstInvalidBase64String` -- validates Base64 encoding structure and padding.

#### ObjectValidator Enhancements
- Compiled expression caching via `ConcurrentDictionary` to avoid repeated `Expression.Compile()` overhead.
- `CrossProperty<TProp1, TProp2>()` for cross-property validation with a custom predicate.
- `When(bool, Action<ObjectValidator<T>>)` for conditional validation blocks.

#### FluentGuard Enhancements
- `Transform(Func<T, T>)` -- in-pipeline value transformation (trim, lowercase, etc.).
- `Default(T)` -- replaces null values with a specified default during validation.
- All date comparisons now use `DateTime.UtcNow`.

#### Thread-Safe Localization
- `ValidationMessages` rewritten with `ConcurrentDictionary` and `AsyncLocal<CultureInfo>`.
- `SetCultureForCurrentScope(CultureInfo)` for per-request culture without affecting other threads.
- Expanded to 8 languages: English, Turkish, German, French, Spanish, Portuguese, Arabic, Japanese.
- 30 message keys per major language with fallback to English for missing entries.

#### Infrastructure
- `GuardProfileRegistry` now uses `ConcurrentDictionary` for thread-safe profile registration.
- Added `TryExecute<T>()`, `IsRegistered()`, and `Remove()` methods to `GuardProfileRegistry`.
- `RegexCache` with bounded size (`MaxCacheSize = 1000`) replaces all raw `Regex.IsMatch` calls.
- CI/CD workflow added: builds and tests on every push, publishes to NuGet.org and GitHub Packages on release.

### Changed

- All `DateTime.Now` references replaced with `DateTime.UtcNow` across DateTimeGuards and FluentGuard.
- All `Regex.IsMatch()` calls replaced with `RegexCache.IsMatch()` (StringGuards, AdvancedStringGuards, BusinessGuards).
- `BusinessGuards` currency codes now stored in a static `FrozenSet<string>` instead of allocating a new `HashSet` per call.
- `CollectionGuards.AgainstExceedingCount` optimized: checks `ICollection<T>.Count` or `IReadOnlyCollection<T>.Count` first, falls back to `Take(n+1).Count()` for unenumerated sequences.
- `FileGuards.AgainstInvalidFileExtension` now uses `StringComparer.OrdinalIgnoreCase` for extension matching.
- All `StartsWith` / `EndsWith` calls include an explicit `StringComparison` parameter.
- `AnalysisLevel` set to `latest-recommended` with `TreatWarningsAsErrors` enabled.
- `GeneratePackageOnBuild` set to `false` (packing handled by CI pipeline).

### Fixed

- `AgainstNotAllLowercase` was comparing the string to itself with `InvariantCultureIgnoreCase`, which always returned true. Now compares against `ToLowerInvariant()` with `Ordinal` comparison.
- `AgainstEmptyCollection` was throwing `EmptyStringException` for empty collections. Now correctly throws `NullValueException`.
- `GuardBuilderExtensions` was passing `.Value` instead of `.ParameterName` in error messages.
- Missing `using System.Diagnostics` in ThrowHelper and FastGuard caused `StackTraceHidden` build errors.
- Multiple CA analyzer violations resolved: CA1305, CA1310, CA1720, CA1845, CA1862, CA2263.

### Removed

- `TurkishGuards.cs` -- country-specific validations removed in favor of universal `FormatGuards`.

---

## [4.0.0] - 2025-01-XX

### ?? Major Features

#### New Fluent API with `Ensure.That()`
```csharp
// Automatic parameter name capture with CallerArgumentExpression
Ensure.That(email).NotNull().NotEmpty().Email();
Ensure.That(password).NotNull().MinLength(8).Matches(@"^(?=.*[A-Z])");

// Shorthand methods
string validEmail = Ensure.NotNull(email);
string validName = Ensure.NotNullOrEmpty(name);
int validAge = Ensure.InRange(age, 18, 120);
```

#### Result Pattern with Error Accumulation
```csharp
// Collect all errors instead of throwing on first
var result = GuardResult.Combine(
    Ensure.Accumulate(email, "Email").NotNull().Email().ToResult(),
    Ensure.Accumulate(password, "Password").MinLength(8).ToResult(),
    Ensure.Accumulate(username, "Username").Length(3, 30).ToResult()
);

if (result.IsInvalid)
{
    foreach (var error in result.Errors)
    {
        Console.WriteLine($"[{error.ParameterName}]: {error.Message}");
    }
}

// API-friendly error format
Dictionary<string, string[]> errors = result.ToErrorDictionary();
```

#### Async Validation Support
```csharp
var result = await EnsureAsync.That(email, "Email")
    .UniqueAsync(async e => await userRepository.IsEmailUniqueAsync(e))
    .ExistsAsync(async e => await emailService.IsDeliverableAsync(e))
    .ValidateAsync();
```

#### Conditional Validation (When/Unless)
```csharp
Ensure.That(secondaryEmail)
    .When(isPrimaryEmailInvalid)     // Only validate when condition is true
    .NotNull()
    .Email()
    .Unless(isGuestUser)             // Skip when condition is true
    .MinLength(5)
    .Always()                        // Reset - always validate from here
    .NotEmpty();
```

#### Object Validation with Property Expressions
```csharp
var result = Validate.Object(userDto)
    .Property(u => u.Email, g => g.NotNull().Email())
    .Property(u => u.Password, g => g.NotNull().MinLength(8))
    .Property(u => u.Age, g => g.InRange(18, 120))
    .NotNull(u => u.CreatedAt)
    .Must(u => u.Role, r => r != "Admin", "Cannot self-assign admin role")
    .ToResult();
```

#### Code Contracts
```csharp
public decimal CalculateDiscount(decimal price, decimal discountPercent)
{
    // Preconditions
    Contract.Requires(price >= 0, "Price must be non-negative");
    Contract.Requires(discountPercent >= 0 && discountPercent <= 100, "Invalid discount");
    
    var result = price * (1 - discountPercent / 100);
    
    // Postcondition
    Contract.Ensures(result >= 0 && result <= price, "Result must be valid");
    
    return result;
}
```

#### High-Performance Guards (FastGuard)
```csharp
// Optimized for hot paths with aggressive inlining
FastGuard.NotNullOrEmpty(email, nameof(email));
FastGuard.InRange(age, 0, 150, nameof(age));
FastGuard.Positive(quantity, nameof(quantity));

// Span-based validation for zero allocations
FastGuard.NotEmpty(dataSpan, nameof(dataSpan));
```

#### Logical Guards (AND/OR)
```csharp
// OR logic - any condition passing is sufficient
var phoneOrEmail = contact.EitherOr("Contact")
    .Or(c => IsValidEmail(c), "valid email")
    .Or(c => IsValidPhone(c), "valid phone")
    .Validate();

// AND logic with short-circuit
var result = value.AllOf("Value", shortCircuit: true)
    .And(v => v != null, "cannot be null")
    .And(v => v.Length > 0, "cannot be empty")
    .And(v => v.Length < 100, "too long")
    .ToResult();
```

### ?? New Features

#### Advanced String Validators
- **Credit Card** - Luhn algorithm validation (Visa, MasterCard)
- **IBAN** - International Bank Account Number
- **JSON/XML** - Structure validation
- **Base64** - Encoding validation
- **Turkish ID (TC Kimlik)** - National ID validation
- **Hex Color** - Color code validation
- **Semantic Version** - SemVer format
- **URL Slug** - URL-friendly format

```csharp
"4111111111111111".AgainstInvalidCreditCard("card");
"DE89370400440532013000".AgainstInvalidIban("iban");
"10000000146".AgainstInvalidTurkishId("tcNo");
"{\"key\": \"value\"}".AgainstInvalidJson("data");
"#FF5733".AgainstInvalidHexColor("color");
"1.2.3-beta.1".AgainstInvalidSemVer("version");
```

#### Business Domain Guards
```csharp
// Money & Currency
amount.AgainstInvalidMonetaryAmount("price", maxDecimalPlaces: 2);
"TRY".AgainstInvalidCurrencyCode("currency");
discount.AgainstInvalidPercentage("discount");

// E-commerce
total.AgainstOrderBelowMinimum(minimumOrder: 50m, "orderTotal");
"SUMMER2025".AgainstInvalidCouponCode("coupon");
"SKU-12345-XL".AgainstInvalidSku("productSku");

// Business Hours
orderDate.AgainstWeekend("deliveryDate");
appointmentTime.AgainstOutsideBusinessHours("appointment", startHour: 9, endHour: 18);

// Rating & Review
rating.AgainstInvalidRating("stars", minRating: 1, maxRating: 5);
reviewText.AgainstInvalidReviewText("review", minLength: 10, maxLength: 5000);
```

#### Validation Attributes
```csharp
public class CreateUserRequest
{
    [NotNull, Email]
    public string Email { get; set; }
    
    [NotEmpty, Length(8, 128)]
    public string Password { get; set; }
    
    [Range(18, 120)]
    public int Age { get; set; }
    
    [Regex(@"^\+?[1-9]\d{1,14}$")]
    public string? Phone { get; set; }
}

// Validate using attributes
var result = AttributeValidator.Validate(request);
```

#### Dependency Injection Integration
```csharp
// Register in Startup/Program.cs
services.AddOrionGuard();
services.AddValidator<CreateUserRequest, CreateUserRequestValidator>();

// Create custom validator
public class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.Email, "Email", v => v.NotNull().NotEmpty().Email());
        RuleFor(x => x.Password, "Password", v => v.NotNull().MinLength(8));
        RuleForAsync(async x => await IsEmailUnique(x.Email), "Email already exists", "Email");
    }
}

// Use in controller
public class UsersController
{
    private readonly IValidator<CreateUserRequest> _validator;
    
    public async Task<IActionResult> Create(CreateUserRequest request)
    {
        var result = await _validator.ValidateAsync(request);
        if (result.IsInvalid)
            return BadRequest(result.ToErrorDictionary());
        // ...
    }
}
```

#### Localization Support
```csharp
// Set culture globally
ValidationMessages.SetCulture("tr");

// Get localized messages
var message = ValidationMessages.Get("NotNull", "Email");
// TR: "Email bo? olamaz."
// EN: "Email cannot be null."
// DE: "Email darf nicht null sein."

// Add custom translations
ValidationMessages.AddMessages("es", new Dictionary<string, string>
{
    ["NotNull"] = "{0} no puede ser nulo.",
    ["Email"] = "{0} debe ser una direcci�n de correo v�lida."
});
```

#### Common Validation Profiles
```csharp
var emailResult = CommonProfiles.Email("user@example.com");
var passwordResult = CommonProfiles.Password("SecureP@ss1", 
    minLength: 8, 
    requireUppercase: true,
    requireSpecialChar: true);
var usernameResult = CommonProfiles.Username("john_doe", minLength: 3, maxLength: 30);
var phoneResult = CommonProfiles.PhoneNumber("+905551234567");
var birthDateResult = CommonProfiles.BirthDate(birthDate, minAge: 18, maxAge: 120);
var moneyResult = CommonProfiles.MonetaryAmount(99.99m, min: 0, max: 10000);
```

### ? Performance Improvements

- **Regex Caching**: Compiled regex patterns are cached for reuse
- **Span-based Operations**: Zero-allocation validation for hot paths
- **Aggressive Inlining**: Critical paths optimized with `MethodImplOptions.AggressiveInlining`
- **Short-circuit Evaluation**: Optional early exit on first failure
- **Debug Guards**: Conditional compilation for development-only assertions

### ?? Breaking Changes

- Minimum target framework changed to .NET 8.0 (still supports .NET 9.0)
- `Guard.For<T>()` now returns `GuardBuilder<T>` for legacy compatibility
- New `Ensure.That<T>()` is the recommended entry point for v4.0

### ?? Package Changes

- Added dependency: `Microsoft.Extensions.DependencyInjection.Abstractions`
- Multi-targeting: net8.0, net9.0

### ?? New Files Added

```
src/Moongazing.OrionGuard/
??? Core/
?   ??? Ensure.cs              # New fluent API entry point
?   ??? FluentGuard.cs         # Enhanced fluent builder
?   ??? GuardResult.cs         # Result pattern implementation
?   ??? AsyncGuard.cs          # Async validation support
?   ??? ObjectValidator.cs     # Object property validation
?   ??? Contract.cs            # Code contracts
?   ??? FastGuard.cs           # High-performance guards
?   ??? LogicalGuards.cs       # AND/OR logic
??? Attributes/
?   ??? ValidationAttributes.cs # Attribute-based validation
??? DependencyInjection/
?   ??? ServiceCollectionExtensions.cs
??? Extensions/
?   ??? AdvancedStringGuards.cs # Credit card, IBAN, etc.
?   ??? BusinessGuards.cs       # Domain-specific guards
??? Localization/
?   ??? ValidationMessages.cs   # Multi-language support
??? Profiles/
    ??? CommonProfiles.cs       # Pre-built validation profiles
```

---

## [3.0.0] - Previous Release

- Initial fluent API with `Guard.For<T>()`
- Basic validation profiles
- Extension methods for common types
- Custom exception types

---

## Migration Guide (3.x ? 4.0)

### Recommended Changes

```csharp
// Before (v3.x) - Still works!
Guard.For(email, nameof(email)).NotNull().NotEmpty();

// After (v4.0) - Recommended
Ensure.That(email).NotNull().NotEmpty();
```

### New Error Handling Pattern

```csharp
// Before (v3.x)
try {
    Guard.For(email, "email").Email();
} catch (InvalidEmailException ex) {
    // Handle
}

// After (v4.0) - Result pattern
var result = Ensure.Accumulate(email, "email").Email().ToResult();
if (result.IsInvalid) {
    return BadRequest(result.ToErrorDictionary());
}
```

---

## Authors

- **Tunahan Ali Ozturk** - *Creator & Maintainer*

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE.txt) file for details.
