; Unshipped analyzer release.
; Add analyzer rules here as they are developed for the next release.

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
OG1010  | OrionGuard.OpenApi | Warning | The [OpenApiValidator] target type is generic (or is nested inside a generic type), which the generator does not support yet; no validator was generated.
OG1011  | OrionGuard.OpenApi | Warning | The [OpenApiValidator] nested target, or one of its enclosing types, is not declared partial, so it cannot be reopened and no validator was generated.
