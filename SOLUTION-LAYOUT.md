# Solution layout

One solution, `RobloxModelScanner.sln`, holds all nineteen projects. The API and the sites were once built
by solutions of their own; they are now entries in the same build, which is what makes "build the stack" a
single command and lets the whole thing be resolved in dependency order by the deployer.

See `README.md` for the project list, the build command and the deployer.

## Conventions

Every project follows the same shape, which is what lets a reader open any of them and know where to look.

```text
src/<Project>/
    Enums/            declarations of closed vocabularies
    Exceptions/       types that describe a failure a caller may catch
    Extensions/       extension methods over types the project does not own
    Interfaces/       the contracts a project exposes and consumes
    Implementation/   the concrete types behind those contracts
    Factories/        the one place a configuration is turned into an object graph
    Models/           data carried between layers
    Operations/       long-running work with a beginning and an end
    Entities/         persisted shapes
    Program.cs        the composition root, for a project that has one
```

- One type per file, and the file is named for the type.
- The folder is part of the namespace: `src/Scanner.Indexer/Implementation/` is
  `Scanner.Indexer.Implementation`.
- File-scoped namespaces.
- Every public and protected member carries an XML comment that says why it exists, not what it does.
  Where a decision had an alternative, the alternative is named and the reason it lost is given. The
  comments are the design record; the code is what the design produced.
- `using` directives are grouped and sorted: `System`, then NuGet, then shared, then this project's own,
  shortest first, with a blank line between the groups.
- Inside a type: nested types, then fields, then private and protected properties, then public
  properties, then constructors, then methods.

A project whose classes all sit in a single folder is not evidence of a missing convention; it is evidence
that the project is small enough not to need the split.

## The shared contracts

Three libraries exist so that two projects cannot disagree about something they both depend on. Each is a
real contract rather than a convenience: changing one changes both sides at once, at compile time.

| Library | Shared between | What it fixes |
| --- | --- | --- |
| `src/Scanner.IndexContract` | `Scanner.Indexer`, `Scanner.MicroApi` | The index schema: its version, its metadata keys, its creation statements and its derived-statistics statements. The indexer writes it; the API reads it; neither owns it. |
| `src/RMS.ServiceModel` | `RMS.ServiceHost`, `RMS.ServiceInstaller`, `RMS.Devops.Service` | The plan file, the run records and the log store. The deployer writes the plan, the supervisor executes it, the installer registers from it, and the DevOps service reports on it. |
| `src/RMS.Site.Common` | the four sites | Page chrome, HTML rendering, the same-origin service client, forms authentication, notices and the audit trail. The four sites are one front end with four domains, so they should differ only in what they say. |

`RMS.Site.Common` declares `InternalsVisibleTo` for each of the four sites, which is what lets the sites
share an implementation detail without publishing it as a contract.

## What is deliberately not shared

- **The scanner does not reference the API, the indexer or the sites.** It writes a JSON catalogue and
  stops there. The indexer is the only thing that reads it, and the API never sees the file. A scanner
  that also knew about the index would have two reasons to be changed at once.
- **The sites do not reference each other**, and none of them references a service project. They talk to
  the service over HTTP, through the same client, so a site cannot quietly acquire a compile-time
  dependency on a service's internals.
- **`RMS.Site.Common` does not reference a service project** either. Its client speaks to whatever is
  answering at the configured base address, which is what lets the same code front four different things.
- **The legacy projects reference nothing and are referenced by nothing.** `Roblox.Wcf`,
  `Scanner.WcfServiceHost` and `ServiceInstallers` are kept building so that the old deployment keeps
  working while the new one is proved; they are not part of the new stack.

## Where order is recorded

Project order lives in `deploy/rmsdeploy.build` as one section per project, not in the solution. The
solution's project entries carry no build ordering, and a reader looking for "what has to be built before
what" would not find it there. The manifest is the answer, and it is the same file the deployer reads.

Publish order and the shape of the published output live in `deploy/rmsdeploy.c`, because the output
layout and the service definitions have to agree, and the service definitions come from the manifest.
