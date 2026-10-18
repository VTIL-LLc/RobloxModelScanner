---
title: The scanner
order: 30
---

# The scanner

`src/RobloxModelScanner` walks Roblox's asset identifier space, downloads what it finds, analyses it against the
rule set and records the result in `catalogue.db`. It is Windows-only because of its tray indicator; everything
else in the stack runs elsewhere.

## Two passes

**`SCAN_MODE=sweep`** — the forward walk. It asks the toolbox details endpoint about a block of identifiers at a
time, keeps the ones that are models, inspects each payload, and advances a cursor. The cursor is written back
through the environment file, so an interrupted sweep resumes where it stopped rather than starting again.

**`SCAN_MODE=backfill`** — the repair pass. It revisits catalogue rows that carry no metadata and rebuilds them
from the toolbox. It is a pass and not a loop: it walks the rows once, and a row the toolbox still cannot describe
stays empty and is retried on the next run. Running it twice is harmless.

A backfill does not post webhooks. Thousands of messages would be worse than silence, and the catalogue is the
record. The webhooks are for the sweep, where a finding is news.

## Settings

Read from `KEY=VALUE` in `.env` beside the executable, or in the working directory when one is there. **The
process environment always wins** over the file, and is never written back. The file is not reformatted: unchanged
lines are written back as they were, with the file's own line ending.

| Setting | Default | Meaning |
| --- | --- | --- |
| `SCAN_MODE` | `sweep` | `sweep` or `backfill`. An unrecognised value is treated as `sweep`. |
| `START_ID` | `16848923941` | Where a sweep begins. Advanced as it proceeds and persisted. |
| `BATCH_SIZE` | `99` | Identifiers asked about per request, capped at 250. |
| `SCAN_CONCURRENCY` | `16` | How far the sweep may run ahead of its own inspections. |
| `ASSET_CONCURRENCY` | `64` | Inspections allowed to run at once. |
| `REQUEST_INTERVAL_MS` | `0` | Least time between two catalogue requests. Zero is unpaced. |
| `FLUSH_SECONDS` | `15` | Longest a change may sit unsaved. |
| `FLUSH_ROWS` | `250` | Unsaved rows that force a write early. |
| `KEEP_SCORE` | `3` | Score at or above which the payload is kept on disk. |
| `KEEP_MODELS` | `true` | Whether kept payloads are written at all. |
| `MODEL_DIRECTORY` | the app directory | Where kept payloads go. |
| `CATALOGUE_PATH` | `<app>/Files/catalogue.db` | The catalogue database. |
| `CONTROL_PATH` | `<app>/Files/control.json` | Where pause and stop intent is written. |
| `IGNORED_CREATORS` | two identifiers | Creators whose assets are not downloaded. Setting it replaces the default list. |
| `VERBOSE` | `false` | Per-asset logging. |
| `ROBLOX_COOKIE_FILE` | — | Where the session cookie is published for the services that need it. Written at start-up from `ROBLOX_COOKIE`; unset means nothing is published. |
| `ROBLOX_COOKIE` | — | The `.ROBLOSECURITY` value. Asset delivery refuses unauthenticated requests. |
| `MAIN_WEBHOOK` | — | Where script-carrying models are reported. |
| `AUTODOWNLOAD_WEBHOOK` | — | Where kept payloads are reported. |

`REQUEST_INTERVAL_MS` earns its place: the live endpoint throttles at roughly two block requests per second.
Pacing at 1200 ms measured 18 blocks in 21 seconds with zero refusals, so a slower request rate asked *more*
questions in total by never being turned away.

## What the toolbox answers mean

Three outcomes that look alike from outside, and only one of them is a fault:

| Answer | Meaning | What the sweep does |
| --- | --- | --- |
| `200` with `data[]` | These identifiers exist and are described. | Inspect the models, advance the cursor. |
| `404` | **None of these identifiers exists.** This is ordinary for a dead stretch of the id space. | Treat as an empty list and advance. |
| `5xx`, `429`, `408`, timeout | The endpoint did not answer the question. | Leave the cursor parked and retry the same block after ten seconds. |

The distinction matters more than it looks. Treating `404` as a failure parks the sweep on a stretch where nothing
exists and it never progresses. Treating a throttle as "none exist" advances past identifiers that were never
examined. One is a stall, the other is silent data loss, and neither is acceptable.

## The catalogue

The catalogue is held in memory for the whole run — a list of records and an index by identifier, tens of
megabytes — and written lazily. A flush happens every `FLUSH_SECONDS` or every `FLUSH_ROWS` changed rows,
whichever comes first, and always at shutdown.

A write is streamed, not buffered: the record list is snapshotted under the lock, serialised outside it straight
to a temporary file, and the temporary file is moved over the original. A reader sees either the whole previous
document or the whole new one, and the process never builds a second forty-two megabyte string to do it.

**An unreadable catalogue is refused, not replaced.** The scanner exits with code `2` and changes nothing. Starting
beside a catalogue it had loaded as empty would overwrite the operator's only copy, and that is the one mistake
this program must not make.

## Payloads

A payload is read through a bounded stream and refused above 64 MB rather than truncated. Before parsing, the
first few bytes are examined to tell a JSON refusal from an actual asset: a refusal is detected without handing a
multi-megabyte file to a parser.

A kept payload's thumbnail is validated against the same host rule as a download location before it is stored, so
the catalogue cannot be made to carry a URL pointing somewhere else.

## Detection

`Features.AssetAnalysis` holds the rules. Each condition is an independent class; the registry is projected into
`GET /api/rules`, so the rule reference a reader sees cannot drift from the rules that actually ran.

The precision work that matters, in one place:

- Matching is on **word boundaries**, so a rule named after a function does not fire on a word that contains it.
- The HttpService rule requires `httpservice` **and** an asynchronous call. A script that mentions HttpService and
  never calls it is not evidence of anything.
- The obfuscation rule requires `bit32` **and** a bit operation, rather than either alone.
- The compiler rule does not treat a bare `luau` as a compiler.
- The webhook rule matches Discord webhook URL prefixes, not the word "webhook".

Detections carry a canonical tag and a score. The tags are separated by `" | "`; legacy rows written by older
versions used a single space, so a legacy row is parsed by longest-first substring match against the known label
set, with an alias table for the labels that were renamed.

## Control

The API is the writer of pause and stop intent; the scanner only reads it. `POST /api/scanner/pause`,
`/resume` and `/stop` persist the intent in `control.json`, and the scanner acts on it at its next poll. A pause is
reported once when it takes effect and once when it lifts. A stop is graceful: the first request lets work in
flight finish, a second abandons it.

## Exiting

| Code | Meaning |
| --- | --- |
| `0` | The pass finished, or was stopped cleanly. |
| `2` | The catalogue could not be read. Nothing was written. |
| `3` | An unexpected failure. |

The tray indicator is started only when `Environment.UserInteractive` is true and only inside a try/catch, so a
supervised run — where there is no desktop — is unaffected by a subsystem that cannot start.
