---
title: Data model
order: 40
---

# Data model

Two artifacts and one derivation. The catalogue is what the scanner found; the index is the catalogue made
answerable; the analytics are the index made cheap.

## The catalogue

`catalogue.db` is the scanner's own SQLite database. The scanner is its only writer, and the indexing service
reads it without ever writing to it. One table holds the rows, one holds the bookkeeping:

| Column | Notes |
| --- | --- |
| `Model` | The asset identifier. The row's identity. |
| `Sequence` | The order the catalogue found the row in, assigned when it was first inserted and never reassigned — including by a repair that rewrites everything else about the row. |
| `Date` | When the asset was created, as Roblox reports it. |
| `Name`, `Description` | Empty when the toolbox cannot describe the asset. A sweep leaves them empty; a backfill fills them. |
| `CreatorName`, `CreatorId` | Same. `CreatorId` is zero when unknown. |
| `HasScripts` | The payload scan's marker **or** Roblox's own `hasScripts`. Roblox's flag corroborates; it never contradicts the scan. |
| `Tags` | The canonical detection tags, `" | "` separated. |
| `Score` | The sum of the matched rules' scores. |
| `AssetUrl` | `https://create.roblox.com/store/asset/{id}`, derived, never stored from an upstream value. |
| `Thumbnail` | A validated location, or SQL `NULL`. Never a URL pointing outside Roblox's own hosts. |

`Meta` holds two values: `catalogue.revision`, a counter bumped inside the same transaction that writes the rows,
and `catalogue.updatedUtc`, the instant it last changed. The revision is what the indexer watches — it is the one
number that cannot describe a revision whose rows have not landed.

The database is in write-ahead mode, so a reader sees a complete revision while the scanner is committing the next
one. Writes are batched: a commit per discovered asset would put a disk flush on the path of every inspection, and
the inspection is the part that costs real time. The rows are appended as they are found rather than the whole file
being rewritten, which is the difference between this and the JSON array it replaced — an array has to be rewritten
in full to save any part of it, so a forty-megabyte file was serialised on a timer and every repair rewrote it.

Most rows in a real catalogue have empty `Name`, `Description`, `CreatorName` and `CreatorId`, and a `NULL`
thumbnail — the sweeps that produced them ran when the toolbox would not describe those assets. The backfill pass
exists to repair exactly those rows, one pass at a time. A row with nothing descriptive in it at all is what the
scanner looks for, and the definition of that lives in one place, on the record type both the pass and the
catalogue use.

**Legacy tags.** Rows written before the separator changed carry their tags space-joined. A parser cannot simply
split those, because labels contain spaces. They are parsed by matching the known label set longest-first, with an
alias table for renamed labels — so a legacy row reads correctly without being rewritten.

## The index

`scanner-index.db` is SQLite. `Scanner.Indexer` is its only writer; `Scanner.MicroApi` opens it read-only.

The schema is a contract, not an implementation detail: `Scanner.IndexContract` holds the version, the metadata
keys and the statements, and both processes compile against it. Changing the schema means changing that one
project and rebuilding both — there is no way for the writer and the reader to disagree about a column.

What it holds:

- **`Meta`** — the schema version and the catalogue revision the index was built from.
- **`Models`** — one row per asset: the identifier, its place in the catalogue's discovery order, the metadata, the
  score, the revision date. `Models.Tags` keeps the **raw** tag text as it was written, so a legacy row survives a
  round trip.
- **`ModelTags`** — one row per detection: the asset, the canonical tag, its category, its severity and its
  score. This is what makes "which rules fire, and how often" a grouped query rather than a text scan.
- **The derived statistics tables** — the analytics that a page would otherwise compute: totals, the per-rule
  counts, the category breakdown, the score distribution and the discovery timeline, all computed at index time.

That last point is the whole reason the index exists. Computing the analytics from the raw rows took roughly
750 ms; reading the pre-aggregated tables takes roughly 30 ms. The derivation runs once per rebuild, while the
page runs on every visit.

### Rebuilds

The indexer compares the catalogue's revision with the one recorded in `Meta` and rebuilds only when it differs.
Running it on a schedule is therefore cheap, and running it twice in a row is a no-op. The API reports `isStale`
when the catalogue has changed since the index was built, which is a warning that the indexer has not run — not a
fault.

When the index cannot be read at all, the API answers `503` with one of `index_missing`, `index_incompatible` or
`index_unreadable`, plus `availability` and a sentence. It never repairs the index and never answers `500` for a
state that has a known cause and a known fix.

## Analytics

`GET /api/analytics/summary` returns one object, because one page renders it:

```json
{
  "generatedUtc": "2026-10-17T16:42:49.4383192+00:00",
  "totals": { "models": 120575, "modelsWithScripts": 13237, "modelsWithoutScripts": 107338,
              "modelsWithDetections": 2330, "detections": 3925, "totalScore": 9112.5, "maximumScore": 102 },
  "categories": [ { "category": "Remote execution", "matches": 1524, "distinctRules": 3 } ],
  "scoreDistribution": [ { "label": "No detections", "lowerBound": 0, "upperBound": 0, "count": 118245 } ],
  "discoveryTimeline": [ { "period": "2024-04", "count": 812 } ],
  "topCreators": [ { "creatorId": 1, "creatorName": "...", "models": 12, "modelsWithScripts": 9,
                     "detections": 31 } ]
}
```

The names are the contract: `category`, `matches`, `distinctRules`; `label`, `lowerBound`, `upperBound`, `count`;
`period`, `count`; `creatorId`, `creatorName`, `models`, `modelsWithScripts`, `detections`.

`GET /api/rules` describes the rule set — name, category, severity, score and purpose — and deliberately carries
no match counts. Counts belong to the index and change with every rebuild; the rule set changes when somebody
edits a rule. Mixing the two would make a cached rule reference wrong after every scan.

## The page size

The catalogue endpoint pages at 50 rows. On the real dataset that is 2,412 pages for 120,575 models, and the page
number is part of the query rather than of the URL path so that a filtered view and an unfiltered one share the
same shape.
