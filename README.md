# Roblox Model Scanner — the stack

A scanner walks Roblox's asset identifier space for models, analyses each payload against a rule set,
records what it finds in a JSON catalogue, and serves that catalogue as a read-only SQLite index behind
an API, a public website, and three operator sites.

Nineteen projects, one solution, one build command, one deployer.

## Projects

### The scan itself

| Project | Framework | Role |
| --- | --- | --- |
| `src/RobloxModelScanner` | net8.0-windows | The scanner. Sweeps the identifier space or repairs catalogue rows, downloads each candidate, analyses it, and writes the catalogue. Windows-only because of its tray indicator. |
| `src/Features.AssetAnalysis` | net8.0 | The detection engine. Each condition is an independent rule class under `Rules/`; the rule registry is projected into `GET /api/rules` so its documentation cannot drift from its behaviour. |
| `src/Features.AssetDelivery` | net8.0 | Resolves an asset identifier to a signed, time-limited content location through Roblox's delivery API. Shared by the scanner and the API. |
| `src/Features.Tray` | net8.0-windows | The scanner's tray presence indicator. |

### The index and the API

| Project | Framework | Role |
| --- | --- | --- |
| `src/Scanner.IndexContract` | net8.0 | The index schema as one shared source: its version, its metadata keys, and the derived-statistics statements. Referenced by both the indexer and the API, so the two cannot disagree about the contract. |
| `src/Scanner.Indexer` | net8.0 | The **sole writer** of the SQLite index. Reads the JSON catalogue and pre-aggregates the analytics into derived tables, which is what makes an analytics page a query rather than a scan. |
| `src/Scanner.MicroApi` | net8.0 | A **read-only** consumer of the index. Serves the catalogue, analytics, the rule reference, download resolution, and the scanner's pause/stop intent. |

### The operator sites

Each site is an ASP.NET Web Forms front end for the service beside it. Sites never hold the service key
in the browser: every call goes through a same-origin proxy or fragment handler that appends it server-side.

| Site | Service | Role |
| --- | --- | --- |
| `src/RMS.Devops.Site` | `src/RMS.Devops.Service` | What this deployment is doing: services, runs, log retrieval, published artifacts, catalogue scan statistics. |
| `src/RMS.Configuration.Site` | `src/RMS.Configuration.Service` | A console over the deployer manifests: `rmsdeploy.requirements`, `.build`, `.services`, and the generated `services.ini`. |
| `src/RMS.Docs.Site` | `src/RMS.Docs.Service` | Serves `docs/` from this repository. Replaces the old wiki. |
| `src/Scanner.WebForms` | — | The public catalogue: models, analytics, the rule reference, and the scored-out assets. No control surface. |

### The service platform

| Project | Framework | Role |
| --- | --- | --- |
| `src/RMS.ServiceModel` | net8.0 | The plan file, the run records, and the log store: the vocabulary the deployer, the supervisor and the sites all share. |
| `src/RMS.ServiceHost` | net8.0 | One supervisor for every service: a Windows service on Windows, a plain supervised process elsewhere. Owns run logging, restart policy and graceful shutdown. |
| `src/RMS.ServiceInstaller` | net8.0 | Turns a platform-neutral plan into real service definitions — SCM, systemd, launchd, or IIS site — and installs them. |
| `src/RMS.Site.Common` | net48 | Everything the four sites share: page chrome, HTML rendering, the service client, forms authentication against a per-site SQLite store, notices and the audit trail. |

### Legacy, still building

| Project | Framework | Role |
| --- | --- | --- |
| `src/Roblox.Wcf` | net48 | The original WCF contracts. Kept compiling; nothing new depends on them. |
| `src/Scanner.WcfServiceHost` | net48 | The original WCF host, superseded by `RMS.ServiceHost`. |
| `src/ServiceInstallers` | net8.0-windows | The original Windows service installer, superseded by `RMS.ServiceInstaller`. |

## Build

The whole solution builds in one invocation. Use the Visual Studio 18 Community MSBuild: the 2022
Build Tools MSBuild cannot resolve the .NET SDK and fails with `NU1503`.

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" `
    RobloxModelScanner.sln /p:Configuration=Debug `
    "/p:VSToolsPath=C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Microsoft\VisualStudio\v18.0" `
    /restore /v:m /nologo /clp:Summary
```

`/restore` is not optional. The net48 projects use `PackageReference` rather than `packages.config`, so a
build without it fails on a missing assets file.

Stop every running service first. A running `Scanner.MicroApi` locks its own DLL, and IIS Express locks
the net48 site DLLs, so a build fails with a file-copy error while they are up.

Two frameworks, deliberately: the services, the API and the scanner are net8.0; the four Web Forms sites
and `RMS.Site.Common` are net48, because Web Forms is a .NET Framework technology.

## Deploy

`deploy/rmsdeploy.c` is a single translation unit with no third-party dependency. It reads three
INI-style manifests, which are the operational source of truth:

- `deploy/rmsdeploy.requirements` — the toolchains to find or install (the .NET SDK, Node, MSBuild, Mono,
  archive and package payloads).
- `deploy/rmsdeploy.build` — the projects to build, in dependency order.
- `deploy/rmsdeploy.services` — the services to register, and their environment.

```text
rmsdeploy probe                       report which requirements are satisfied
rmsdeploy bootstrap                   install what is missing
rmsdeploy build [step-id]             build every step, or one of them
rmsdeploy publish                     build, then collect publish output under --out
rmsdeploy services <verb>             install | uninstall | start | stop | restart | status
rmsdeploy deploy                      bootstrap, build, publish, install
```

Global options: `--root`, `--tools`, `--out`, `--dry-run`, `--verbose`, `--keep-cache`,
`--continue-on-error`.

The deployer never invents a secret. Before or after a deploy, `configure.bat` in the repository root asks
for the five credentials and the scanner's settings and writes them where the deployment reads them; see
Credentials below.

The deployer does not render service definitions itself. It expands a manifest into one platform-neutral
plan file, `artifacts/services/services.ini`, and hands that to `RMS.ServiceInstaller`, which knows what a
service definition looks like on each platform. Adding a project to the build needs no C change: a new
manifest section is enough. Adding a *publish* target does need one, because the published output layout
is held in code so that the service manifest and the publish list can be reviewed together.

Platforms: Windows uses IIS Express with MSVC's MSBuild and the SCM; Linux uses Mono's MSBuild with
`xsp4` under systemd; macOS uses Mono from Homebrew with `xsp4` under launchd.

## Scanner configuration

The scanner reads `KEY=VALUE` from `.env` beside the executable, or from the working directory when one
is there, and the **process environment always wins** over the file and is never written back. The file
is not reformatted: unchanged lines are written back as they were, in place, with the file's own line
ending. The only value the scanner writes is the sweep cursor.

| Setting | Default | Meaning |
| --- | --- | --- |
| `SCAN_MODE` | `sweep` | `sweep` walks forward for new models; `backfill` revisits the rows that carry no metadata and rebuilds them. |
| `START_ID` | `16848923941` | Where a sweep begins. Advanced as the sweep proceeds and persisted, so an interrupted sweep resumes. |
| `BATCH_SIZE` | `99` | Identifiers asked about per catalogue request. |
| `SCAN_CONCURRENCY` | `16` | Candidates the sweep may run ahead of its own inspections. |
| `ASSET_CONCURRENCY` | `64` | Inspections allowed to run at once. |
| `REQUEST_INTERVAL_MS` | `0` | Least time between two catalogue requests. `0` is unpaced; the live endpoint throttles at roughly two requests a second, so a nonzero value asks fewer questions in total by never being refused. |
| `FLUSH_SECONDS` | `15` | Longest a change may sit unsaved. |
| `FLUSH_ROWS` | `250` | Unsaved rows that force a write early. |
| `KEEP_SCORE` | `3` | Score at or above which the payload is kept beside the scanner and reported. |
| `KEEP_MODELS` | `true` | Whether kept payloads are written to disk. |
| `MODEL_DIRECTORY` | app directory | Where kept payloads go. |
| `CATALOGUE_PATH` | `<app>/Files/catalogue.db` | The catalogue. |
| `CONTROL_PATH` | `<app>/Files/control.json` | Where the API leaves pause/stop intent. |
| `IGNORED_CREATORS` | `5845922908,1395153130` | Creators whose assets are not downloaded. |
| `VERBOSE` | `false` | Per-asset logging. |
| `ROBLOX_COOKIE_FILE` | — | Where the session cookie is published for other services. The scanner writes the value it read from `ROBLOX_COOKIE` here at start-up and the API reads the same file, so the two cannot drift. Unset means nothing is published. |
| `ROBLOX_COOKIE` | — | The `.ROBLOSECURITY` value. Asset delivery refuses unauthenticated requests, so without it nothing downloads. |
| `MAIN_WEBHOOK` | — | Where script-carrying models are reported. |
| `AUTODOWNLOAD_WEBHOOK` | — | Where kept payloads are reported. |

Notable behaviour:

- **The catalogue is written lazily and atomically.** Rows change in memory and the document is rewritten
  on a timer or after `FLUSH_ROWS` changes, to a sibling file that is then moved into place. A reader
  sees either the whole previous document or the whole new one.
- **An unreadable catalogue is refused, not replaced.** The scanner exits with code 2 and changes nothing,
  because starting beside a catalogue it had loaded as empty would overwrite the operator's only copy.
- **A block of identifiers that does not exist is a 404, not a failure.** A sweep advances past it. A
  block the catalogue refuses to answer at all is asked for again with the cursor left where it is, so a
  throttle cannot silently skip identifiers.
- **A stop is graceful.** One interrupt stops the pass starting more work and lets what is in flight
  finish; a second abandons it.
- **The catalogue's own `hasScripts` is corroboration, not a substitute.** An asset is recorded as
  carrying scripts when the payload scan finds a marker *or* Roblox declares scripts.

## API

Every endpoint sits behind the key gate, so a deployment started without a usable key rejects all traffic
rather than serving the catalogue to anyone who can reach the port. The key arrives as the `apiKey` query
parameter and must be a parseable GUID.

```text
GET  /api/health                  liveness
GET  /api/rules                   the rule reference: score, category, severity, purpose
GET  /api/analytics/summary       totals, per-rule breakdown, categories, score histogram, timeline, top creators
GET  /api/models                  catalogue search: q or search, hasScripts, hasObfuscation, ruleTag, page, pageSize, sortBy, sortDirection
GET  /api/models/{id}/download    resolve a signed location; redirects by default, redirect=false returns JSON, stream=true returns the bytes named after the asset
GET  /api/scanner/status          persisted pause/stop intent, and whether the catalogue and index exist
POST /api/scanner/pause           request that the scanner wait before its next block
POST /api/scanner/resume          clear a pending pause
POST /api/scanner/stop            request that the scanner stop once in-flight work completes
```

`/api/models/{id}/download` only resolves assets that are already in the catalogue, which is what stops it
being usable as a proxy for arbitrary Roblox identifiers. An asset that is not indexed answers 404
`not_indexed`; a resolver failure answers 502 `unavailable`. An index that cannot be read answers 503 with
`index_missing`, `index_incompatible` or `index_unreadable`, never a 500 and never an attempt to repair it.

With `stream=true` the asset's bytes are sent back rather than its address, under a `Content-Disposition`
name built from the asset identifier and the kind of content Roblox said it was. That form exists because a
browser following a signed address saves the file under the last segment of that address, which is a hash
Roblox generated: the right bytes under a name that identifies nothing. The site's download link uses it, so
a model saves as `8515755685.rbxm` and not as `385fb7a1a4594fe610e6ac1fb4eb0fbf`.

The control endpoints persist intent; the scanner performs the action, so a pause or stop takes effect when
the scanner next reaches a poll.

## Credentials

Nothing in this repository is meant to hold a credential. Secrets arrive through the environment:

- `SCANNER_API_KEY` — the API key (a GUID). Also `Scanner:ApiKey` in the API's `appsettings.json`.
- `ROBLOX_COOKIE` — the Roblox session cookie, for the API and the scanner. The scanner writes its value to `ROBLOX_COOKIE_FILE` at start-up, and the API reads that file, so a session replaced in one place does not leave the other holding a dead copy.
- `SCANNER_TRUSTED_PROXIES` — the proxies whose forwarded headers are believed. Defaults to Cloudflare's
  published ranges; the single word `none` trusts nobody.
- `SITE_SERVICE_KEY_FILE` / `SITE_SERVICE_KEY` — the key a site presents to its service. The file wins.
- `SITE_BOOTSTRAP_USER` / `SITE_BOOTSTRAP_PASSWORD` — the first administrator a site creates.
- `SCANNER_INDEXER_TOKEN_FILE`, `RMS_DEVOPS_TOKEN_FILE`, `RMS_CONFIGURATION_TOKEN_FILE`,
  `RMS_DOCS_TOKEN_FILE` — the per-service tokens, each generated at deploy time.

The internal services bind loopback by default and refuse a wildcard bind unless explicitly allowed. Each
carries its own key in its own header — `X-Indexer-Token`, `X-Devops-Key`, `X-Configuration-Key` —
compared in fixed time, and a token that is absent or shorter than 32 characters disables the surface
rather than leaving it open.

A deployment writes the *shape* of these and never the values. `rmsdeploy services install` writes the
path of each credential file into the published `Web.config`, and `artifacts/credentials/` is created with
a `README.txt` naming which files are still missing. Filling them in is the operator's half of that split.

`configure.bat` in the repository root is that half, as a form. Run it, answer the fields, and it writes:

- `artifacts/credentials/{api-key,indexer.token,devops.token,configuration.token,docs.token}` — one value
  per file, no trailing newline and no byte order mark. `g` generates a value from the cryptographic
  generator; a value typed by hand is written exactly as typed. A credential left empty is not created at
  all, because a file that exists and holds nothing reads the same to every surface as no file.
- `artifacts/scanner/.env` — the scanner's environment file, filled in from the template it was published
  with. The comments, the setting order and the file's own line ending are kept; only the values of the
  settings that file already declares are replaced.
- `.env` at the repository root, when asked for, which is what a scanner started by hand reads.

Pressing Enter keeps whatever a field already holds, so a run that answers nothing writes nothing. No value
is ever printed — not even the one just written, for which only its length is reported — and `clear` empties
a setting rather than removing it. The writing is done by `configure.ps1`, because a session cookie contains
a pipe character and cmd cannot carry one through an echo or an expansion; every value is passed in the
environment instead. The four sites' `Web.config` is deliberately not touched: it is a publish artifact,
and the one value in it that matters is written by the deployer at install time.

## Known state

Built and verified live: the scanner's sweep and repair passes, `Scanner.Indexer`, `Scanner.MicroApi`,
`RMS.ServiceHost`, `RMS.ServiceInstaller`, `RMS.Devops.Service`/`.Site`,
`RMS.Configuration.Service`/`.Site`, `RMS.Docs.Service`/`.Site`, `Scanner.WebForms`, the deployer's build,
publish, credentials and site-key passes, `configure.bat`, all ten services running under the SCM with the
four sites served by IIS Express as children of the host, and the download path end to end — the scanner
publishing its session to the credential file, the API resolving the signed address with it and streaming the
bytes, and the site serving a model byte for byte under `<asset identifier>.rbxm`.

Not verifiable on the machine this was written on: the installer's IIS half, because it needs the machine-wide
role and the role's payload cannot land while Windows has a servicing reboot pending; and the POSIX branches of
the deployer, the service installer and the site publish, because there is no WSL, no Docker and no elevation
here.
