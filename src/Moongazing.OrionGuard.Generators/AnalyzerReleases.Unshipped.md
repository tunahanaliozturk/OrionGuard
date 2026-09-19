; Unshipped analyzer release.
; Add analyzer rules here as they are developed for the next release.

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
OG0002  | Usage    | Warning  | A property of a [GenerateValidator] type carries a ValidationAttribute the generator does not translate, so the generated validator does not enforce it.
OG0003  | Usage    | Warning  | A property of a [GenerateValidator] type has a getter the generated validator cannot call, so its validation attributes are not enforced.
