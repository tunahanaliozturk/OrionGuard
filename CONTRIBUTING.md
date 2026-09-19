# Contributing to OrionGuard

Thanks for taking the time to look at this. OrionGuard is a guard-clause and fluent-validation
toolkit for .NET, with a family of integration packages around it. The project is small and the bar
for contributions is "does it make the package clearer, faster, or safer without expanding the public
surface needlessly."

If you are looking for somewhere to start, [docs/good-first-issues.md](docs/good-first-issues.md)
lists concrete, small tasks that exist in this repo today.

## Before you open a PR

For anything beyond a typo, a docs tweak, or a one-line fix, please open an issue first. Five minutes
of alignment up front saves an afternoon of rework later. State:

- The use case you are trying to solve
- What you tried that did not work
- Whether you want to send the patch yourself or are flagging the gap

For typos, docs polish, comment fixes and single-line changes, skip the issue and send a PR directly.
Title it `docs: ...` or `chore: ...` so it is obvious from the queue.

## Build and test

```bash
git clone https://github.com/tunahanaliozturk/OrionGuard
cd OrionGuard
dotnet restore Moongazing.OrionGuard.sln
dotnet build Moongazing.OrionGuard.sln -c Release
dotnet test Moongazing.OrionGuard.sln -c Release
```

That is what CI runs, on `8.0.x`, `9.0.x` and `10.0.x`.

The shipped packages multi-target `net8.0;net9.0;net10.0`, and one `dotnet build` builds all three.
To build or test a single framework:

```bash
dotnet build src/Moongazing.OrionGuard/Moongazing.OrionGuard.csproj -c Release -f net8.0
dotnet test tests/Moongazing.OrionGuard.Tests -c Release -f net10.0
```

A **.NET 10 SDK 10.0.400 or newer is required** to build the whole solution: `OrionGuard.Generators`
and `OrionGuard.OpenApi` reference `Microsoft.CodeAnalysis.CSharp` 5.9.0, and older SDKs report
`CS9057` and silently skip the generators. The .NET 8 and .NET 9 *runtimes* are still needed to run
the multi-targeted test matrix.

Two warnings are expected today and are not yours to fix in an unrelated PR:
`GuardTests.cs` `CS8600` and `OrionLockBridgeRedisIntegrationTests.cs` `CS0618`. A PR must not add a
third.

Shipped projects set `TreatWarningsAsErrors`, so a warning in `src/` fails the build outright.

## Repo layout

| Path | What lives there |
| --- | --- |
| `src/` | Every shipped package. One project per NuGet package, each with its own `docs/README.md` (the NuGet readme), `docs/logo.png`, and `<Version>` |
| `tests/` | One xUnit project per package under test, named `<ProjectName>.Tests` |
| `benchmarks/` | The BenchmarkDotNet suite |
| `demo/` | A console app that exercises the features end to end; not packable |
| `templates/` | The `dotnet new` template pack (`OrionGuard.Templates`). Deliberately **outside** the solution, see below |
| `docs/` | Feature guides, the roadmap, and the good-first-issue list |
| `Directory.Build.props` | Repo-wide NuGet audit policy only. Everything else is set per-csproj |

`templates/` is not in `Moongazing.OrionGuard.sln` on purpose: the release workflow packs the
solution and then asserts that the number of produced `.nupkg` files equals the number of packable
projects under `src/`. A packable project outside `src/` would break that check. Pack it on its own:

```bash
dotnet pack templates/Moongazing.OrionGuard.Templates.csproj -c Release -o ./nupkgs
```

## Code style

`.editorconfig` carries one rule (`csharp_style_prefer_primary_constructors = false`, so IDE0290 does
not nag). Everything else is convention, enforced by review:

- **Nullable reference types are enabled everywhere.** `<Nullable>enable</Nullable>` is on in every
  project, including tests. Do not silence a nullable warning with `!` when the type can be fixed.
- **XML docs on public API.** Shipped projects set `GenerateDocumentationFile`. `CS1591` is in
  `NoWarn` only so that internal helpers and implicit `Program` types do not block the build; a new
  public type or member ships with a `<summary>`, and `<param>` / `<returns>` / `<exception>` where
  they say something the signature does not.
- **Comments explain WHY, not what.** The code already says what. The comments worth writing are the
  ones that record a decision, a constraint, or a trap, like the EF Core version split in
  `Moongazing.OrionGuard.EntityFrameworkCore.csproj` or the exception-swallowing rationale on
  `AbstractValidator`'s async rule entries. If a comment restates the line below it, delete it.
- **Match the surrounding code.** There is no separate style guide. If the existing code does X, do X.
- Names are spelled out. No `mgr`, `svc`, `ctx`. Well-known abbreviations (`Id`, `Db`, `Url`, `Json`)
  are fine.
- `sealed` by default on classes that are not designed for inheritance.
- Analyzers: `OrionGuard` and `OrionGuard.Migration` run `EnableNETAnalyzers` with
  `AnalysisLevel=latest-recommended` and `EnforceCodeStyleInBuild`. Treat their warnings as bugs.

## Tests

- xUnit, plain `Assert`. There is no FluentAssertions dependency; do not add one.
- Test names are sentences that state the promise:
  `Invalid_body_uses_the_status_the_validator_suggests_over_DefaultStatusCode`. Older files use
  `Method_ShouldDoX_WhenY`; either is fine, matching the file you are editing is better.
- **New behaviour comes with tests. A bug fix comes with a test that fails before the fix and passes
  after it.** A fix without that test will be asked for one.
- Tests that need real infrastructure (Redis, PostgreSQL, SQL Server) live in their own project and
  skip themselves when the infrastructure is not reachable. Keep `dotnet test` green on a laptop with
  nothing running.
- Coverage is a side effect of testing behaviour, not a target.

## Benchmarks

```bash
dotnet run -c Release --project benchmarks/Moongazing.OrionGuard.Benchmarks
```

BenchmarkDotNet's switcher prompts for which suites to run. To run one without the prompt, or all of
them:

```bash
dotnet run -c Release --project benchmarks/Moongazing.OrionGuard.Benchmarks -- --filter '*EmailBenchmarks*'
dotnet run -c Release --project benchmarks/Moongazing.OrionGuard.Benchmarks -- --filter '*'
```

Release configuration is not optional; BenchmarkDotNet refuses to run a Debug build. If a PR claims a
performance win, paste the before and after tables into the PR body, from the same machine and the
same run session. `benchmarks.md` holds the last published run and the environment it was measured
on; update it only when you re-ran the whole suite.

## How a PR is reviewed and merged

1. Branch from `master`. Name the branch after intent: `feat/...`, `fix/...`, `docs/...`,
   `refactor/...`, `perf/...`, `chore/...`, `test/...`.
2. One conceptual change per PR. Refactors and behaviour changes go in separate PRs even when the
   diff feels small. A PR that fixes three unrelated things gets asked to become three PRs.
3. Conventional Commits subject (`fix(outbox): ...`, `feat(aspnetcore): ...`). The subject says what
   changed, the body says why.
4. Fill in [the PR template](.github/PULL_REQUEST_TEMPLATE.md): what changed, why, how you verified
   it, and the CHANGELOG entry.
5. CI must be green: build and test on all three SDKs. A red matrix leg is a blocker, not a flake,
   until you have shown otherwise.
6. Anything user-visible gets a line under `## [Unreleased]` in `CHANGELOG.md`, in the right
   subsection (`Added` / `Changed` / `Fixed` / `Removed`). Write it for someone upgrading, and say
   what they have to do differently.
7. Public API changes need XML docs and, if they change behaviour, a note in the affected package's
   `docs/README.md` too. That file is the NuGet readme and it is expected to match the code.
8. The maintainer reviews. Expect questions about why the change has to exist at all, and about the
   size of the new public surface. Answer them in the thread rather than by pushing more code.
9. Merges are squash merges onto `master`. The squash subject is the PR title, so title the PR the
   way you want the commit to read.
10. No `Co-Authored-By` trailers and no generated-by footers. The author of the PR is the author of
    the work.

## Reporting bugs

Use the [bug report form](https://github.com/tunahanaliozturk/OrionGuard/issues/new?template=bug_report.yml).
It asks for the package and version, the target framework, and a minimal reproduction, because
without those the first reply is always a request for them.

If the bug has security implications, do not open a public issue. Contact the maintainer directly at
the address in the package NuGet metadata.

## Conduct

Be kind. We follow the [Code of Conduct](CODE_OF_CONDUCT.md). Disagreement is fine; rudeness is not.

## License

By submitting a pull request, you agree your contribution is licensed under the repo's
[MIT License](src/Moongazing.OrionGuard/docs/LICENSE.txt).
