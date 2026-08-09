# Changelog

All notable changes to this package are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com); versioning is semver.

## [0.1.0] - 2026-08-08

Initial public release.

### Added
- Editor window (**Tools → Audio → Library Curator**) for auditioning purchased Foley,
  sound-effect, ambience, and music libraries from any folder on disk, without importing them
  into the project first.
- Machine-local library registry (`EditorPrefs`) so registered library roots carry over between
  Unity projects.
- Per-project, per-user review data (`UserSettings/AudioLibraryCurator.json`): star ratings,
  shortlist, free-text project tags, and decision notes.
- External audio preview (play, stop, loop, scrub) using Unity's editor audio-preview API, with a
  reflection-isolated compatibility bridge for cross-version support.
- Search and filtering across filename, vendor metadata, tags, and notes; quick filters for All,
  Shortlisted, Unreviewed, and Imported.
- Keyboard shortcuts: `↑`/`↓` to change selection, `Space` to play/stop, `S` to toggle shortlist,
  `I` to import.
- Selective import of one file or the full shortlist into a configurable destination beneath
  `Assets`, with SHA-256-based collision handling that never overwrites different audio and reuses
  the existing destination for identical files.
- `_AudioCuratorManifest.json` provenance manifest recording source library, relative source path,
  SHA-256, destination, and import time for every imported file.
- Optional metadata enrichment for libraries that ship 344 Audio's semicolon-delimited
  `Documentation/Metadata.csv` format; libraries without metadata work fine via filename/folder
  scanning alone.
- Editor test suite covering metadata parsing, recursive library scanning, destination/path
  validation, and import collision behavior.
