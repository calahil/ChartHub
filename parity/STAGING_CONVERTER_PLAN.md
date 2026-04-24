# Staging-Ready Converter Plan Ledger

Last updated: 2026-04-24
Owner: Conversion library workstream

---

## MANDATORY ANCHOR — READ BEFORE TOUCHING ANY CONVERSION CODE

The authoritative reference for ALL conversion behavior is the Onyx source code at:

  `onyx-source/onyx/haskell/packages/onyx-lib/src/Onyx/Build/CloneHero.hs`
  function: `psRules`

No conversion behavior may be implemented, changed, or guessed without first tracing
it back to a specific line or function in that file. "It seemed right" is not acceptable.

The goal is ONE thing only: produce a Clone Hero / Phase Shift folder from an RB3CON
(encrypted 0x0B, unencrypted 0x0A, or 0x0D) or an unencrypted SNG input, with output
IDENTICAL to what Onyx produces from the same input via "Create CH/PS song folder".

Everything in ChartHub.Conversion that does not serve that goal must be removed.
The parity harness infrastructure, milestone ledger ceremony, and oracle comparison
machinery are SECONDARY — they are only useful if the conversion itself is correct.
They are currently masking the fact that the conversion is broken.

---

## WHAT ONYX ACTUALLY PRODUCES (per CloneHero.hs psRules)

For a PS folder build, Onyx writes these files to `<target>/ps/`:

### Always present
- `notes.mid` — RB3 MIDI processed through `RB3.processPS` (track filtering + CH mapping)
- `song.ini` — generated from DTA metadata + difficulty tiers + preview bounds + song length
- `album.png` or `album.jpg` — cover art; PNG from `gen/cover-full.png` (decoded Xbox texture),
  or JPEG if the original source is already JPEG

### Audio — always present
- `song.ogg` — the BACKING track: all MOGG channels NOT assigned to any instrument stem,
  mixed to stereo using per-channel pan/vol from DTA, rendered to OGG.
  Built by `sourceBacking`. Audio format controlled by `ps.audioFormat` (default: `"ogg"`).

### Audio — present only when the instrument part exists AND the mixed result is non-silent
Each of these is built from the MOGG channels assigned to that instrument in the DTA track map,
mixed to stereo using DTA pan/vol, rendered to OGG. Onyx uses a "try-" phony gate that checks
if the rendered audio is non-zero length before copying it to the output folder.

- `guitar.ogg`  — guitar + guitarCoop MOGG channels
- `rhythm.ogg`  — bass + rhythm MOGG channels
- `keys.ogg`    — keys MOGG channels
- `vocals.ogg`  — vocals MOGG channels
- `crowd.ogg`   — crowd MOGG channels
- `drums.ogg`   — drums channels (only when DrumMixMode == D0, i.e. single drum stem)
- `drums_1.ogg` — kick channels  (when mixMode != D0)
- `drums_2.ogg` — snare channels (when mixMode != D0; kit when D4)
- `drums_3.ogg` — cymbals channels (when mixMode != D0 and not GH-style drums)
- `drums_4.ogg` — toms channels (only when ghDrumsAudio == true)

### song.ini required fields (direct from psRules makeSongIni)
These fields MUST be present — Onyx always emits them:
  name, artist, album, charter, year, genre, song_length, preview_start_time, preview_end_time,
  diff_band, diff_guitar, diff_guitar_ghl, diff_bass, diff_bass_ghl, diff_drums, diff_drums_real,
  diff_keys, diff_keys_real, diff_vocals, diff_vocals_harm, diff_dance, diff_bass_real,
  diff_guitar_real, diff_guitar_coop, diff_rhythm, diff_drums_real_ps, diff_keys_real_ps,
  diff_guitar_pad, diff_bass_pad, diff_drums_pad, diff_vocals_pad, diff_keys_pad,
  pro_drums, five_lane_drums, drum_fallback_blue, star_power_note (= 116),
  sysex_slider, sysex_open_bass, loading_phrase, tags (= "cover" if cover song)

---

## KNOWN BROKEN STATE (as of 2026-04-24)

The ChartHub.Conversion library is not producing correct output. Confirmed by direct
comparison of `~/.local/share/Steam/.../clonehero/Modest Mouse/Broke/Kamotch__rhythmverse`
against `onyx-conversion` sibling folder for the same song:

| File | ChartHub output | Onyx output |
|---|---|---|
| album.png | MISSING | present |
| song.ogg | present but STATIC (raw multi-channel dump) | present, stereo, correct |
| guitar.ogg / rhythm.ogg / etc. | absent or wrong | absent (single-stem song — correct) |
| song.ini | missing most fields | full field set |
| notes.mid | present | present |

Root causes (traced, not guessed):
1. Audio: raw MOGG bytes are dumped as-is after MOGG header strip. Multi-channel OGG
   is not stereo — Clone Hero cannot play it / plays as static. Must decode + stereo-mix
   per channel using DTA pan/vol before writing song.ogg.
2. Album art: extractor only searches a song-scoped subfolder path inside the STFS for
   `.png_xbox`. The actual art in many RB3CON packages is at the package root or
   under a different path. Needs a two-pass search: scoped first, then package-wide.
3. song.ini: many fields from psRules `makeSongIni` are absent. DTA tier/rank values
   are not being extracted or mapped to the `diff_*` key schema correctly.

---

## ACTIONABLE EXECUTION PLAN (Onyx 1:1 Port)

This is the only active implementation plan.

### Rule 0
- Every conversion behavior must reference a specific Onyx function/path before implementation.
- If no Onyx source anchor is identified, do not implement the behavior.
- Hard requirement: conversion runtime must be pure C# in-process with zero external decoder/mixer processes.
- Forbidden at runtime: ffmpeg, ffprobe, onyx CLI, shell-outs, or any external audio forks.
- If audio decode/remix parity is not achievable in-process, stop and document the blocker; do not add process dependencies.

### Phase 1 — Lock stage/progress contract for DownloadsView

Goal:
- Surface conversion internals in DownloadsView with clear stage and percent progression.

Implementation:
- Keep existing top-level pipeline stage flow:
   - ResolvingSource → Downloading → Downloaded → InstallQueued → Staging → Installing → Installed
- Introduce conversion sub-stages as concrete Stage strings persisted in `download_jobs.stage`
   and streamed over SSE (`/api/v1/downloads/jobs/stream`), using names that map to UI text:
   - Converting:ParseContainer
   - Converting:ParseDta
   - Converting:ConvertMidi
   - Converting:DecodeMogg
   - Converting:MixBacking
   - Converting:MixStems
   - Converting:ExtractAlbumArt
   - Converting:WriteSongIni
   - Converting:Finalize
- Percent allocation (deterministic):
   - Download complete: 80
   - InstallQueued: 88
   - Staging: 90
   - Converting:* spans 91–96 (fixed per-substage increments)
   - Installing: 97
   - Installed: 100
- Update client stage mapping so unknown stage strings do not collapse to `Queued`:
   - `MapServerStage` must treat `Converting:*` as `IngestionState.Converting`.
   - Keep terminal states visible (already done).

Acceptance:
- DownloadsView shows monotonic stage transitions and non-jumping progress for each conversion.
- SSE stream payload reflects every conversion sub-stage transition.

### Phase 2 — Wire conversion progress callbacks end-to-end

Goal:
- Emit stage/progress from conversion internals without violating MVVM boundaries.

Implementation:
- Add a conversion progress callback contract in `ChartHub.Conversion`:
   - Example shape: `(stage, percent, message)` with deterministic stage IDs.
- Pass callback from `DownloadJobInstallService` into `IConversionService.ConvertAsync`.
- On callback:
   - `IDownloadJobStore.UpdateProgress(jobId, stage, percent)`
   - emit structured logs (ILogger)
   - emit job log entries (`IJobLogSink`) with stage and percent context.

Acceptance:
- All conversion sub-stages persist to `download_jobs` and appear in `/jobs/{id}/logs`.

### Phase 3 — Rebuild audio path to match Onyx sourceBacking/sourceStereoParts

Goal:
- Replace raw OGG passthrough with Onyx-equivalent decode/remix logic.

Implementation:
- Restore decoded audio path in `MoggExtractor`:
   - decrypt 0x0A/0x0B/0x0D as currently supported.
   - decode Vorbis channels.
   - apply Onyx pan/vol math equivalent to `applyPansVols`.
   - build `song.ogg` from backing channels (all channels not assigned to used parts/crowd).
- Build optional stems with Onyx channel selection rules:
   - guitar/rhythm/keys/vocals/crowd and drum split variants (`drums`, `drums_1..4`) only when present and non-silent.
- Do not use external runtime tools for decode/mix.

Acceptance:
- `song.ogg` is audible and non-static in Clone Hero for Broke fixture.
- Stem presence/absence matches Onyx output for the same input.

### Phase 4 — Complete song.ini parity with Onyx makeSongIni

Goal:
- Emit the same functional key set as Onyx CH/PS `song.ini`.

Implementation:
- Expand DTA extraction for all keys used by Onyx `makeSongIni`.
- Expand generator to include missing fields (for example):
   - `diff_guitar_ghl`, `diff_bass_ghl`, `diff_drums_real_ps`, `diff_keys_real_ps`
   - `diff_*_pad` placeholders
   - `pro_drums`, `five_lane_drums`, `drum_fallback_blue`
   - `star_power_note`, `sysex_slider`, `sysex_open_bass`
- Keep existing preview fix; ensure start/end always map when present in DTA.

Acceptance:
- Broke `song.ini` contains all required Onyx key families with correct values or Onyx-equivalent defaults.

### Phase 5 — Album art parity hardening

Goal:
- Ensure `album.png`/`album.jpg` output parity with Onyx for RB3CON and unencrypted SNG.

Implementation:
- Keep two-pass RB3CON search (song-scoped then package-wide).
- Prefer decoded `.png_xbox` where applicable; preserve JPEG when source is JPEG-equivalent.
- Emit conversion stage/log entries for selected source path and decode result.

Acceptance:
- Broke fixture always installs with album art file present in final folder.

### Phase 6 — Observability sink requirements (mandatory)

For each conversion sub-stage transition and major output decision, emit to both sinks:
- Structured server logs (`ILogger` with stable event IDs).
- Job log sink (`IJobLogSink`) for per-job timeline retrieval in DownloadsView.

Required payload fields:
- `jobId`
- `stage`
- `progressPercent`
- `sourcePath` (when safe)
- `outputPath` (when safe)
- `decision` (for example `stem-silent-skip`, `album-source-selected`, `song-ini-field-defaulted`)

Failure reporting requirement:
- Do not emit generic failure text only.
- Include exact stage and parser/mixer/decode context.

### Phase 7 — Verification gate for this plan

Must pass before marking complete:
- Real fixture install diff against Onyx folder for Broke:
   - required artifacts present (`notes.mid`, `song.ini`, `song.ogg`, album art)
   - no static-audio regression
   - song.ini key family parity checked
- DownloadsView displays conversion sub-stages and monotonic percent during live install.
- `/jobs/{id}/logs` shows full conversion timeline with stage/decision entries.

Completion definition:
- Only declare success when live install output and user-visible stage flow match the Onyx-backed expectations above.

---

Legacy M1-M7 milestone sections were intentionally removed on 2026-04-24 because
they created false confidence and repeatedly derailed implementation work away from
the Onyx source path.

This document now serves only as:
- An anchor to exact Onyx source behavior.
- A statement of the currently broken converter state.
- A strict instruction to implement 1:1 CH/PS folder output from source-defined behavior.
