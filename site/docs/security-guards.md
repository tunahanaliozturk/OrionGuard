# Security guards

`Moongazing.OrionGuard.Extensions.SecurityGuards` holds two different kinds of check, and the
difference matters more than anything else on this page.

- **Heuristics.** `AgainstSqlInjection`, `AgainstXss`, `AgainstCommandInjection`,
  `AgainstLdapInjection`, `AgainstXxe` and `AgainstInjection` are denylists. They reject input that
  contains a known attack token. A payload written in a form the list does not know passes, and
  ordinary text that happens to contain a listed token is rejected. **They are not a defence.**
- **Structural guards.** `AgainstPathTraversal`, `AgainstPathEscape`, `AgainstOpenRedirect` and
  `AgainstUnsafeFileName` decide from the shape of the value, not from a list of attacks. Each one
  states what it guarantees, and each guarantee is narrow.

Null or empty input is a no-op for all of them unless the guard says otherwise, so add `NotNull` or
`NotEmpty` upstream when presence is required. None of them sanitize: they only reject.

## The heuristics, and what actually defends you

| Guard | What it looks for | The defence |
| --- | --- | --- |
| `AgainstSqlInjection` | SQL keywords, comment sequences (`--`, `/*`), `xp_`/`sp_` prefixes, `INFORMATION_SCHEMA`, quoted tautologies such as `'1'='1`, `GRANT ... TO` | Parameterized queries: ADO.NET parameters, EF Core, Dapper parameters. Never concatenate input into SQL, whether or not it passed the guard. |
| `AgainstXss` | `<script`, `<iframe`, `javascript:`/`vbscript:`, DOM sinks (`document.cookie`, `innerHTML`), and event-handler attributes — also after decoding character references, dropping tab/CR/LF and removing whitespace before `=`, so `jav&#x61;script:` and `onerror =` are caught | Contextual output encoding: Razor and Blazor encode by default; otherwise use the HTML, attribute, JavaScript or URL encoder that matches where the value lands. Add a Content-Security-Policy. To accept markup, run it through an HTML sanitizer. |
| `AgainstCommandInjection` | Shell metacharacters (pipe, ampersand, semicolon, backtick, dollar, angle brackets, single and double quotes, CR, LF), interpreters and download tools (`cmd.exe`, `powershell`, `/bin/sh`, `curl`, ...), and a leading `-`, which a program would read as an option | Start the process without a shell and pass each argument separately through `ProcessStartInfo.ArgumentList`. Validate arguments against an allow-list of expected values, and end options with `--` where the program supports it. |
| `AgainstLdapInjection` | LDAP filter metacharacters (`* ( ) \ /`, NUL, CR, LF) | RFC 4515 escaping for values in a filter, RFC 4514 escaping for values in a distinguished name. The guard does not cover DNs at all: `jdoe,ou=Admins` passes it. |
| `AgainstXxe` | A `<!DOCTYPE` declaration, or an `<!ENTITY>` and `SYSTEM` pair | The parser configuration: `XmlReaderSettings.DtdProcessing = DtdProcessing.Prohibit` and `XmlResolver = null`. |
| `AgainstInjection` | The SQL, XSS and path-traversal checks plus the shell-operator subset of the command check, for screening free text | All of the above. It deliberately allows rooted paths, quotes, `&`, `<`, `>`, `$` and line breaks, which are ordinary in prose; use `AgainstCommandInjection` for a value that reaches a command line. |

So what are the heuristics for? Turning obviously hostile input away at the edge, and putting it in
your logs. Treat a rejection as a signal, not as proof that everything else is safe — and treat a
pass as no signal at all.

```csharp
using System.Data.Common;
using Moongazing.OrionGuard.Extensions;

public static class ArticleSearch
{
    public static DbCommand BuildQuery(DbConnection connection, string term)
    {
        // Heuristic screening: cheap, rejects the obvious, and gives the log something to show.
        term.AgainstSqlInjection(nameof(term));

        // The defence: the value travels as a parameter, never as SQL text.
        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Title FROM Articles WHERE Title LIKE @term";

        var parameter = command.CreateParameter();
        parameter.ParameterName = "@term";
        parameter.Value = $"%{term}%";
        command.Parameters.Add(parameter);

        return command;
    }
}
```

## False positives are part of the deal

`AgainstSqlInjection` rejects "Please update my delivery address" because `UPDATE` is on the list.
`AgainstXss` rejects a comment containing `alert(`. On a free-text field — a bio, a message, a
support ticket — that is a bug report waiting to happen.

Use them on fields with a narrow shape (an identifier, a code, a file name), or on a path where a
rejection is cheap. For free text, encode on output and let it through. You can see both sides in
[the playground](../playground/index.html): type an ordinary sentence and watch the SQL guard fire.

## The structural guards

### `AgainstPathTraversal(parameterName)`

Rejects any `..`; a leading `/` or `\` (Unix-absolute, Windows root-relative and UNC
`\\server\share` paths); a drive prefix such as `C:`; a value still changing after three URL
decodes; and ill-formed UTF-16. The value is checked as given, after repeated URL decoding
(`%2e`, `%252e`, ...) and after Unicode NFKC normalization, so `.%2e/`, `%252e%252e%252f` and
fullwidth `．．／` are caught. The checks are the same on every operating system.

It does not promise that a path stays inside a directory once combined with one, and it says nothing
about symbolic links, junctions, Windows device names (`CON`, `NUL`) or alternate data streams. Note
that `Path.Combine(root, value)` discards `root` when `value` is rooted.

### `AgainstPathEscape(root, parameterName)`

The one to use when a request names a file under a directory. It resolves the path and returns the
resolved full path:

```csharp
using Moongazing.OrionGuard.Extensions;

public static class UserUploads
{
    public static byte[] Read(string root, string requestedPath)
    {
        // Rejects rooted values, NUL characters, and anything that resolves outside root.
        var fullPath = requestedPath.AgainstPathEscape(root, nameof(requestedPath));

        // Open the resolved path the guard returned, not one re-combined from the inputs.
        return File.ReadAllBytes(fullPath);
    }
}
```

It guarantees that `Path.GetFullPath(Path.Join(root, value))` starts with the full path of `root`
plus a separator, compared ordinally, so every `..` resolves the way the operating system resolves
it. It does not URL-decode the value — pass it exactly as it will reach the file system — and it
cannot see symbolic links inside the root, nor changes made between the check and the open.

### `AgainstOpenRedirect(parameterName, params allowedDomains)`

Follows ASP.NET Core's `IsLocalUrl`. A local path starts with `/` not followed by a second `/`, or
with `~/` not followed by `/`. Whitespace, control characters and `\` are rejected outright, because
browsers drop tab/CR/LF and read `\` as `/`, which turns `/\evil.example` into a protocol-relative
URL. Everything else must be an absolute `http`/`https` URL, not a UNC path, whose host is in
`allowedDomains` or a subdomain of one. An empty array therefore allows local paths only.

```csharp
using Moongazing.OrionGuard.Extensions;

public static class LoginRedirect
{
    public static string Safe(string? returnUrl)
    {
        if (string.IsNullOrEmpty(returnUrl))
        {
            return "/";
        }

        // Throws for //evil.example, https://evil.example/, javascript:..., page.html, /\evil.example.
        returnUrl.AgainstOpenRedirect(nameof(returnUrl), "example.com");
        return returnUrl;
    }
}
```

### `AgainstUnsafeFileName(parameterName)`

Rejects empty values, everything `AgainstPathTraversal` rejects, and characters
`Path.GetInvalidFileNameChars()` reports. That last set is decided by the running operating system,
so a name accepted on Linux can still be invalid on Windows. Prefer a generated name and keep the
uploaded one as a label.

## Performance

Multi-pattern checks use `SearchValues<string>` on .NET 9 and later, which is SIMD-based and does not
get slower as the list grows, and a `FrozenSet<string>` scan on .NET 8. Character-set checks use
`SearchValues<char>` on every target. The regular expression for SQL tautologies is
`[GeneratedRegex]` with a 1000 ms timeout and a bounded scan, so hostile input cannot make it
quadratic.

## Running in a browser

`AgainstPathTraversal` normalizes with `NormalizationForm.FormKC` before it looks for traversal
sequences, and browser ICU does not carry the data for the compatibility forms `FormKC` and `FormKD`
([runtime source](https://github.com/dotnet/runtime/blob/release/10.0/src/libraries/System.Private.CoreLib/src/System/Globalization/Normalization.Icu.cs)).
A non-ASCII value therefore throws `PlatformNotSupportedException` there instead of being judged;
ASCII values are returned by a fast path before normalization is attempted and work normally. This
affects `AgainstPathTraversal`, `AgainstUnsafeFileName` and `AgainstInjection`. Setting
`BlazorWebAssemblyLoadAllGlobalizationData` does not change it — the check is on the platform and the
normalization form, not on which ICU shard was loaded. The playground shows it: pick the fullwidth
`．．／secret.txt` sample.

A server-side runtime is unaffected, with one exception that fails the other way round. In
globalization-invariant mode (`InvariantGlobalization=true`, common in trimmed and container builds)
normalization is skipped instead of attempted: [the string comes back unchanged and `IsNormalized`
always returns `true`](https://github.com/dotnet/runtime/blob/main/docs/design/features/globalization-invariant-mode.md#string-normalization).
Nothing throws, so there is no signal at all — the literal and URL-decoded passes keep working while
the normalized pass silently stops catching the fullwidth forms. Of the two, the browser's exception
is the kinder failure.

## See also

- [Moongazing.OrionGuard.Extensions](xref:Moongazing.OrionGuard.Extensions) — every guard, with its
  remarks, in the API reference.
- [Playground](../playground/index.html) — run these guards against your own values.
- [OrionGuard package page](../packages/orionguard.md) — where the guards live.
