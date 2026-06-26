; Shipped analyzer releases.
; See https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 6.7.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
OG1001  | OrionGuard.OpenApi | Error   | The OpenAPI document named by [OpenApiValidator] was not supplied as an AdditionalFile.
OG1002  | OrionGuard.OpenApi | Error   | The OpenAPI document could not be parsed as JSON (YAML is not supported yet).
OG1003  | OrionGuard.OpenApi | Error   | The JSON pointer did not resolve to a schema in the OpenAPI document.
OG1004  | OrionGuard.OpenApi | Error   | A $ref inside the OpenAPI document could not be resolved.
OG1005  | OrionGuard.OpenApi | Warning | The [OpenApiValidator] target type is not a partial class, so no validator could be generated.
OG1006  | OrionGuard.OpenApi | Warning | The schema uses an OpenAPI construct that the generator does not yet support; the construct was skipped.
OG1007  | OrionGuard.OpenApi | Warning | An integer schema keyword (minLength, maxLength, minItems, maxItems) had a value that is not an integer or is outside the supported range; the constraint was skipped.
OG1008  | OrionGuard.OpenApi | Warning | The [OpenApiValidator] target type does not participate in the OrionGuard IValidator&lt;T&gt; contract, so no validator was generated.
OG1009  | OrionGuard.OpenApi | Error   | The document name in [OpenApiValidator] matched more than one AdditionalFile; the match was ambiguous.
