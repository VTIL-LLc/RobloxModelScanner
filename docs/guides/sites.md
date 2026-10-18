---
title: The sites
order: 50
---

# The sites

Four Web Forms applications on .NET Framework 4.8, sharing one library and one shape. Three of them have a
service behind them; one has the API.

| Site | Behind it | Audience |
| --- | --- | --- |
| `Scanner.WebForms` | `Scanner.MicroApi` | Anyone. The public catalogue. |
| `RMS.Devops.Site` | `RMS.Devops.Service` | Operators. What the deployment is doing. |
| `RMS.Configuration.Site` | `RMS.Configuration.Service` | Operators. The deployer's manifests. |
| `RMS.Docs.Site` | `RMS.Docs.Service` | Anyone inside. This documentation. |

## What they share

`RMS.Site.Common` holds the chrome, the HTML rendering, the same-origin service client, forms authentication
against a per-site SQLite store, notices and the audit trail. A page overrides its title, its active navigation
section and whether it uses chrome — and nothing else. The navbar, the head, the footer and the page document are
rendered by the shared base class, so a change to the chrome is one change and not four.

The sites reference it, not each other, and none of them references a service project. They talk to their service
over HTTP through the same client, so a site cannot quietly acquire a compile-time dependency on a service's
internals.

## Pages

**The public catalogue.** A filter form and a server-rendered table: score, tags, creator, scripts. A model page
shows one asset, its detections, its metadata and a download link. Three more pages — analytics, the rule
reference and the catalogue itself — are live regions.

It has no control surface and no JSON proxy. The catalogue is a plain GET form and a rendered table, which is what
keeps the content security policy at `script-src 'self'` without an exception. It is the only site that uses
webpack: JavaScript only, emitted unminified and without source maps, and CSS stays hand-written.

A download is resolved server-side. The browser is sent to a validated Roblox location by a handler, not handed a
URL that a script could construct.

**The DevOps console.** The services in the plan and their state, one service's runs and its log, the published
artifacts and their versions, and a summary of the catalogue scan. The log viewer asks a handler for more text as
it scrolls and stops when the run is complete or the ceiling is reached.

**The configuration console.** The four manifests, their parsed contents, the generated plan, the deployer's
vocabulary and the requirements each manifest declares. The editor posts to its own page with two submit buttons:
one checks without touching anything, one saves. A save carries the hash of the document it was based on, so a
save that lost a race is refused with the current hash rather than silently overwriting somebody else's edit.

**The documentation site.** The catalogue of documents as a navigation, one document rendered, search, and the
admin console. It renders Markdown server-side into HTML built from encoded text, so a document cannot inject
markup into a page.

## Authentication

Each site has **its own** SQLite database under `App_Data`, holding accounts, notices and the audit trail. There is
no shared identity: an account on one console is not an account on another.

- Passwords: PBKDF2-HMAC-SHA256, 16-byte salt, 32-byte hash, iteration count stored per row.
- Roles: exactly two — `Administrator` and `Viewer`. The store refuses to remove or disable the last enabled
  administrator.
- The ticket carries the **account id only**; the account is re-read on every request, so disabling somebody takes
  effect immediately.
- One message for every failed sign-in. The reason goes to the audit trail; an unknown account still performs a
  decoy derivation so the timing does not distinguish it from a wrong password.
- Every state-changing form is a POST carrying a CSRF token, and the admin forms re-check the administrator role
  on every post rather than trusting the page that rendered them.
- Sign-out is a POST. The navbar's GET lands on a confirmation card, so a link prefetcher cannot sign you out.

The first administrator is seeded from `SITE_BOOTSTRAP_USER` and `SITE_BOOTSTRAP_PASSWORD`. A generated password
is written once to `App_Data/initial-admin.txt`; the file exists only when an account was created.

Diagnostics live beside the account database, so `App_Data/site-errors.log` is where a page fault is recorded, and
it is rolled at 4 MB.

## Live regions

A live region is a page area that refreshes itself. It is rendered server-side on the page, then replaced with a
fragment fetched from a handler — HTML, not JSON, rendered by the same C# renderer the page used. That is what
keeps the browser from needing a template or a JSON parser, and it is why the policy stays strict.

`<meta name="rms-refresh" content="…">` carries the interval. `site.js` reads it, follows each region's
`data-url`, pauses when the tab is hidden, re-applies a sort it was given, and redirects to the sign-in page on a
`401`.

The one exception is the log handler, which answers JSON because a log body is data rather than a rendering: the
name, the run, the text, the offset, the length, the total, whether the run is complete, and whether the ceiling
was reached.

## Manual refresh and redirects

Pages that change state redirect: a successful save is a `302` to the same page with a status word in the query,
and only a recognised word renders a notice. A forged or missing CSRF token is also a redirect — to a status that
says so — rather than a `200` that looks like a failure the operator might retry.

Cache policy is set through the response's cache object rather than by adding a header, because a header added by
hand is discarded by the pipeline. Every page and every proxy handler marks its response `no-cache, no-store`.

## What the browser never sees

The service key. Every call goes through a same-origin proxy or fragment handler that appends it server-side. A
proxy that echoed the key, or a URL that carried it, would put it in the browser's history and in any extension
the user has installed — and one of those keys can rewrite the deployment's manifests.

The price is that a site's configuration must name its service, its key and the header to present it in. That is
`SiteServiceBaseUrl`, the key file, and `SiteServiceKeyHeader` in the site's `Web.config` — the only three settings
a site needs to reach its service.
