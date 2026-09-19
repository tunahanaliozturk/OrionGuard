# Good first issues

Small, real tasks that exist in this repo right now. Each one was found by reading the code, not by
brainstorming; each names the files, what "done" looks like, and the check that proves it.

Read [CONTRIBUTING.md](../CONTRIBUTING.md) first. One task per pull request. Before you start, search
the open pull requests for the file you are about to touch so two people do not fix the same line.

Every one of these assumes the baseline still holds afterwards:

```bash
dotnet build Moongazing.OrionGuard.sln -c Release   # 0 errors, 0 warnings
dotnet test Moongazing.OrionGuard.sln -c Release    # green
```

---

## 1. Add the four missing packages to the README package table

Four shipped packages are missing from `README.md`'s **Ecosystem Packages** table:
`OrionGuard.Outbox.Dashboard`, `OrionGuard.Outbox.PostgresNotify`, `OrionGuard.Outbox.SqlServerBroker`
and `OrionGuard.Migration` (a `dotnet` tool, so its install line is `dotnet tool install -g
OrionGuard.Migration`).

- **Files:** `README.md` (the `## Ecosystem Packages` table)
- **Done:** every packable project under `src/` has a row, with the one-line purpose taken from that
  package's own `docs/README.md` intro rather than invented.
- **Verify:** `ls src/` and the table have the same set of packages. Nothing else in `README.md`
  changes.

## 2. Re-run the benchmarks and republish the numbers

`benchmarks.md` line 3 and `README.md`'s Benchmarks section both say the published run used
BenchmarkDotNet **0.14.0** and .NET 10.0.5. `benchmarks/Moongazing.OrionGuard.Benchmarks.csproj` is
on **0.15.8**, so the published tables no longer match the tool that produces them.

- **Files:** `benchmarks.md`, `README.md` (Benchmarks section)
- **Done:** one full suite run on one machine, in Release, with the environment line and every table
  replaced from that single run. Do not mix numbers from two runs.
- **Verify:**
  ```bash
  dotnet run -c Release --project benchmarks/Moongazing.OrionGuard.Benchmarks -- --filter '*'
  ```
  The environment header BenchmarkDotNet prints is the one that goes into `benchmarks.md` line 3.

## 3. Hoist the health check's default tag array out of the call

`src/Moongazing.OrionGuard.AspNetCore/Extensions/HealthCheckExtensions.cs:35` allocates
`new[] { "validation", "orionguard" }` on every `AddOrionGuardCheck()` call that does not pass tags.
It is registration-time code, so the win is tidiness rather than throughput, but it is the kind of
allocation the rest of this codebase already avoids.

- **Files:** `src/Moongazing.OrionGuard.AspNetCore/Extensions/HealthCheckExtensions.cs`
- **Done:** a `private static readonly string[] DefaultTags = ["validation", "orionguard"];` field,
  used as the fallback. Behaviour and the documented tag names are unchanged.
- **Verify:** `dotnet test tests/Moongazing.OrionGuard.AspNetCore.Tests -c Release` stays green, and
  the registered check still carries both tags.

## 4. Give `OrionGuard.Blazor` a test project

Every other shipped package is covered by a `tests/<Project>.Tests` project or by
`tests/Moongazing.OrionGuard.Tests`. `src/Moongazing.OrionGuard.Blazor` is referenced by no test
project at all, so its `EditForm` integration is verified only by hand.

- **Files:** new `tests/Moongazing.OrionGuard.Blazor.Tests/`, `Moongazing.OrionGuard.sln`
- **Done:** a test project that mirrors the shape of `tests/Moongazing.OrionGuard.AspNetCore.Tests`
  (same xUnit versions, `IsPackable=false`, `<Using Include="Xunit" />`), added to the solution, with
  a first handful of tests over the package's public entry points. Start with the validator-to-
  `EditContext` path; a full bUnit harness is a separate, larger change.
- **Verify:** `dotnet test Moongazing.OrionGuard.sln -c Release` runs the new project and is green.

## 5. Make the compatibility builder's `Matches` behave like FluentValidation, then map it

`src/Moongazing.OrionGuard.Migration/RuleMapper.cs:87` carries a `TODO` and refuses to migrate
`Matches()`: `src/Moongazing.OrionGuard/Compatibility/FluentRuleBuilder.cs:127` skips empty and
whitespace values, while FluentValidation matches them against the pattern. The codemod is correct to
refuse today; the fix is to remove the reason it has to.

- **Files:** `src/Moongazing.OrionGuard/Compatibility/FluentRuleBuilder.cs`,
  `src/Moongazing.OrionGuard.Migration/RuleMapper.cs`,
  `tests/Moongazing.OrionGuard.Tests`, `tests/Moongazing.OrionGuard.Migration.Tests`
- **Done:** `Matches(string)` fails a blank value the pattern does not match, the `Regex` and
  `Func<T, string>` overloads the TODO names exist, the `RuleMapper` entry becomes
  `RuleMapping.Supported("Matches")`, and the `TODO` comment is deleted rather than reworded.
- **Verify:** a test that a blank value fails `Matches("^a+$")`, and a `MigrationEngineTests` case
  showing a `Matches` chain now migrates instead of emitting `// TODO: OrionGuard migration`. This is
  a behaviour change to a shipped API, so it needs a `### Changed` line under `## [Unreleased]` in
  `CHANGELOG.md`.

## 6. Make the compatibility builder's `EmailAddress` behave like FluentValidation, then map it

The same shape, one rule over: `src/Moongazing.OrionGuard.Migration/RuleMapper.cs:94` refuses
`EmailAddress()` because `FluentRuleBuilder.EmailAddress()` uses a stricter regex and skips blank
values, where FluentValidation only requires a single `@` that is neither the first nor the last
character, and rejects an empty string.

- **Files:** `src/Moongazing.OrionGuard/Compatibility/FluentRuleBuilder.cs` (around line 108),
  `src/Moongazing.OrionGuard.Migration/RuleMapper.cs`,
  `tests/Moongazing.OrionGuard.Tests`, `tests/Moongazing.OrionGuard.Migration.Tests`
- **Done:** the compatibility `EmailAddress()` implements FluentValidation's check exactly, the
  mapper entry becomes supported, and the `TODO` is gone. Do not touch `PropertyValidator.Email()`:
  that is OrionGuard's own stricter rule and it is meant to stay strict.
- **Verify:** tests for `""`, `"@a.com"`, `"a@"` and `"a@b"`, plus a migration test showing the chain
  now migrates. Needs a `### Changed` CHANGELOG line.
