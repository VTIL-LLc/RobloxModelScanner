---
title: Architecture
order: 10
---

# Architecture

Five moving parts, arranged so that each one has a single reason to be changed. The data flows one way.

## The flow

```
Roblox toolbox and asset delivery
        |
        v
   the scanner            writes the catalogue database
        |
        v
  Files/catalogue.db      SQLite, appended to as assets are found
        |
        v
  Scanner.Indexer         the only writer of the index
        |
        v
  scanner-index.db        SQLite, with analytics pre-aggregated
        |
        v
  Scanner.MicroApi        the only reader of the index
        |
        v
  Scanner.WebForms        the public catalogue
```

The operator side is a second, smaller flow. `RMS.Devops.Service` reads the service plan and the run store and
reports what the deployment is doing; `RMS.Configuration.Service` reads the deployer's manifests; `RMS.Docs.Service`
reads this documentation. Each has a site in front of it, and each site reaches its service from the same origin
through a proxy that holds the key.

## Why the catalogue is a database

The scanner must survive being stopped mid-sweep, and its result must be inspectable. It was a JSON array for both
of those reasons, and an array is bad at the first one: saving any part of it means rewriting all of it, so a
sweep's findings were serialised into a forty-megabyte file on a timer and every repair rewrote the whole document.
The throttle that kept that off the scan path — one flush every fifteen seconds or every two hundred and fifty
changed rows, whichever came first — existed to hide the format, not to do anything for the catalogue.

A database does not rewrite itself to save a row. Rows are appended as they are found, the file stays readable, and
an operator can still open it with any SQLite browser. The batching stayed, for a different and smaller reason: a
commit per discovered asset would put a disk flush on the path of every inspection.

The catalogue also publishes a revision number, bumped inside the transaction that changes a row. That is what
replaced the file's length and last-write time as the signal that the index is behind, and it is a better one — a
database's length moves for reasons that are not content, and a commit can land inside the resolution of the file
system clock and leave the timestamp where it was. One row also records the order the catalogue found it in, which
is what "recently found" sorts on; an asset identifier answers a different question.

## Why the index exists

The catalogue is the wrong shape for serving. A page that asks "how many models fire the HttpService rule, by
category" would otherwise read every row of the catalogue per request. `Scanner.Indexer` reads the catalogue once,
writes a SQLite index, and pre-aggregates the analytics into derived tables at the same time: an analytics page
becomes a handful of queries rather than a full scan, which took roughly 750 ms and now takes roughly 30 ms on the
real dataset.

The index is not a cache. It is a published artifact with a version, and the API refuses to read one it does not
recognise rather than guessing.

## Why the API is read-only

The index has exactly one writer. If the API could write it, two processes could disagree about the schema and
about who is allowed to rebuild it, and the failure would appear as intermittent corruption. So the API opens the
index read-only and answers `503` with a reason when it cannot read it. A rebuild is the indexer's job, and the
indexer is driven by hand or by its supervisor.

## Why there are four sites

They have different audiences and different lifetimes. The public catalogue is anonymous and must be fast. The
three operator sites are authenticated, are used by a handful of people, and change when the deployment changes.
Keeping them separate means a change to the public site cannot take the console down with it, and a console that
needs a new permission does not touch the public surface.

What they share is `RMS.Site.Common`: chrome, HTML rendering, the same-origin service client, forms
authentication, notices and the audit trail. They differ only in what they say.

## The service platform

Every long-running process is described once, in a platform-neutral plan, and supervised by `RMS.ServiceHost`.
On Windows that means a service registered with the SCM; elsewhere it means a plain process under systemd or
launchd. `RMS.ServiceInstaller` is the only thing that knows what a service definition looks like on each
platform, so the deployer never renders one.

That is what makes the stack deployable to a bare machine on three platforms without a second deployment story
for each.

## What is deliberately absent

- **No message bus.** The parts are decoupled by files and by HTTP, and there is no ordering problem that a broker
  would solve.
- **No database server.** SQLite is a file, and the index is a published artifact rather than a live store.
- **No second copy of the catalogue.** The API reads the index; the indexer reads the catalogue; nothing else
  reads either.
- **No write path from any website to Roblox.** The sites display; they do not act.
