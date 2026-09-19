# Contributing and roadmap

The repository is [tunahanaliozturk/OrionGuard](https://github.com/tunahanaliozturk/OrionGuard).
Issues and pull requests are welcome.

- [CONTRIBUTING.md](https://github.com/tunahanaliozturk/OrionGuard/blob/master/CONTRIBUTING.md) —
  how to build and test, the shape a pull request should have, coding style, and how to report a bug
  or a security issue.
- [Code of Conduct](https://github.com/tunahanaliozturk/OrionGuard/blob/master/CODE_OF_CONDUCT.md)
- [Roadmap](https://github.com/tunahanaliozturk/OrionGuard/blob/master/docs/ROADMAP.md) — the
  next releases through 7.0.0, plus the backlog of everything under consideration. If an item there
  matters to you, open an issue with the `roadmap` label; demand is what moves items up.
- [Changelog](https://github.com/tunahanaliozturk/OrionGuard/blob/master/CHANGELOG.md)
- [In development for 7.0.0](coming-in-7.md) — the short version of what is being built now.

## Working on the documentation

The site lives in `site/` in the repository and is built with DocFX, pinned in a local tool
manifest. `site/README.md` has the commands to build and preview it, including the browser
playground.

Three rules save most of the review time:

- A package page includes that package's `src/<project>/docs/README.md`. Edit the README, not the
  page, so NuGet and the site cannot disagree.
- Every C# snippet in the guides is expected to compile against the current source. If you change a
  snippet, compile it.
- A new package under `src/` reaches the API reference only when it is added to the project list in
  `site/docfx.json`. Add it together with its package page, and take it off
  [In development for 7.0.0](coming-in-7.md) in the same change, so the site never half-documents a
  package.
