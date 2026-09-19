# In development for 7.0.0

> [!NOTE]
> Placeholder page. The work below is in progress for the 7.0.0 release. Nothing here has a
> documented API yet, and names may change before release. This page exists so the table of contents
> matches what is being built; each entry is replaced by a real package page when the feature lands.

| In progress | What it is meant to do |
| --- | --- |
| `OrionGuard.Aspire` | .NET Aspire integration, so an OrionGuard-instrumented service reports through the Aspire dashboard. |
| `OrionGuard.SchemaExport` | Producing a JSON Schema, and TypeScript types, from an OrionGuard validator. |
| A `dotnet new` template pack | Project and validator templates with the wiring already in place. |
| Snapshot testing for validators | Pinning a validator's rule set in a test, and failing when it drifts. |

A package appears on this page or in [Packages](../packages/index.md), never in both, and the
[API reference](../api/index.md) covers exactly the documented ones: `docfx.json` lists the projects
it reads, so a package that is still in progress cannot reach the site before it has a page. When
one of these lands, add its project to that list, add a page that includes its README, and remove
its row here.

Until then, follow them in the
[roadmap](https://github.com/tunahanaliozturk/OrionGuard/blob/master/docs/ROADMAP.md) and the
[changelog](https://github.com/tunahanaliozturk/OrionGuard/blob/master/CHANGELOG.md), and raise
questions as issues on the repository.

7.0.0 is also the release in which the obsolete `IExceptionFactory` surface
(`AddOrionGuardExceptionFactory<T>()`, `ExceptionFactoryProvider.Configure`,
`DefaultExceptionFactory`) is removed. No guard has ever called it; catch `GuardException` at your
boundary and translate it there instead.
