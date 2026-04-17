# Clone Hero Conversion

When you download a song through ChartHub, the chart and audio files are automatically staged and prepared for Clone Hero.

---

## What Happens

1. ChartHub downloads the chart archive from the source (RhythmVerse or Chorus Encore).
2. The archive is extracted and its contents validated.
3. Files are placed in your configured Clone Hero songs directory.
4. The song becomes available in Clone Hero on next scan.

---

## Supported Formats

ChartHub handles the chart formats produced by RhythmVerse and Chorus Encore, including:

- `.chart` files
- `.mid` files
- Accompanying audio tracks and metadata

---

## Songs Directory

ChartHub writes converted songs to the Clone Hero songs folder configured in your settings. The default path varies by platform:

| Platform | Default Songs Path |
|---|---|
| Windows | `%USERPROFILE%\Clone Hero\Songs` |
| Linux | `~/Clone Hero/Songs` |

You can override this path in **Settings → Library**.

---

## Ingestion Status

Each download goes through a tracked ingestion lifecycle. You can monitor progress in the **Downloads** view on desktop, or from the Android companion app's ingestion status panel.

Possible states include: queued, downloading, extracting, installed, and failed. Failed ingestions can be retried from the same panel.
