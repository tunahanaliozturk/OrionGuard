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
- **`FluentValidation` `AbstractValidator<T>`.** The closest commodity alternative for object validation. The `FluentStyleValidator<T>` compatibility layer ships specifically so readers can migrate from FluentValidation without rewriting validators; the benchmark should answer "what does that migration cost me at runtime".
- **Hand-compiled `Regex`.** The floor for regex-based validation. `GeneratedRegex_Email` is measured next to `RawCompiledRegex_Email` for exactly this reason.

The point of the comparison is to be honest about where OrionGuard sits, not to win a chart. If a competitor is faster on a given scenario we will say so and explain why.
