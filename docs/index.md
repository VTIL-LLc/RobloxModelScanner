---
title: RMS documentation
order: 0
---

# RMS documentation

This is the documentation for the Roblox Model Scanner stack: a scanner that walks Roblox's asset identifier
space for models, analyses what it finds, and publishes the result as a catalogue, an API and four websites.

It replaces the wiki. Every page here is a file in this repository's `docs` directory, served by
`RMS.Docs.Service` and rendered by `RMS.Docs.Site` — so a page and the file it was written from cannot drift
apart across machines.

## Where to start

- **New to the stack?** Read [Architecture](architecture) first. It describes the five moving parts and what
  flows between them.
- **Deploying it?** [The deployer](guides/deployer) covers the manifests, the verbs and what happens on each
  platform.
- **Running it?** [Operations](operations) is the day-two runbook: what to start, where the logs are, and what
  to check when something is wrong.
- **Changing it?** [Development](guides/development) covers the build, the conventions and how to add a
  project without touching the deployer.

## What this stack is not

It is not a Roblox client, a moderation tool, or a marketplace. It reads public asset metadata and public
asset payloads, records what its rules detect, and shows the result. It never writes to Roblox.

## A note on how these pages are written

They describe what the system does, and where a decision had an alternative, why that alternative lost. A
document that lists settings without saying which ones matter under load is a document that will be
copy-pasted into a machine nobody understands.

## The five moving parts

| Part | What it is |
| --- | --- |
| The scanner | A Windows desktop program that sweeps the identifier space and writes `catalogue.db`. |
| The indexer | A service that turns the catalogue into a SQLite index with its analytics pre-aggregated. |
| The API | A read-only service over that index. |
| The sites | Four Web Forms applications: the public catalogue, and three operator consoles. |
| The deployer | One C program that installs the toolchains, builds everything and registers the services. |
