# Audio Library Curator

A Unity Editor workflow for auditioning purchased Foley, sound-effect, ambience, and music
libraries **without importing them into your project first**. Large libraries are often tens or
hundreds of gigabytes of masters you never want inside `Assets` — this tool lets you browse and
play them where they already live on disk, keep per-project review decisions (ratings, tags,
shortlist, notes), and copy only the files you actually approve into the project, with a
provenance manifest recording exactly where each imported file came from.

## Installation

In the Unity Editor: **Window → Package Manager → + → Add package from git URL…**, then enter:

```
<REPO-URL>
```

Requires Unity 6000.0 or newer. The package is editor-only — it has no runtime component and
adds nothing to player builds.

## Quick start

1. In Unity, choose **Tools → Audio → Library Curator**.
2. Click **Add Library Folder…** and select any folder containing your purchased audio library.
   The folder can live anywhere on disk — it does not need to be inside the Unity project.
3. Click a row to preview it. Scrub or loop longer clips, and use search, ratings, tags, notes,
   and the shortlist to narrow your choices.
4. Set an import root beneath `Assets`, then import one sound or the complete shortlist.

Keyboard shortcuts, while a text field is not being edited:

| Key | Action |
|---|---|
| `↑` / `↓` | Previous or next filtered result |
| `Space` | Play or stop |
| `S` | Toggle shortlist |
| `I` | Import the selected sound |

## Where data goes

- Purchased masters remain in their external library folder — the curator never moves or modifies
  them.
- Registered library roots use machine-local Unity `EditorPrefs`, so the same roots appear in
  other Unity projects that install this package on the same machine.
- Ratings, shortlist choices, tags, notes, and the chosen destination are stored per project and
  per user at `UserSettings/AudioLibraryCurator.json` (not source-controlled by default, matching
  Unity's convention for `UserSettings`).
- Imported clips are copied beneath `<import root>/<library name>/`, preserving their
  library-relative folders.
- `_AudioCuratorManifest.json` is written under the import root with the source library, relative
  source path, SHA-256, destination, and import time. Commit it alongside imported assets for
  provenance.

The importer never overwrites different audio. An identical file reuses its existing destination;
a filename collision receives a numeric suffix.

## Metadata and formats

The curator works with **any** folder of audio files — libraries with no metadata at all are
scanned by filename and folder structure alone, and that's enough to browse, preview, rate, and
import them.

When a library ships richer metadata, the curator can use it to enrich search and the details
panel. Today it recognizes one such format out of the box: 344 Audio's semicolon-delimited
`Documentation/Metadata.csv`, which supplies per-file duration, format, channel count, description,
and originator. If a library includes a file matching that convention, its fields show up
automatically; if not, the curator simply falls back to filename/folder scanning.

The scanner recognizes WAV, AIFF, MP3, OGG, and FLAC. External preview uses Unity's decoder;
format availability can vary by Unity version and platform, particularly for FLAC. Import remains
available even when preview decoding is unavailable for a given format.

## Licensing boundary

This tool records provenance; it does not grant or validate a license. Do not register a shared
master-library folder or commit raw selections for teammates unless the purchased seat count and
EULA permit their access. Keep the vendor EULA and invoice with your studio's licensing records.

## Requirements

- Unity 6000.0+
- Editor-only — no runtime dependency, nothing added to player builds
