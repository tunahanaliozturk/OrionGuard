# site

The documentation site and the browser playground. Neither is part of `Moongazing.OrionGuard.sln`,
and building the solution does not build either of them.

```text
site/
  docfx.json              DocFX configuration: API metadata, content, the playground as a resource
  dotnet-tools.json       local tool manifest, pinning DocFX
  toc.yml                 top navigation
  index.md                landing page and 30-second quick start
  docs/                   getting started and the guides
  packages/               one page per package; each includes src/<project>/docs/README.md
  api/                    index.md is written by hand; the *.yml next to it are generated
  playground/             Blazor WebAssembly app, published into the site under /playground/
  _site/                  build output (generated)
```

## Prerequisites

- .NET SDK 10.0 or later (the playground targets `net10.0`; DocFX reads the projects through MSBuild).
- No `wasm-tools` workload is needed. `dotnet publish` prints a recommendation to install it; it only
  affects size optimization of the published app.

Restore the pinned DocFX once per clone:

```bash
cd site
dotnet tool restore
```

## Build

Run both commands from `site/`. The playground comes first, because DocFX copies its published
output into the site.

```bash
# 1. Publish the playground. Its static files land in site/playground/bin/publish/wwwroot.
dotnet publish playground -c Release -o playground/bin/publish

# 2. Generate the API metadata from the src projects, then build the site.
dotnet docfx docfx.json
```

`dotnet docfx docfx.json` runs metadata and build in one pass. To run them separately, use
`dotnet docfx metadata docfx.json` and `dotnet docfx build docfx.json`.

The finished site is `site/_site/`. Inside it:

- `_site/index.html` — landing page
- `_site/api/` — API reference generated from the XML docs
- `_site/playground/` — the published playground, copied verbatim from
  `playground/bin/publish/wwwroot` by the `resource` entry in `docfx.json`. The playground's
  `index.html` uses `<base href="./" />`, so it works at any path, including a project-page URL such
  as `https://<user>.github.io/OrionGuard/playground/`.

DocFX always warns `InvalidFileLink: ~/playground/index.html` for the pages that link to the
playground. It copies resource files without registering them as link targets, so it cannot see a
file it has itself copied; the links work in the output. Skipping step 1 is therefore also allowed —
the same warnings appear and the rest of the site builds normally, only without a playground.

## Preview

```bash
dotnet docfx docfx.json --serve
```

That serves `_site/` on <http://localhost:8080>, playground included. `--serve` does not watch for
changes: rebuild to see edits.

To work on the playground alone, run its dev server, which rebuilds on restart and serves at the
URL it prints:

```bash
dotnet run --project playground
```

The dev-server build is not trimmed, while the published one is. Test anything size- or
trimming-sensitive against the published output. The dev server also serves the app at the site
root, so the playground's links back to the documentation (`../`) only resolve in the built site.

## How the pages are put together

- **Package pages** are one line: an `[!INCLUDE]` of `src/<project>/docs/README.md`, plus links into
  the API reference. Edit the package README, never the page, so NuGet and the site cannot drift
  apart.
- **Guides** under `docs/` are written for the site. Their C# snippets are expected to compile
  against the current `src/` projects; verify a snippet you change by pasting it into a scratch
  project that references those projects.
- **API pages** come from `dotnet docfx metadata`, which reads the `net10.0` build of every runtime
  package. `OrionGuard.Generators`, `OrionGuard.OpenApi` and `OrionGuard.Migration` are excluded in
  `docfx.json`: the first two are Roslyn components and the third is a CLI tool.
- The DocFX version is pinned in `dotnet-tools.json`. Changing it is a deliberate act; rerun the
  build and check the output before committing a bump.

## Playground notes

`playground/` is a standalone Blazor WebAssembly app referencing
`src/Moongazing.OrionGuard/Moongazing.OrionGuard.csproj`. It runs the JSON rule engine and a handful
of guards in the browser; nothing is sent anywhere.

Two things about WebAssembly are worth knowing before changing it:

- **Framework exception messages.** Publishing trims the app, and trimming replaces framework
  resource strings with their keys, so `ArgumentException.Message` would end in
  `Arg_ParamName_Name, input` instead of `(Parameter 'input')`. The project sets
  `UseSystemResourceKeys=false` to keep the real strings, because those messages are the point of the
  page.
- **NFKC normalization is missing on WebAssembly.** `AgainstPathTraversal` normalizes its input
  before looking for traversal sequences, so a non-ASCII value throws
  `PlatformNotSupportedException` in the browser. The playground catches it and shows the guard as
  "unavailable in the browser" rather than as a rejection. Server-side runtimes are unaffected.

Reflection survives trimming here (the rule engine reads properties by reflection, and the input JSON
is deserialized without a source generator), because a Blazor WebAssembly app is trimmed
conservatively and the app's own assemblies are kept.
