---
title: Security model
order: 20
---

# Security model

Two networks, four keys, one rule: a port being open never means a surface being readable.

## The two networks

**Public.** Cloudflare fronts `rms.vtilserver.com` and `rms-api.vtilserver.com` and terminates TLS. The origin
sees plain HTTP from Cloudflare, so the forwarding headers are believed only when the connection comes from one
of Cloudflare's published ranges. `X-Forwarded-For` and `X-Forwarded-Proto` are consumed; `X-Forwarded-Host` is
deliberately not, because nothing in this stack needs it and honouring it is how a host-header attack gets a
foothold. `SCANNER_TRUSTED_PROXIES` replaces the list; the single word `none` trusts nobody; a malformed entry
fails at start-up rather than being ignored.

**Internal.** Every service binds loopback by default and refuses a wildcard bind unless the deployment has
explicitly allowed it. The allow-list is evaluated against the socket's peer address, never against a header,
because a header is written by the caller.

## The four keys

| Surface | Header | What it can do |
| --- | --- | --- |
| Indexer control | `X-Indexer-Token` | Rebuild the index. |
| DevOps | `X-Devops-Key` | Read the deployment's state, runs and logs. |
| Configuration | `X-Configuration-Key` | **Read and rewrite the deployment's manifests.** |
| Documentation | `X-Docs-Key` | Read the documentation directory. |

Each is compared in fixed time over its raw bytes. A key that is absent or shorter than 32 characters **disables
the surface** rather than being accepted — a copy-paste accident cannot leave a guessable secret in place, and a
missing secret cannot silently become an open one. A disabled surface answers `404`, because `403` would confirm
there was something there to forbid; a refusal on a live surface answers `403` and says nothing about which check
refused it.

The Configuration key is separate from the DevOps key on purpose. Two services accepting the same header would be
two services reachable with one secret, and one of them can rewrite what the machine runs.

**The browser never holds a key.** Every site call goes through a same-origin proxy or fragment handler that
appends the key server-side. A proxy that answered with the key in a URL, a header or a page would put it in the
browser's history and in any extension the user has installed.

## The indexer's control plane

The indexer can rebuild the index, which is expensive, so its control surface is the tightest: loopback by
default, CIDR allow-list on the socket peer, a fixed-time token, a 60 request per minute ceiling per peer, a 4 KB
body limit, and `404` when disabled. A wildcard bind requires an explicit opt-in.

## The sites

- One SQLite database per site under `App_Data`, holding accounts, notices and the audit trail. Accounts are
  per-site; there is no shared identity provider.
- Passwords are PBKDF2-HMAC-SHA256 with a 16-byte salt and a 32-byte hash. The iteration count is stored per row
  so it can be raised without invalidating existing accounts.
- Two roles only: `Administrator` and `Viewer`. The store refuses to remove or disable the last enabled
  administrator, and says so instead of quietly allowing it.
- The identity travels in a forms-auth ticket whose payload is the **account id only**. The account is re-read on
  every request, so disabling somebody takes effect immediately rather than when their ticket expires.
- A failed sign-in produces one message whatever the reason. The reason — unknown account, wrong password,
  disabled, locked out — goes to the audit trail only. An unknown account still performs a decoy derivation, so
  the response time does not distinguish it from a wrong password.
- Every state-changing form carries a CSRF token and is a POST. A forged or missing token is a redirect back to
  the page with a status code, not a `200` that looks like a failure.

## Content security

The sites render no inline script and no inline style, so the policy is strict:

```
script-src 'self'; style-src 'self'; img-src 'self'; frame-ancestors 'none'
```

Reaching that took two decisions. Pages carry no server-side Web Forms controls, no `<form runat="server">` and
no view-state script, so nothing emits script that the policy would have to allow. And live regions are refreshed
with server-rendered HTML fragments from `.ashx` handlers rather than with JSON assembled in the browser, so the
browser never needs a JSON parser or a template.

The policy is emitted as a `<meta http-equiv="Content-Security-Policy">` element in the body, alongside
`X-Content-Type-Options: nosniff`, `Referrer-Policy: same-origin` and `X-Frame-Options: DENY`.

`style-src 'self'` forbids `style=""` attributes too, which is why every bar width and colour in the UI is a
class in `Content/site.css` rather than a computed inline style.

## Paths and input

- **No service builds a path from caller text.** The Configuration service maps four known manifest names to a
  configured directory; the documentation service maps a slug to an entry produced by enumerating the directory.
  A request cannot name a file.
- **No request body is unbounded.** The DevOps service takes 4 KB, the Configuration service twice its document
  ceiling, the documentation service 1 KB because it accepts no body at all.
- **Every query is parameterised.** There is no string concatenation into SQL anywhere in the index reader or the
  site stores.
- **Everything rendered is encoded first**, then marked up. `Html.Encode` is the single place a value stops being
  text, which is why a URL in a page can contain a raw `&` — the escaping happens once, at the boundary.
- A file over the configured ceiling is reported and refused, never truncated. Half a document served as though
  it were whole is worse than a document that is visibly absent.

## Secrets

Secrets arrive through the environment and never through a file in this repository:

- `SCANNER_API_KEY` — the API key, a GUID.
- `ROBLOX_COOKIE` — the Roblox session cookie, for asset delivery. The scanner publishes its value to `ROBLOX_COOKIE_FILE` at start-up and the API reads that file, so the credential an operator pastes once is the credential every process uses.
- `SCANNER_INDEXER_TOKEN_FILE`, `RMS_DEVOPS_TOKEN_FILE`, `RMS_CONFIGURATION_TOKEN_FILE`, `RMS_DOCS_TOKEN_FILE` —
  one file per service, generated at deploy time.
- `SITE_SERVICE_KEY_FILE` or `SITE_SERVICE_KEY` — the key a site presents. The file wins; a file that cannot be
  read disables the call rather than falling back to the setting.
- `SITE_BOOTSTRAP_USER` / `SITE_BOOTSTRAP_PASSWORD` — the first administrator a site creates.

No key is ever logged, and no refusal reason contains one. Request-line logging is filtered out of ASP.NET's own
diagnostics precisely because a request line can carry `?apiKey=`.

## What the threat model actually is

The stack's exposure is not a public web application being attacked from outside; Cloudflare and the site's own
authentication are in front. The realistic failures are:

1. **An internal port left open.** Answered by loopback defaults, refusal of wildcard binds, and key guards that
   disable rather than relax.
2. **A key leaking to a browser.** Answered by never sending one.
3. **A caller reaching the file system.** Answered by never building a path from caller text.
4. **An operator making a change nobody can trace.** Answered by the audit trail and by the Configuration
   service's precondition: a write carries the hash of the document it was based on, so a write that lost a race
   is a `409` naming the current hash rather than a silent last-writer-wins.
