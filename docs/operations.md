---
title: Operations
order: 30
---

# Operations

The day-two runbook. What to start, where the evidence is, and what to check first.

## Before you build on Windows

**Stop the services first.** A running `Scanner.MicroApi` holds its own dependencies open, and IIS Express holds
the sites' DLLs. MSBuild retries a locked copy for ten seconds and then fails, and the error names a file whose
only problem is that it is in use.

```text
rmsdeploy services stop
rmsdeploy build
rmsdeploy publish
rmsdeploy services start
```

`rmsdeploy deploy` does the four in sequence, but it does not stop anything first: on a fresh machine there is
nothing to stop, and on a running one the operator should decide when the stack goes down.

## The plan is the truth

`artifacts/services/services.ini` is what the installer acts on. The deployer produces it from
`deploy/rmsdeploy.services` and hands it over; the installer renders the platform's own service definitions from
it. If a service is not behaving as the manifest says, read the plan before reading the manifest — they should
agree, and finding out that they do not is half the diagnosis.

```text
rmsdeploy services status
```

## Where the evidence is

**Run logs.** One file per run:

```text
<out>/logs/<service>/<runId>.log     the output
<out>/logs/<service>/<runId>.run     the record: status, exit code, times
<out>/logs/<service>/index.ini       the runs, newest first
```

`runId` is `<yyyyMMdd-HHmmssfff>-<sequence>`, so sorting the directory sorts by time. The newest 50 runs are
kept per service; older ones are removed, and the retention count is part of the plan.

A run's status is one of `Running`, `Succeeded`, `Failed`, `Stopped` or `Abandoned`. `Abandoned` means the
supervisor itself went away before the process did — it is the one status that is evidence about the supervisor
rather than about the service.

**The DevOps site** shows the same thing with a UI and fetches the log body on demand. It reads the plan and the
run store directly; there is no database behind it.

## Health

| Surface | Answer |
| --- | --- |
| `Scanner.MicroApi` | `GET /api/health` — `200` with a valid key, `401` without. |
| `RMS.Docs.Service` | `GET /` — `200` with the service name. |
| `RMS.Devops.Service`, `RMS.Configuration.Service` | `404` on `/health` by design; they have no unguarded route. |
| The sites | `200` on the sign-in page, `302` to it when a session is absent. |

An unguarded health route is a route a supervisor could poll without holding a secret — which is why the two
consoles have none. Their liveness is judged by whether the supervisor's process is alive and by the run log.

## The index

The indexer is idempotent. It compares the catalogue's fingerprint with the one recorded in the index and only
rebuilds when it differs, so running it on a schedule is cheap and running it twice in a row is a no-op.

The API answers one of three refusals when it cannot serve, each naming the fault:

| Body | Meaning |
| --- | --- |
| `index_missing` | There is no index file. Run the indexer. |
| `index_incompatible` | The index was written by a different schema version. Rebuild it. |
| `index_unreadable` | The file is there and cannot be read — permissions, or a copy still in progress. |

Each is a `503` carrying `availability` and a sentence. The API never repairs the index and never answers `500`
for one of these, because "the index is not ready" is a known state with a known fix.

## The scanner

Two passes, chosen by `SCAN_MODE`:

- **`sweep`** walks the identifier space forward, starting at `START_ID`. It is resumable: the cursor is written
  back through the environment file, so an interrupted sweep continues where it stopped.
- **`backfill`** revisits rows that carry no metadata and repairs them. It is a pass rather than a loop, it is
  idempotent, and a row the toolbox still cannot describe stays empty and is retried next time.

A backfill does not post webhooks. The flood of thousands of messages would be worse than no notification, and the
catalogue is the record; the report is for the sweep, where a new finding is news.

Control without stopping the process: `POST /api/scanner/pause`, `/resume` and `/stop` persist intent, and the
scanner acts on it the next time it polls. A stop is graceful — one request lets work in flight finish, a second
abandons it.

### When a sweep looks stuck

Three different things look identical from outside, and the log tells them apart:

- **The endpoint is throttling.** `429` from the toolbox. The cursor does not advance and the block is asked for
  again. Set `REQUEST_INTERVAL_MS` (1200 is a good starting point) and the refusals stop.
- **The block does not exist.** `404` is the ordinary answer for a dead stretch of the identifier space. It is
  not a fault, and the sweep advances past it.
- **The endpoint is down or slow.** `5xx`, `408` or a timeout. The block is treated as unanswered, the cursor
  stays parked, and the same block is asked for again after a delay. This is deliberate: a throttle must never be
  able to skip identifiers silently.

## Common faults

| Symptom | First thing to check |
| --- | --- |
| A site renders `502` | Its service is not running, or the key file it presents cannot be read. |
| A download answers `502` | The API has no session cookie. `artifacts/credentials/roblox-cookie` is published by the scanner at start-up, so check the scanner is running and that `ROBLOX_COOKIE` is set in its `.env`. |
| The model page says `stale` | Roblox refused the session. Replace `ROBLOX_COOKIE` in `artifacts/scanner/.env` and restart the scanner and the API: the scanner republishes the file the API reads. |
| The scanner site shows `unresolved` | The scanner's session cookie is missing, not the API being down — the two are distinguished on purpose. |
| `rmsdeploy build` fails on a copy | A service or IIS Express is holding the file. Stop it. |
| `rmsdeploy build` fails on the Web Forms sites with `MSB4226` | The Visual Studio installation it picked has no web build workload. Run `rmsdeploy probe` and read the `web build targets` line. |
| A site answers `404` on every page | Its service surfaces are disabled. Check the service's start-up log for the reason. |
| The DevOps site's `sitetest3` harness fails | That harness is the "service unreachable" test; it expects the DevOps service to be stopped. |

## Backups

The catalogue and the index are derived artifacts: the catalogue can be rebuilt by scanning, and the index can be
rebuilt from the catalogue. What cannot be rebuilt is **`App_Data/site-auth.db` in each of the four sites** — the
accounts, the notices and the audit trail. Back those up.

Losing one means creating the bootstrap account again from `SITE_BOOTSTRAP_USER` and `SITE_BOOTSTRAP_PASSWORD`,
which writes the generated password once to `App_Data/initial-admin.txt`. That file is written only when an
account was created; if it is absent, no account was created and the password you set is the one in force.
