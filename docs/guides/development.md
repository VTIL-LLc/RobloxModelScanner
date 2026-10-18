---
title: Development
order: 60
---

# Development

## Build

Nineteen projects, one solution, one command. Use the Visual Studio 18 Community MSBuild: the 2022 Build Tools
MSBuild cannot resolve the .NET SDK and fails with `NU1503`.

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" `
    RobloxModelScanner.sln /p:Configuration=Debug `
    "/p:VSToolsPath=C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Microsoft\VisualStudio\v18.0" `
    /restore /v:m /nologo /clp:Summary
```

`/restore` is not optional. The net48 projects use `PackageReference` rather than `packages.config`, so a build
without it fails on a missing assets file.

**Stop the services first.** A running API locks its own dependencies and IIS Express locks the sites' DLLs.
MSBuild retries a locked copy for ten seconds and then fails naming a file whose only problem is that it is in
use.

Two frameworks, deliberately: the services, the API and the scanner are net8; the four sites and the shared site
library are net48, because Web Forms is a .NET Framework technology.

## Conventions

```text
src/<Project>/
    Enums/            closed vocabularies
    Exceptions/       failures a caller may catch
    Extensions/       extension methods over types the project does not own
    Interfaces/       contracts exposed and consumed
    Implementation/   the concrete types behind them
    Factories/        where a configuration becomes an object graph
    Models/           data carried between layers
    Operations/       long-running work with a beginning and an end
    Entities/         persisted shapes
    Program.cs        the composition root
```

- One type per file, named for the type. The folder is part of the namespace.
- File-scoped namespaces.
- Every public and protected member carries an XML comment that says **why it exists**, not what it does. Where a
  decision had an alternative, name the alternative and say why it lost. The comments are the design record.
- `using` directives grouped and sorted: `System`, then NuGet, then shared, then the project's own, shortest first,
  with a blank line between groups.
- Inside a type: nested types, fields, private and protected properties, public properties, constructors, methods.

A project that puts everything in one folder is a project small enough not to need the split; it is not a
convention violation.

## The shared contracts

Three libraries exist so two projects cannot disagree about something they both depend on:

| Library | Between | Fixes |
| --- | --- | --- |
| `Scanner.IndexContract` | the indexer and the API | The schema: version, metadata keys, statements. Changing it changes both sides at compile time. |
| `RMS.ServiceModel` | the deployer's output, the host, the installer, DevOps | The plan, the run records and the log store. |
| `RMS.Site.Common` | the four sites | Chrome, rendering, the client, authentication, notices, audit. |

`RMS.Site.Common` declares `InternalsVisibleTo` for each site, so the sites can share an implementation detail
without publishing it as a contract.

## Adding a project

1. Create the project. net8 for a service; net48 for a Web Forms site.
2. Add it to `RobloxModelScanner.sln`.
3. Add a section to `deploy/rmsdeploy.build` with a `kind` and a `path`: `dotnet-project`, `dotnet-solution`,
   `framework`, `node-package` or `shell`. No C change is needed.
4. If it must be **published**, add a line to `k_publish_targets` in `deploy/rmsdeploy.c`. The published layout
   and the service definitions have to agree, which is why that list is in code rather than in a manifest.
5. If it must **run**, add a section to `deploy/rmsdeploy.services`, regenerate the plan, and install.

## Adding a service

Follow the shape of `RMS.Docs.Service`: options bound from a section with environment overrides that win, a guard
whose every failure path disables the surface, one endpoint filter, one guarded route group, and stateless
singletons.

Two rules are not negotiable:

- **The listener binds loopback by default and a wildcard bind is refused** unless an environment variable says
  otherwise. The check runs before the socket opens, because a port that answers with a refusal is still a port
  that answers.
- **A key shorter than 32 characters disables the surface.** It never becomes an accepted shorter key, and the
  refusal reason never contains the key.

## Adding a site page

Derive from the site's page base, override the title, the active section and — if the page is a standalone card —
`UsesChrome`. Render with the views; do not add server controls, a `<form runat="server">` or view state. A page
that needs a form posts plain HTML and reads `Request.Form`.

Every state-changing post carries a CSRF token and re-checks the role. A missing token is a redirect to a status
code, not a `200`.

If the page needs to refresh, wrap the region in `Live(id, view, content)` and add a fragment handler that renders
the same content through the same renderer. The browser gets HTML, not a model.

## Adding a detection rule

A rule is a class in `Features.AssetAnalysis/Rules/` plus an entry in the registry. The registry is projected into
`GET /api/rules`, so the rule reference cannot drift from the rules that ran.

Match on word boundaries. A rule that fires on a substring is a rule that fires on prose. If a rule needs two
pieces of evidence, require both — the HttpService rule requires the mention *and* an asynchronous call, and the
obfuscation rule requires `bit32` *and* a bit operation, for exactly this reason.

## Testing

The harnesses are PowerShell scripts against live processes: the DevOps site (three, one of which is the
"service unreachable" case and therefore requires the DevOps service to be **stopped**), the Configuration service
and site, the scanner site, and the deployer's manifest validation. They assert on rendered HTML and on HTTP
status codes, which is the only way to be sure a page still works after a shared-library change.

The scanner has its own smoke tests: a sweep against a scratch catalogue with its own `.env` (so the real
`START_ID` is not overwritten), a repair pass, a corrupt-catalogue safety check, and a paced run.

## Pitfalls worth knowing

| Pitfall | Consequence |
| --- | --- |
| net48 projects list `<Compile Include>` explicitly | A new file is silently not compiled until it is listed. |
| A property named `Path` shadows `System.IO.Path` inside its own type | A compile error far from the line that caused it. Spell it out, or name it something else. |
| `Response.Redirect(url, endResponse: true)` throws `ThreadAbortException` | Expected when ending a response; it must not be caught as a failure. |
| `Response.Headers.Add("Cache-Control", …)` is discarded | Set the cache policy through `Response.Cache`. |
| `&` in a URL is escaped exactly once | Build with a raw `&`; the escaping happens in `Html.Encode`. |
| XML comments cannot contain `--` | `MSB4025` or `MSB3249` on the project file. |
| PowerShell's `..` overflows on 11-digit asset identifiers | Build identifier lists with an explicit `[long]` loop. |
| IIS Express registers only `http://localhost:<port>/` | `127.0.0.1` answers `400`. Set environment variables in the process that launches `iisexpress.exe`. |
| A build through `rmsdeploy` resolves its own MSBuild | Check `rmsdeploy probe`'s `web build targets` line before blaming the project. |
