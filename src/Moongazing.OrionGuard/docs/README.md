# OrionGuard

A modern, fluent, and extensible guard clause & validation library for .NET 8/9.

## Features

- Fluent API — `Ensure.That(email).NotNull().Email()`
- Security Guards — SQL injection, XSS, path traversal, command injection, LDAP, XXE
- Format Guards — Coordinates, MAC, CIDR, JWT, country codes, time zones, Base64
- FastGuard — Span-based zero-allocation validation with ThrowHelper optimization
- Result Pattern — Collect all errors with `GuardResult.Combine()`
- Async Validation — Database lookups, API calls
- Object Validation — Property expressions, cross-property rules, compiled caching
- Business Guards — Money, SKU, coupon, scheduling, status transitions
- Attribute-Based — `[NotNull]`, `[Email]`, `[Range]` on model properties
- DI Support — `services.AddOrionGuard()`
- Localization — 8 languages, thread-safe per-request culture

## Quick Start

```csharp
using Moongazing.OrionGuard.Core;
using Moongazing.OrionGuard.Extensions;

Ensure.That(email).NotNull().NotEmpty().Email();
FastGuard.NotNullOrEmpty(name, nameof(name));
userInput.AgainstInjection(nameof(userInput));
"US".AgainstInvalidCountryCode(nameof(country));
```

## Links

- [GitHub](https://github.com/Moongazing/OrionGuard)
- [Changelog](https://github.com/Moongazing/OrionGuard/blob/master/CHANGELOG.md)

## License

MIT
