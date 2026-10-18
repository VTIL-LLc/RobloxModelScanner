---
title: Data model
order: 40
---

# Data model

Two artifacts and one derivation. The catalogue is what the scanner found; the index is the catalogue made
answerable; the analytics are the index made cheap.

## The catalogue

`models.json` is a JSON array, one record per asset, indented, UTF-8 without a BOM. The property order is fixed
because the scanner rewrites the file it read:

```json
{
  "Model": 17074984378,
  "Date": "2024-04-09T04:18:35.02Z",
  "Name": "Eclipse Masquerade",
  "Description": "...",
  "CreatorName": "Darkworldkris4900",
  "CreatorId": 1234567890,
  "HasScripts": true,
  "Tags": "Obfuscation(Possible) | HttpService functions(Verified)",
  "Score": 7,
  "AssetUrl": "https://create.roblox.com/store/asset/17074984378",
  "Thumbnail": null
}
```

| Field | Notes |
| --- | --- |
| `Model` | The asset identifier. The record's identity. |
| `Date` | When the asset was created, as Roblox reports it. |
| `Name`, `Description` | Empty when the toolbox cannot describe the asset. A sweep leaves them empty; a backfill fills them. |
| `CreatorName`, `CreatorId` | Same. `CreatorId` is zero when unknown. |
| `HasScripts` | The payload scan's marker **or** Roblox's own `hasScripts`. Roblox's flag corroborates; it never contradicts the scan. |
| `Tags` | The canonical detection tags, `" | "` separated. |
| `Score` | The sum of the matched rules' scores. |
| `AssetUrl` | `https://create.roblox.com/store/asset/{id}`, derived, never stored from an upstream value. |
| `Thumbnail` | A validated location, or an explicit `null`. Never a URL pointing outside Roblox's own hosts. |

Most rows in a real catalogue have empty `Name`, `Description`, `CreatorName` and `CreatorId`, and a `null`
thumbnail — the sweeps that produced them ran when the toolbox would not describe those assets. The backfill pass
exists to repair exactly those rows, one pass at a time.

**Legacy tags.** Rows written before the separator changed carry their tags space-joined. A parser cannot simply
split those, because labels contain spaces. They are parsed by matching the known label set longest-first, with an
alias table for renamed labels — so a legacy row reads correctly without being rewritten.

## The index

`scanner-index.db` is SQLite. `Scanner.Indexer` is its only writer; `Scanner.MicroApi` opens it read-only.

The schema is a contract, not an implementation detail: `Scanner.IndexContract` holds the version, the metadata
keys and the statements, and both processes compile against it. Changing the schema means changing that one
project and rebuilding both — there is no way for the writer and the reader to disagree about a column.

What it holds:

- **`Meta`** — the schema version and the fingerprint of the catalogue the index was built from.
- **`Models`** — one row per asset: the identifier, the metadata, the score, the revision date. `Models.Tags`
  keeps the **raw** tag text as it was written, so a legacy row survives a round trip.
- **`ModelTags`** — one row per detection: the asset, the canonical tag, its category, its severity and its
  score. This is what makes "which rules fire, and how often" a grouped query rather than a text scan.
- **The derived statistics tables** — the analytics that a page would otherwise compute: totals, the per-rule
  counts, the category breakdown, the score distribution and the discovery timeline, all computed at index time.

That last point is the whole reason the index exists. Computing the analytics from the raw rows took roughly
750 ms; reading the pre-aggregated tables takes roughly 30 ms. The derivation runs once per rebuild, while the
page runs on every visit.

### Rebuilds

The indexer compares the catalogue's fingerprint with the one in `Meta` and rebuilds only when it differs. Running
it on a schedule is therefore cheap, and running it twice in a row is a no-op. The API reports `isStale` when the
catalogue has changed since the index was built, which is a warning that the indexer has not run — not a fault.

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
