# Before / after

The same Lemon Squeezy License API client in five languages. In every pair the only difference
is the host:

| Language | Before | After | The change |
|---|---|---|---|
| JavaScript / TypeScript (Node 18+, Electron, browsers, Deno, Bun) | [before.ts](js-ts/before.ts) | [after.ts](js-ts/after.ts) | line 2 |
| Python | [before.py](python/before.py) | [after.py](python/after.py) | line 4 |
| C# / .NET | [Before.cs](dotnet/Before.cs) | [After.cs](dotnet/After.cs) | line 6 |
| Swift | [Before.swift](swift/Before.swift) | [After.swift](swift/After.swift) | line 5 |
| Go | [before/license.go](go/before/license.go) | [after/license.go](go/after/license.go) | line 12 |

```diff
- const LICENSE_API = 'https://api.lemonsqueezy.com/v1/licenses';
+ const LICENSE_API = 'https://licenses.exisone.com/v1/licenses';
```

Check it yourself: `diff js-ts/before.ts js-ts/after.ts`.

Requests and responses follow Lemon Squeezy's documented behavior: form fields `license_key`,
`instance_name`, `instance_id`; JSON bodies with `activated` / `valid` / `deactivated`, `error`,
`license_key`, `instance` and `meta`; the same HTTP status codes. Check your own keys with the
**Compare with Lemon Squeezy** tool before you switch (see the [checklist](../checklist.md)). For
example, an unknown key:

```json
{"valid":false,"error":"license_key not found."}
```

If your app uses an official or community Lemon Squeezy SDK for these calls, look for its base
URL option and set it to `https://licenses.exisone.com`. If it has none, replace the three calls
with the file for your language here.

Better still, read the base URL from your app's configuration so the next change needs no app
update (see the [checklist](../checklist.md)).
