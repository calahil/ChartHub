# Song Sources

ChartHub pulls song charts from two sources.

---

## RhythmVerse

[RhythmVerse](https://rhythmverse.co/) is the primary song catalog. It hosts community-submitted charts for Clone Hero and similar rhythm games.

- Requires a RhythmVerse account and API token for download access.
- ChartHub stores your token in its local settings under `rhythmverseToken`.
- Source ID used internally: `rhythmverse`

## Chorus Encore

[Chorus Encore](https://chorus.fightthe.pw/) is a secondary catalog aggregating charts from various community repositories.

- No account required for browsing and downloading.
- Source ID used internally: `encore`

---

## How Source Membership Works

ChartHub tracks which source each downloaded song came from in a local database (`library-catalog.db`). This allows in-library indicators to show correctly in search results across both sources — if you downloaded a song via RhythmVerse, it will be marked as "in library" even when browsing from the Chorus Encore view.

---

## Backup API (Optional)

If you self-host the [Backup API](../self-hosting/backup-api.md), ChartHub can be pointed at your mirror instead of the upstream RhythmVerse endpoint. This is useful when the upstream source becomes unreliable or rate-limited.
