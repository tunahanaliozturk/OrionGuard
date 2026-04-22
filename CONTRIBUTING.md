# Contributing to OrionGuard

First off — thank you for considering a contribution. OrionGuard is a single-maintainer project, so clear, focused contributions that carry their own tests and documentation land the fastest. This guide tells you what "good" looks like here, and what will bounce.

## Table of contents

- [Code of conduct](#code-of-conduct)
- [Before you start](#before-you-start)
- [Ways to contribute](#ways-to-contribute)
- [Local development setup](#local-development-setup)
- [Repository layout](#repository-layout)
- [Coding standards](#coding-standards)
- [Testing rules](#testing-rules)
- [Documentation requirements](#documentation-requirements)
- [Commit & PR conventions](#commit--pr-conventions)
- [Security issues](#security-issues)
- [Release process](#release-process)
- [License](#license)

## Code of conduct

Be kind, be specific, be technical. Attack ideas, not people. If a behaviour would feel out of place on a mature .NET project (e.g. dotnet/runtime), it's out of place here too.

## Before you start

1. **Search first.** Check [existing issues](https://github.com/tunahanaliozturk/OrionGuard/issues) and open pull requests. Your idea may already be in motion.
2. **Discuss non-trivial changes in an issue before coding.** Anything touching public API, a new ecosystem package, a new localization language, or a cross-cutting refactor needs an issue with a short design sketch first. This protects your time — the maintainer may redirect the approach before you've written 500 lines.
3. **Trivial changes can go straight to a PR.** Typos, README fixes, new test cases for existing behaviour, XML doc clarifications — no issue required.

## Ways to contribute

| Kind | Expectation |
|------|-------------|
| Bug fix | Repro test first; fix second. |
| New guard / validator | New public API → discuss in issue first. Include XML docs, tests, localization keys if user-visible, CHANGELOG entry. |
| Localization | Add keys to every language block in `ValidationMessages.cs` — partial translations are rejected. |
| Source generator work | Emitter changes need at least one generator test asserting the exact emitted text shape. |
| Performance work | Include a BenchmarkDotNet run before/after. Numbers only — no hand-waving. |
| Documentation | Fine as a standalone PR. Keep it terse and technical. |
| New ecosystem package | Ask first. Comes with its own README, tests, version cadence. |

## Local development setup

**Toolchain:**

- .NET SDK 10.0 (also test against 8.0 and 9.0 if you can — solution multi-targets).
- Git 2.30+.
- Any editor with C# + Roslyn support (Visual Studio, Rider, VS Code + C# Dev Kit).

**Clone & build:**

```bash
git clone https://github.com/tunahanaliozturk/OrionGuard.git
cd OrionGuard
dotnet build Moongazing.OrionGuard.sln -c Release
```

**Run the full test suite:**

```bash
dotnet test Moongazing.OrionGuard.sln -c Release
```

All tests must pass on your branch before you open a PR. The main suite has 500+ tests; expect sub-second runtime per project.

**Run the benchmarks (optional, for perf PRs):**

```bash
dotnet run -c Release --project benchmarks/Moongazing.OrionGuard.Benchmarks -- <BenchmarkClassName>
```

**Run the demo:**

```bash
dotnet run --project demo/Moongazing.OrionGuard.Demo -c Release
```

The demo prints every major feature. If you add a user-visible feature, wire a section into the demo too.

## Repository layout

```
OrionGuard/
├── src/
│   ├── Moongazing.OrionGuard/                  core guard + validation library (PackageId: OrionGuard)
│   ├── Moongazing.OrionGuard.AspNetCore/       (PackageId: OrionGuard.AspNetCore)
│   ├── Moongazing.OrionGuard.Blazor/           (PackageId: OrionGuard.Blazor)
│   ├── Moongazing.OrionGuard.Generators/       (PackageId: OrionGuard.Generators)
│   ├── Moongazing.OrionGuard.Grpc/             (PackageId: OrionGuard.Grpc)
│   ├── Moongazing.OrionGuard.MediatR/          (PackageId: OrionGuard.MediatR)
│   ├── Moongazing.OrionGuard.OpenTelemetry/    (PackageId: OrionGuard.OpenTelemetry)
│   ├── Moongazing.OrionGuard.SignalR/          (PackageId: OrionGuard.SignalR)
│   └── Moongazing.OrionGuard.Swagger/          (PackageId: OrionGuard.Swagger)
├── tests/                                      xUnit tests for core + generators
├── benchmarks/                                 BenchmarkDotNet harness
├── demo/                                       Console app exercising every feature
├── docs/                                       Long-form feature guides, specs, plans
├── CHANGELOG.md                                Keep-a-Changelog format
└── README.md
```

The C# namespace layout mirrors the folder layout (`Moongazing.OrionGuard.AspNetCore` etc.) — only the published NuGet PackageIds dropped the `Moongazing.` prefix starting in v6.2.0.

## Coding standards

- **Target frameworks:** core + sub-packages multi-target `net8.0;net9.0;net10.0`. The source generator project is `netstandard2.0` (Roslyn constraint). Don't break the older TFMs without discussion.
- **Nullable reference types:** enabled project-wide. No `#nullable disable` — if the compiler complains, fix the code, not the pragma.
- **Warnings are errors.** Core and sub-packages have `TreatWarningsAsErrors=true`. `TreatWarningsAsErrors=false` is never acceptable in a PR.
- **XML docs on every public member.** `GenerateDocumentationFile=true` is on everywhere. Missing docs = failed build.
- **Analyzers:** `AnalysisLevel=latest-recommended`. CA1510 (prefer `ArgumentNullException.ThrowIfNull`) is enforced — follow it, unless you're throwing an OrionGuard-specific exception type (e.g. `NullValueException`) which stays hand-rolled.
- **File-scoped namespaces** in core and sub-packages. Block-scoped namespaces in the generator project (netstandard2.0 doesn't mind either way, but the existing style is block-scoped).
- **One type per file.** If you need a small helper type, put it in its own file unless it's a private nested class.
- **Formatting:** run `dotnet format` before committing. Tabs for indentation in csproj, 4-space indent in C# (Visual Studio default).
- **No unrelated refactors in a feature PR.** Keep the diff focused.

## Testing rules

- Every behaviour change needs a test. No exceptions for "it's obvious".
- Tests live next to the code: core behaviour → `tests/Moongazing.OrionGuard.Tests/`; generator behaviour → `tests/Moongazing.OrionGuard.Generators.Tests/`.
- **Test naming:** `<Method>_Should<ExpectedOutcome>_When<Condition>`. Legible at `dotnet test --filter` time.
- **TDD-friendly but not mandatory:** write the failing test before the fix if it's practical. For generator emitter changes it's almost always practical.
- No `Thread.Sleep` in tests. No flaky timing assertions — use ranges (`Assert.InRange`) or record real clock boundaries (`before`/`after` snapshots).
- Generator tests assert the emitted text shape — `Assert.Contains("expected snippet", generatedSource)`. Don't snapshot-test entire emitted files; they're too brittle.

## Documentation requirements

When you change user-visible behaviour:

1. **CHANGELOG.md** — add a bullet under the appropriate Keep-a-Changelog section (`### Added` / `### Changed` / `### Fixed` / `### Deprecated`) in the unreleased block, or if there's no unreleased block yet, under the top-most released version with a short note explaining you're proposing a new minor.
2. **README.md** — if the change surfaces a new public entry point, update the relevant example snippet.
3. **Per-package README** (`src/<Package>/docs/README.md`) — if the change is inside a sub-package, update its README too.
4. **XML docs** on the public API itself.
5. **Demo** (`demo/Moongazing.OrionGuard.Demo/Program.cs`) — if the feature is user-visible and has a nice 5–10 line demonstration, add a new numbered section. Don't duplicate what's already there.

## Commit & PR conventions

**Commit messages** follow [Conventional Commits](https://www.conventionalcommits.org/):

- `feat(guard): add AgainstPositive extension for decimal`
- `fix(generators): skip EF Core converter when type is missing`
- `docs(readme): document the v6.2 IParsable emission`
- `test(domain): cover CheckRule with async rule`
- `refactor(core): extract GuardResult builder`
- `chore(deps): bump MediatR to 12.4.1`
- `perf(fastguard): reduce allocations in NotNullOrEmpty`
- `release: bump to 6.2.0`

Scope (`guard`, `domain`, `generators`, `aspnetcore`, etc.) helps readers scan `git log`.

**Pull requests:**

- One logical change per PR. Multiple unrelated fixes → multiple PRs.
- **PR title:** same style as a commit subject.
- **PR body:** a short description, plus:
  - Why (link to the issue, or explain the problem in 2-3 sentences).
  - What (high-level summary of the change).
  - How you tested (which test files, which commands, benchmark numbers for perf PRs).
  - A note if the PR needs a CHANGELOG entry — usually yes.
- **Keep the PR green.** Push fixes, don't force-push history that rewrites other reviewers' context.
- **Squash-merge** is the default. Write the squashed commit message in the PR description so it's ready to go at merge time.

## Security issues

**Do not open public issues for security vulnerabilities.**

Email the maintainer directly — see `FUNDING.yml` or the `<Authors>` field in the core csproj for the current contact. Include:

- A minimal reproduction.
- The affected versions.
- Your disclosure expectations (coordinated disclosure, CVE, etc.).

You'll get an acknowledgement within a few business days and a patched release within two weeks for confirmed high-severity issues.

## Release process

Releases are cut by the maintainer only. The flow is:

1. All planned changes merged to `master`.
2. Version bumped across all 9 csprojs in a single `release:` commit.
3. CHANGELOG moves the unreleased block under the new version header with an ISO date.
4. `PackageReleaseNotes` in the core csproj gains a new version block.
5. A git tag `vX.Y.Z` is pushed; GitHub Actions publishes to NuGet.

If you want to help shape the next release, open an issue describing the proposal and link to your PR (if you have one ready).

## License

By contributing, you agree that your contributions will be licensed under the [MIT License](src/Moongazing.OrionGuard/docs/LICENSE.txt), the same licence the project ships under. No CLA required.

---

Thanks again. Contributions of any size are welcome — even a single test case that covers an overlooked edge case is a real improvement.
