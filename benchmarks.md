# OrionGuard Benchmarks

Latest run: 2026-05 on Intel Core i7-7820HQ CPU @ 2.90 GHz (Kaby Lake, 4 physical / 8 logical cores), Windows 11 22H2, .NET 10.0.5 (X64 RyuJIT AVX2), BenchmarkDotNet 0.14.0.

> **Note.** These numbers are reference-grade, not marketing claims. Reproduce locally with `dotnet run -c Release --project benchmarks/Moongazing.OrionGuard.Benchmarks`. Your hardware will differ.

## Methodology

- BenchmarkDotNet `ShortRun` job (3 warmup + 3 measurement iterations). Some figures show wider error bars than `MediumRun` would, which is by design: these benchmarks ship for fast feedback in CI, not for publication-grade precision. Rerun with `--job medium` if you need tighter intervals.
- Memory profiler enabled (`[MemoryDiagnoser]`).
- All allocations and GC stats reported.
- Each scenario isolated; no shared state between runs.

## Scenarios

### Null checks

The cheapest validation OrionGuard does. The numbers below pin down the cost of going through the abstractions versus a raw `if (x is null) throw` pattern.

| Method                                |       Mean |      Error |    StdDev |     Median |  Ratio | RatioSD |   Gen0 | Allocated |
|---------------------------------------|-----------:|-----------:|----------:|-----------:|-------:|--------:|-------:|----------:|
| RawIfThrow_NotNull                    |  0.9427 ns |  9.0955 ns | 0.4986 ns |  0.7322 ns |  1.175 |    0.72 |      - |       0 B |
| Guard_AgainstNull                     |  0.1294 ns |  0.8487 ns | 0.0465 ns |  0.1418 ns |  0.161 |    0.08 |      - |       0 B |
| Ensure_That_NotNull                   | 15.1543 ns | 34.4373 ns | 1.8876 ns | 14.1039 ns | 18.890 |    7.35 | 0.0191 |      80 B |
| FastGuard_NotNull                     |  1.2557 ns |  1.3515 ns | 0.0741 ns |  1.2389 ns |  1.565 |    0.59 |      - |       0 B |
| Guard_AgainstNullOrEmpty_ValidString  |  1.1358 ns |  3.1390 ns | 0.1721 ns |  1.0534 ns |  1.416 |    0.56 |      - |       0 B |
| FastGuard_NotNullOrEmpty_ValidString  |  0.0796 ns |  1.7406 ns | 0.0954 ns |  0.0534 ns |  0.099 |    0.12 |      - |       0 B |
| Ensure_NotNullOrEmpty_ValidString     |  0.0000 ns |  0.0000 ns | 0.0000 ns |  0.0000 ns |  0.000 |    0.00 |      - |       0 B |

Interpretation: `FastGuard` and `Guard.Against*` are essentially free on the happy path. `Ensure.That(...).NotNull()` allocates an 80 B builder so it pays for the fluent ergonomics. Reach for `FastGuard` on hot paths and `Ensure` elsewhere.

### Email validation

| Method                    |     Mean |    Error |   StdDev | Ratio | RatioSD |   Gen0 | Allocated |
|---------------------------|---------:|---------:|---------:|------:|--------:|-------:|----------:|
| RawCompiledRegex_Email    | 78.23 ns | 23.68 ns | 1.298 ns |  1.00 |    0.02 |      - |       0 B |
| GeneratedRegex_Email      | 79.45 ns | 34.13 ns | 1.871 ns |  1.02 |    0.03 |      - |       0 B |
| Guard_AgainstInvalidEmail | 74.11 ns | 28.21 ns | 1.546 ns |  0.95 |    0.02 |      - |       0 B |
| FastGuard_Email_SpanBased | 11.66 ns | 19.49 ns | 1.068 ns |  0.15 |    0.01 |      - |       0 B |
| Ensure_That_Email         | 84.45 ns | 33.18 ns | 1.819 ns |  1.08 |    0.03 | 0.0191 |      80 B |

Interpretation: the source-generated regex is statistically identical to the hand-compiled regex (both ~80 ns). `Guard.AgainstInvalidEmail` is a hair faster because of an early-exit fast-fail. `FastGuard.Email_SpanBased` skips the regex entirely and does a span scan, paying 12 ns for what most production code actually needs.

### Regex patterns

| Method                      |      Mean |     Error |   StdDev |   Gen0 | Allocated |
|-----------------------------|----------:|----------:|---------:|-------:|----------:|
| RegexCache_Email            | 155.53 ns | 51.732 ns | 2.836 ns | 0.0191 |      80 B |
| GeneratedRegex_Email        |  75.65 ns | 30.528 ns | 1.673 ns |      - |       0 B |
| RegexCache_PhoneNumber      | 120.45 ns | 10.215 ns | 0.560 ns | 0.0153 |      64 B |
| GeneratedRegex_PhoneNumber  |  50.09 ns | 56.275 ns | 3.085 ns |      - |       0 B |
| RegexCache_AlphaNumeric     | 104.81 ns | 87.034 ns | 4.771 ns | 0.0134 |      56 B |
| GeneratedRegex_AlphaNumeric |  35.11 ns |  6.788 ns | 0.372 ns |      - |       0 B |

Interpretation: source-generated regex is 2x to 3x faster than a `RegexCache` lookup plus a `Match()` call, and allocates nothing. This is why OrionGuard's 24 regex patterns are all `[GeneratedRegex]`.

### Object validation

| Method                            |          Mean |        Error |     StdDev |  Ratio | RatioSD |   Gen0 | Allocated |
|-----------------------------------|--------------:|-------------:|-----------:|-------:|--------:|-------:|----------:|
| ManualValidation                  |      4.786 ns |     1.316 ns |  0.0721 ns |   1.00 |    0.02 |      - |       0 B |
| Validate_For_AllProperties        |  2,546.446 ns |   542.276 ns | 29.7240 ns | 532.11 |    8.78 | 0.8850 |    3704 B |
| Validate_For_WithPropertyChaining |  2,777.566 ns | 1,464.866 ns | 80.2942 ns | 580.41 |   16.38 | 0.8545 |    3576 B |
| Validate_ForStrict_AllProperties  |  2,642.425 ns | 1,409.270 ns | 77.2468 ns | 552.17 |   15.72 | 0.8698 |    3640 B |

Interpretation: hand-written validation will always win the microbenchmark because it does exactly nothing the compiler did not already inline. The 2.5 us figure for the fluent object validator is what you pay for an extensible rule pipeline, error accumulation, and localization. For most CRUD endpoints this is invisible next to the EF Core round-trip. For hot loops, reach for `FastGuard`.

### Security guards

Linear in input length because the FrozenSet pattern set is fixed and each pattern matched against the input.

| Method                     |         Mean |        Error |     StdDev | Allocated |
|----------------------------|-------------:|-------------:|-----------:|----------:|
| AgainstSqlInjection_Short  |     41.98 ns |     6.435 ns |   0.353 ns |       0 B |
| AgainstSqlInjection_Medium |  1,266.87 ns |   607.982 ns |  33.326 ns |       0 B |
| AgainstSqlInjection_Long   | 25,233.83 ns | 2,297.883 ns | 125.955 ns |       0 B |
| AgainstXss_Short           |     34.03 ns |    31.848 ns |   1.746 ns |       0 B |
| AgainstXss_Medium          |    297.09 ns |   217.671 ns |  11.931 ns |       0 B |
| AgainstXss_Long            |  3,230.04 ns | 1,115.045 ns |  61.119 ns |       0 B |
| AgainstInjection_Short     |    129.59 ns |    29.715 ns |   1.629 ns |       0 B |
| AgainstInjection_Medium    |  1,735.31 ns |   451.942 ns |  24.772 ns |       0 B |

Interpretation: the security guards are designed for request validation on small inputs. The "Long" rows simulate a 4 KB payload and show the linear scaling cost; for a more typical 100-byte query string, sub-microsecond figures apply.

### Domain primitives

| Method                     |       Mean |      Error |    StdDev | Ratio | RatioSD |   Gen0 | Allocated |
|----------------------------|-----------:|-----------:|----------:|------:|--------:|-------:|----------:|
| ValueObject_ClassEquality  | 104.908 ns | 66.1232 ns | 3.6244 ns |  1.00 |    0.04 | 0.0343 |     144 B |
| ValueObject_RecordEquality |   6.187 ns |  0.9568 ns | 0.0524 ns |  0.06 |    0.00 |      - |       0 B |
| AggregateRoot_RaiseAndPull | 160.362 ns | 25.2494 ns | 1.3840 ns |  1.53 |    0.05 | 0.0172 |      72 B |

Interpretation: prefer `record`-based value objects when you can. The class-based `ValueObject` base type allocates an enumerator and walks `GetEqualityComponents`; the record version delegates to the compiler-generated structural-equality member and is essentially free. `AggregateRoot.RaiseAndPull` measures the round-trip of raising a domain event and dispatching it via the in-memory dispatcher.

## OrionGuard vs FluentValidation

Measured run: 2026-09-20 on `42ba486` (master including #76), BenchmarkDotNet 0.15.8, **FluentValidation 12.1.1** (Apache-2.0, published 2025-12-03, no transitive dependencies; referenced by `benchmarks/Moongazing.OrionGuard.Benchmarks` only, which is `IsPackable=false`, so nothing ships with it). Every table below comes from that single run; no numbers are carried over from an earlier one.

```text
BenchmarkDotNet v0.15.8, Windows 11 (10.0.26100.9106/24H2/2024Update/HudsonValley)
Intel Core Ultra 7 255H 2.00GHz, 1 CPU, 16 logical and 16 physical cores
.NET SDK 10.0.401
  [Host]   : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v3
  ShortRun : IterationCount=3  LaunchCount=1  WarmupCount=3
```

Laptop CPU on a power-managed profile, so absolute numbers move between runs and `Error` (the half-width of the 99.9 % confidence interval) is wide on some rows. Compare rows **within one run**, never across runs or machines. `us` below means microseconds.

### Fairness rules

These are the rules the comparison benchmarks follow. They are enforced in code where that is possible.

1. **Same rules, same inputs.** Both libraries validate the same DTO instances against the same rule set, written the way each library's own documentation writes it. Rule parameters and predicates live in one `SharedRules` class that both sides call.
2. **Validators are created once, in `[GlobalSetup]`, outside the measured method** - except in the construction scenario, where creating the validator *is* what is measured. OrionGuard's `Validate.For` / `Validate.Nested` have no validator object to reuse: building the chain per call is the API, and the tables say so.
3. **`[MemoryDiagnoser]`, Release build, no debugger**, one benchmark process per case, `--job short`.
4. **Equal-outcome check.** Each `[GlobalSetup]` runs every implementation once and aborts the whole run unless they all report the *same number of errors* for that input (0 for valid, 5 for the all-invalid DTO, 23 for the invalid order, 6 for the async case). A benchmark that quietly skipped a rule cannot produce a number.
5. **Where FluentValidation wins it is reported as prominently as where OrionGuard wins** - see the scoreboard below, and the list of OrionGuard problems this comparison exposed at the end of this section.
6. **Neither side is tuned.** No cascade-mode tricks, no pre-compiled accessors, no hand-rolled fast paths, no `Regex` swaps on either side. Idiomatic code only.

### What each row exercises

| Row | API | Reused across calls? |
|-----|-----|----------------------|
| `FluentValidation_*` | `FluentValidation.AbstractValidator<T>` (12.1.1) | yes |
| `OrionGuard_AbstractValidator` | `Moongazing.OrionGuard.DependencyInjection.AbstractValidator<T>`, the DI-registered validator | yes |
| `OrionGuard_FluentStyleValidator` | `Moongazing.OrionGuard.Compatibility.FluentStyleValidator<T>`, the FluentValidation migration layer | yes |
| `OrionGuard_ValidateFor` / `OrionGuard_ValidateNested` | `Validate.For` / `Validate.Nested`, the inline fluent API | no - per call by design |
| `OrionGuard_SourceGenerated` | `[GenerateValidator]` + OrionGuard attributes. **A different programming model**: rules are attributes on the DTO, the validator is emitted at compile time, there is no reflection and no expression tree | static |

### Differences that could not be removed

Honest caveats; none of them is a tuning choice made for one side.

- **`EmailAddress()` is not the same rule in both libraries.** FluentValidation's default mode (`AspNetCoreCompatible`) only requires a single `@` that is neither first nor last. Every OrionGuard path runs a source-generated regex. OrionGuard does strictly more work here, and is measured doing it.
- **OrionGuard's DI `AbstractValidator` property builder has no `MaximumLength` or `InclusiveBetween`**, so those two rules are written as `Length(0, max)` and `Must(...)`. That is how its API expresses them.
- **`NotEmpty` differs on null in the DI validator.** FluentValidation's `NotEmpty` rejects null; OrionGuard's `PropertyValidator.NotEmpty` alone does not, so the DI validator uses `NotNull().NotEmpty()` to get the same behaviour. The compat layer's `NotEmpty` matches FluentValidation case for case since #76 (null, blank strings, empty collections and value-type defaults all fail), so it needs no adjustment.
- **The generated path has no attribute for a custom predicate.** `[GenerateValidator]` covers 4 of the 5 rules; the `Must` rule is checked by hand and merged with `GuardResult.Merge`, exactly as a caller would have to. That merge is inside the measured method.
- **Rows that do not exist are absent, not hidden.** `FluentStyleValidator` has no nested-object, collection or async rules, and the generated path has no nested or collection support, so neither appears in scenarios (b) and (e).

### Scoreboard

| Scenario | Faster | Margin | Fewer allocations |
|----------|--------|--------|-------------------|
| (a) 5-rule DTO, valid | OrionGuard (`AbstractValidator`, `FluentStyleValidator`, generated) | 1.6x - 2.8x | OrionGuard, 4.4x - 9.9x |
| (a) 5-rule DTO, all 5 rules failing | OrionGuard | 9.6x - 22x | OrionGuard, 8.8x - 11.5x |
| (a) same, but via inline `Validate.For`, valid | **FluentValidation** | **8.9x** | **FluentValidation**, 6.1x |
| (a) same, but via inline `Validate.For`, all 5 rules failing | OrionGuard | 1.3x | OrionGuard, 1.6x |
| (b) nested object + 10-item collection | **FluentValidation** | **50x - 335x** | **FluentValidation**, 2.3x - 12.5x |
| (c) collect all errors | OrionGuard `Validate.For` | 1.6x | OrionGuard, 1.6x |
| (c) fail fast | **FluentValidation** (`CascadeMode.Stop`, no exception) | **4.0x** vs OrionGuard's throwing `ForStrict` | **FluentValidation**, 1.03x |
| (c) fail fast *by throwing* | OrionGuard (`Validate.ForStrict`) | 1.6x | OrionGuard, 1.5x |
| (d) construct + validate once (warm) | OrionGuard: `AbstractValidator` 5.0x, `FluentStyleValidator` 1.4x | 1.4x - 5.0x | OrionGuard, 2.3x - 4.5x |
| (d) first use in a cold process | OrionGuard: `AbstractValidator` 2.8x, `FluentStyleValidator` 1.1x | 1.1x - 2.8x | OrionGuard, 2.4x - 4.7x |
| (e) async rule, valid | OrionGuard `AbstractValidator` | 2.4x | OrionGuard, 3.0x |
| (e) async rule, all rules failing | OrionGuard `AbstractValidator` | 10.0x | OrionGuard, 7.5x |

Short version: **OrionGuard's DI `AbstractValidator`, its generated validators and - since #76 - its `FluentStyleValidator` compat layer win every scenario they support: per-call validation on valid and invalid input, async, and construction warm and cold. OrionGuard loses wherever an expression tree is built per call: `Validate.For` (8.9x on valid input) and `Validate.Nested` (up to 335x). It also has no fail-fast path that does not throw, which hands FluentValidation the cheapest rejection in the comparison.** The wins on invalid input are inflated by FluentValidation's message formatting (see (a)); the loss in (b) is a defect, not an API-philosophy difference.

### (a) Small DTO, 5 rules

`NotEmpty`, `MaximumLength(20)`, `EmailAddress`, `InclusiveBetween(18, 120)`, `Must(IsSupportedCountry)` - one rule per property. The invalid input fails all five.

| Method                             | Input   | Mean        | Error       | StdDev     | Ratio | Allocated | Alloc Ratio |
|----------------------------------- |-------- |------------:|------------:|-----------:|------:|----------:|------------:|
| FluentValidation_AbstractValidator | Valid   |   178.23 ns |   239.82 ns |  13.145 ns |  1.00 |     632 B |        1.00 |
| OrionGuard_AbstractValidator       | Valid   |   111.79 ns |    87.72 ns |   4.808 ns |  0.63 |      64 B |        0.10 |
| OrionGuard_FluentStyleValidator    | Valid   |    88.26 ns |    52.18 ns |   2.860 ns |  0.50 |     144 B |        0.23 |
| OrionGuard_ValidateFor             | Valid   | 1,584.09 ns | 1,598.56 ns |  87.622 ns |  8.92 |    3832 B |        6.06 |
| OrionGuard_SourceGenerated         | Valid   |    64.52 ns |    46.84 ns |   2.567 ns |  0.36 |      96 B |        0.15 |
| FluentValidation_AbstractValidator | Invalid | 3,131.09 ns | 2,585.33 ns | 141.711 ns |  1.00 |    9656 B |        1.00 |
| OrionGuard_AbstractValidator       | Invalid |   325.52 ns |   379.72 ns |  20.814 ns |  0.10 |    1040 B |        0.11 |
| OrionGuard_FluentStyleValidator    | Invalid |   236.59 ns |   290.90 ns |  15.945 ns |  0.08 |    1096 B |        0.11 |
| OrionGuard_ValidateFor             | Invalid | 2,358.20 ns | 1,444.54 ns |  79.180 ns |  0.75 |    5952 B |        0.62 |
| OrionGuard_SourceGenerated         | Invalid |   141.61 ns |    20.35 ns |   1.116 ns |  0.05 |     840 B |        0.09 |

Interpretation:

- On the happy path OrionGuard's reusable validators are about 1.6x - 2.8x faster and allocate 64 - 144 B against FluentValidation's 632 B. The 64 B is the whole story for `AbstractValidator`: it is one `GuardResult` plus its empty `List`. FluentValidation also builds a `ValidationContext<T>`, a `ValidationResult` and its internal failure list.
- On the failing path the 9.6x - 22x gap is **not** 10x better rule evaluation. FluentValidation builds each message through its `MessageFormatter` and `LanguageManager` (placeholder expansion, localized templates); OrionGuard interpolates a string. Read this row as "error-message construction is expensive in FluentValidation", not "OrionGuard evaluates rules 10x faster".
- `Validate.For` is the outlier: 8.9x slower than FluentValidation on valid input, and the only OrionGuard row that allocates more than FluentValidation there (3,832 B vs 632 B). It builds five `Expression<Func<...>>` trees per call (see the issue list below). On the failing input it still comes out ahead (0.75x), but only because FluentValidation's message formatting costs more than OrionGuard's expression trees - a fixed ~1.5 us tax that stays put whatever the input is.
- The source-generated path is the fastest thing in the table on both inputs, and it is the only one with no expression trees at all. It still allocates 96 B on a fully valid DTO, because the generated code news up an error `List` before the first check.
- `FluentStyleValidator` allocates 144 B on valid input where `AbstractValidator` allocates 64 B: its `InclusiveBetween` takes `IComparable` thresholds and now routes through `NumericComparer`, which boxes the `int` property on every call. That is the price of the #76 correctness fix (`GreaterThan(0)` on a `decimal` used to throw), and it is still 4.4x below FluentValidation.

### (b) Nested object + collection of 10 items

Order with 1 root rule, a nested address with 2 rules, and 10 lines with 2 rules each (23 errors when invalid). FluentValidation uses `SetValidator` and `RuleForEach`; OrionGuard uses `Validate.Nested`, its only API with nested-object and collection support.

| Method                                    | Input   | Mean         | Error        | StdDev      | Ratio  | Allocated | Alloc Ratio |
|------------------------------------------ |-------- |-------------:|-------------:|------------:|-------:|----------:|------------:|
| FluentValidation_SetValidator_RuleForEach | Valid   |     3.252 us |     5.480 us |   0.3004 us |   1.01 |   9.48 KB |        1.00 |
| OrionGuard_ValidateNested                 | Valid   | 1,082.931 us | 1,897.507 us | 104.0088 us | 334.84 | 118.76 KB |       12.53 |
| FluentValidation_SetValidator_RuleForEach | Invalid |    19.080 us |    10.385 us |   0.5693 us |   1.00 |  54.89 KB |        1.00 |
| OrionGuard_ValidateNested                 | Invalid |   953.306 us | 4,304.557 us | 235.9473 us |  49.99 | 124.94 KB |        2.28 |

Interpretation: **this is OrionGuard's worst result in the whole comparison, and it is a defect, not a trade-off.** `NestedValidator.Property`, `.Nested` and `.Collection` each call `selector.Compile()` on every invocation instead of going through `AccessorCache`, the way `ObjectValidator` does (still three `Compile()` sites on `42ba486`). This order compiles 24 expression trees per validation (2 per line x 10 lines, plus 4 at the root), about 45 us each. The cost does not depend on whether the data is valid, which is why the "valid" row is no cheaper than the "invalid" one, and why the ratio against FluentValidation is worse on valid input (335x) than on invalid (50x): FluentValidation gets cheaper when there is nothing to report, OrionGuard does not. The 118 - 125 KB and the Gen1 collections come from the same place. Note the error bars: the compile cost is variable enough that the two OrionGuard rows overlap, so read them as "about a millisecond", not as a valid/invalid difference.

### (c) Collecting all errors vs failing fast

Input fails all 5 rules of scenario (a). FluentValidation fails fast with `ClassLevelCascadeMode = CascadeMode.Stop` and can either return a result or throw (`ValidateAndThrow`). OrionGuard's only fail-fast primitive, `Validate.ForStrict`, always throws.

| Method                                    | Mean       | Error      | StdDev    | Ratio | Allocated | Alloc Ratio |
|------------------------------------------ |-----------:|-----------:|----------:|------:|----------:|------------:|
| FluentValidation_CollectAll               | 3,195.3 ns | 6,501.1 ns | 356.35 ns |  1.01 |   9.43 KB |        1.00 |
| FluentValidation_StopOnFirstFailure       |   479.7 ns |   327.1 ns |  17.93 ns |  0.15 |    1.7 KB |        0.18 |
| FluentValidation_StopOnFirstFailure_Throw | 3,110.2 ns |   922.5 ns |  50.56 ns |  0.98 |   2.62 KB |        0.28 |
| OrionGuard_ValidateFor_CollectAll         | 2,054.5 ns |   666.4 ns |  36.53 ns |  0.65 |   5.73 KB |        0.61 |
| OrionGuard_ValidateForStrict_Throw        | 1,904.8 ns | 1,261.9 ns |  69.17 ns |  0.60 |   1.75 KB |        0.19 |

Interpretation: the cheapest way to reject bad input in this table belongs to FluentValidation - 480 ns, because `CascadeMode.Stop` stops after the first failing rule **and returns a result instead of throwing**. OrionGuard has no equivalent: `Validate.ForStrict` short-circuits correctly but pays ~1.5 us for the exception, so it lands at 1,905 ns, 4.0x the cost. It is still 1.6x faster than FluentValidation's throwing variant (exception plus a message assembled from every failure), so if you must throw, OrionGuard's throw is cheaper - but a fail-fast API that does not throw would be cheaper than both.

### (d) Validator construction and first use

`new Validator()` plus one `Validate(validInput)`. This is the per-request cost when validators are registered transient (OrionGuard's `AddOrionGuard`) or scoped (FluentValidation's `AddValidatorsFromAssembly` default). The `ColdStart` job measures the first call in a fresh process (JIT, type loading and cold caches included); `ShortRun` measures it warm.

| Method                                         | Job       | Mean            | Error          | StdDev          | Ratio | Allocated | Alloc Ratio |
|----------------------------------------------- |---------- |----------------:|---------------:|----------------:|------:|----------:|------------:|
| FluentValidation_NewAndValidate                | ShortRun  |      2,696.6 ns |     1,336.7 ns |        73.27 ns |  1.00 |  11.31 KB |        1.00 |
| OrionGuard_AbstractValidator_NewAndValidate    | ShortRun  |        538.5 ns |     1,206.4 ns |        66.13 ns |  0.20 |   2.52 KB |        0.22 |
| OrionGuard_FluentStyleValidator_NewAndValidate | ShortRun  |      1,900.5 ns |     2,810.6 ns |       154.06 ns |  0.71 |   4.92 KB |        0.44 |
| FluentValidation_NewAndValidate                | ColdStart | 22,774,033.3 ns | 1,109,398.4 ns | 1,037,731.93 ns |  1.00 |  11.73 KB |        1.00 |
| OrionGuard_AbstractValidator_NewAndValidate    | ColdStart |  8,161,026.7 ns | 1,023,376.9 ns |   957,267.37 ns |  0.36 |   2.52 KB |        0.21 |
| OrionGuard_FluentStyleValidator_NewAndValidate | ColdStart | 20,993,453.3 ns | 2,043,976.5 ns | 1,911,936.87 ns |  0.92 |   4.95 KB |        0.42 |

Interpretation: OrionGuard wins both jobs. `AbstractValidator` is 5.0x faster warm (539 ns vs 2,697 ns) and allocates 4.5x less, because it takes a plain `Func<T, TProperty>` and an explicit property name and never touches expression trees.

**The compat layer used to be the trap here and no longer is.** Before #76, `FluentRuleBuilder` called `expression.Compile()` for every `RuleFor` in the constructor; constructing the five-rule validator cost ~248 us and 25 KB in the pre-#76 run of this same benchmark, 78.8x FluentValidation's constructor, paid on every request under the transient registration `AddValidator<T, TValidator>` performs. It now takes its accessors from the core `AccessorCache`, which compiles a member selector once per process - the same thing FluentValidation does with its own `MemberInfo`-keyed cache - and lands at 1,900 ns / 4.92 KB, 1.4x *faster* than FluentValidation and 2.3x leaner. That is a 130x improvement on the same benchmark and the reason this section no longer lists it as a finding.

Cold-start numbers are JIT-dominated (tens of milliseconds, wide variance) and are only worth reading as an ordering: OrionGuard's `AbstractValidator` (8.2 ms) < compat layer (21.0 ms) ~ FluentValidation (22.8 ms). FluentValidation has far more generic machinery to JIT on first use; that cost is paid once per process, not per request.

### (e) Async rules

The 5 sync rules of scenario (a) plus one async rule standing in for an availability lookup. The predicate completes synchronously from a cached `Task<bool>`, so this measures each library's async pipeline rather than I/O. `FluentStyleValidator` is absent: it has no async rules (its `ValidateAsync` wraps the sync path).

| Method                                     | Input   | Mean       | Error       | StdDev   | Ratio | Allocated | Alloc Ratio |
|------------------------------------------- |-------- |-----------:|------------:|---------:|------:|----------:|------------:|
| FluentValidation_ValidateAsync             | Valid   |   380.5 ns |   235.61 ns | 12.91 ns |  1.00 |     704 B |        1.00 |
| OrionGuard_AbstractValidator_ValidateAsync | Valid   |   159.0 ns |    96.38 ns |  5.28 ns |  0.42 |     232 B |        0.33 |
| OrionGuard_ValidateFor_ToResultAsync       | Valid   | 2,160.9 ns |   828.49 ns | 45.41 ns |  5.68 |    4792 B |        6.81 |
| FluentValidation_ValidateAsync             | Invalid | 4,085.2 ns | 7,852.77 ns | 430.44 ns |  1.01 |   10872 B |        1.00 |
| OrionGuard_AbstractValidator_ValidateAsync | Invalid |   409.1 ns |   335.84 ns | 18.41 ns |  0.10 |    1456 B |        0.13 |
| OrionGuard_ValidateFor_ToResultAsync       | Invalid | 2,247.4 ns |   190.33 ns | 10.43 ns |  0.55 |    7088 B |        0.65 |

Interpretation: with a synchronously-completing rule, OrionGuard's reusable validator adds ~47 ns and 168 B over its own sync path, and beats FluentValidation 2.4x (valid) / 10.0x (invalid). The `Validate.For` async terminal carries the same per-call expression-tree cost as its sync terminal. Caveat for API users rather than for the numbers: OrionGuard's `RuleForAsync` overloads take no `CancellationToken`, so an async rule on `AbstractValidator<T>` cannot observe the token passed to `ValidateAsync`. `Validate.For(...).MustAsync(...)` does flow it.

### What this comparison says about OrionGuard

Findings for the performance work, worst first, each checked against `42ba486`. Every one of them is visible in the tables above.

1. **`NestedValidator` compiles an expression on every call** (`Core/NestedValidator.cs`; `Property`, `Nested` and `Collection` each call `selector.Compile()` - still three sites). 24 compiles per 10-line order, ~1 ms, 119 - 125 KB, and the cost does not fall when the data is valid. `ObjectValidator` and now `FluentRuleBuilder` both solve this with `AccessorCache<T, TProperty>`; routing `NestedValidator` through the same cache should collapse (b) by two orders of magnitude. `Core/CrossPropertyValidator.cs` compiles both of its selectors per call too, in all five rules, so `Validate.CrossProperties` pays the same cost on a smaller scale.
2. **`Validate.For` rebuilds its selector expression trees on every call.** The accessors are cached, but the trees themselves are not: the C# compiler constructs five `Expression<Func<...>>` (including `Expression.Property`'s reflection lookup) per validation, which is most of the 1.6 us / 3.8 KB. An overload taking `Func<T, TProperty>` plus `[CallerArgumentExpression]` (or an explicit name, as the DI `AbstractValidator` already does) would give the inline API a no-expression path.
3. **There is no non-throwing fail-fast.** `Validate.ForStrict` is 4.0x slower than FluentValidation's `CascadeMode.Stop` purely because of the exception. A `StopOnFirstError` mode that returns a `GuardResult` would be the cheapest rejection path in the table.
4. **`GuardResult.Success()` allocates a new result and a new empty `List` every call.** On a fully valid DTO that is 100 % of `AbstractValidator`'s allocation (64 B). `_issues` is never mutated after construction, so a cached singleton is possible.
5. **The generated validator allocates its error `List` eagerly** - still true on `42ba486`: `OrionGuardGenerator.EmitPropertyValidation` emits `var errors = new System.Collections.Generic.List<ValidationError>()` before the first check, which is the 96 B the generated path allocates on valid input. Lazy `??= new()` - what the DI `AbstractValidator` already does - takes it to 64 B, and to 0 B combined with item 4. Worth coordinating with the generators branch before it lands.
6. **`AbstractValidator` clones every error it produces.** `RuleBuilder.Apply` does `error with { Severity, ErrorCode }` unconditionally, so every failure allocates a second `ValidationError` even when no override was configured. Visible in the invalid rows (1,040 B for 5 errors).
7. **`GuardResult.Errors` allocates on every access** (`Where().ToList().AsReadOnly()`), and `ThrowIfInvalid()` goes through it. Not measured here - the benchmarks return the result object rather than reading it - but every caller that inspects `Errors` pays it.
8. Minor, and the price of a correctness fix: the compat layer's comparison rules take `IComparable` thresholds and box the property value through `NumericComparer` on every call (144 B vs `AbstractValidator`'s 64 B on a valid DTO). A generic `InclusiveBetween<TValue>` overload would avoid the box without giving up the cross-numeric-type comparison #76 added.

**Fixed since the first run of this comparison:** `FluentRuleBuilder` used to call `expression.Compile()` per `RuleFor` in the constructor, which made constructing the migration layer 78.8x more expensive than FluentValidation's own constructor (248 us, 25 KB) on transiently-registered validators. #76 routed it through `AccessorCache`; the same benchmark now reads 1,900 ns / 4.92 KB, 1.4x faster than FluentValidation.

One finding in FluentValidation's favour of the opposite kind: its failing-input cost (3.1 us, 9.7 KB for 5 errors) is dominated by message formatting and localization that OrionGuard does not do. If OrionGuard's localization is switched on for messages, expect its invalid-path advantage to shrink.

### Reproducing the comparison

```bash
dotnet run -c Release --project benchmarks/Moongazing.OrionGuard.Benchmarks -- --filter '*Comparison*' --job short
```

Or trigger the `Benchmarks` workflow (`workflow_dispatch` only) and download the `benchmarkdotnet-comparison-*` artifact. Shared runners are noisier than a local machine; the workflow exists to produce reports, not to gate merges.

## How to reproduce

```bash
cd <repo-root>
dotnet run -c Release --project benchmarks/Moongazing.OrionGuard.Benchmarks
```

Results appear in `BenchmarkDotNet.Artifacts/results/`. Pass `--filter '*Email*'` (or any class / method pattern) to run a subset.

## Comparison baselines

We report OrionGuard numbers next to honest baselines so readers can place them in context:

- **Hand-written `if (x is null) throw`.** The floor. Anything OrionGuard does on top of this pays for the abstraction.
- **`Ardalis.GuardClauses` `Guard.Against.*`.** The closest commodity alternative for guard-clause style. Establishes how OrionGuard's `Guard.Against*` compares against the package most readers already know.
- **`FluentValidation` `AbstractValidator<T>`.** The closest commodity alternative for object validation, measured head-to-head in [OrionGuard vs FluentValidation](#orionguard-vs-fluentvalidation) across five scenarios - including what the `FluentStyleValidator<T>` migration layer costs at runtime.
- **Hand-compiled `Regex`.** The floor for regex-based validation. `GeneratedRegex_Email` is measured next to `RawCompiledRegex_Email` for exactly this reason.

The point of the comparison is to be honest about where OrionGuard sits, not to win a chart. If a competitor is faster on a given scenario we will say so and explain why.
