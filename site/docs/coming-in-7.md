# In development for 7.0.0

> [!NOTE]
> Placeholder page. The work below is in progress for the 7.0.0 release. Nothing here has a
> documented API yet, and names may change before release. This page exists so the table of contents
> matches what is being built; it will be replaced by real pages when the features land.

| In progress | What it is meant to do |
| --- | --- |
| `OrionGuard.MassTransit` | Validation for MassTransit message consumers. |
| `OrionGuard.Aspire` | .NET Aspire integration for the OrionGuard services. |
| JSON Schema and TypeScript export | Producing a JSON Schema, and TypeScript types, from an OrionGuard validator. |
| Snapshot testing for validators | Pinning a validator's output in a test, and failing when it drifts. |

Until they ship, follow them in the
[roadmap](https://github.com/tunahanaliozturk/OrionGuard/blob/master/docs/ROADMAP.md) and the
[changelog](https://github.com/tunahanaliozturk/OrionGuard/blob/master/CHANGELOG.md), and raise
questions as issues on the repository.

7.0.0 is also the release in which the obsolete `IExceptionFactory` surface
(`AddOrionGuardExceptionFactory<T>()`, `ExceptionFactoryProvider.Configure`,
`DefaultExceptionFactory`) is removed. No guard has ever called it; catch `GuardException` at your
boundary and translate it there instead.
