# Changelog

## 1.5.0 – 2026-11-08

### New

- **Draw a route on the map** instead of opening a GPX (*File → New route*, Ctrl+N). Click to add points, drag to move them, click on a segment to insert one, right-click to delete, Ctrl+Z to undo and Esc to finish. *Edit route* (Ctrl+R) changes the route later. A drawn route is saved in the project, or as a normal GPX with *File → Save track as GPX…*.
- **Two new base maps** for regions whose map labels are not in Latin script:
  - *OpenStreetMap – Latin labels*: detailed, with Latin names (next to the local name in bigger places).
  - *National Geographic – English labels*: English only, up to zoom 12, for routes between cities.
- **Track detail slider**: choose how many points the Fusion polyline gets, for faster rendering in Resolve. The preview shows the simplified line.
- **Save project** (Ctrl+S) now saves under the current name, and **Save project as…** (Ctrl+Shift+S) saves a copy. The window title shows the project name.
- The "Export finished" message now reminds you how to bring `GpxTrack.setting` into Fusion.

### Changed

- *File → Save GPX with added points…* is now *File → Save track as GPX…*.

### Fixed

- Durations over 24 hours are shown correctly in the app and in `track_points.csv`. Whole days used to be dropped.

### Known issues

- The *CARTO dark* and *CARTO light* base maps currently show an "API KEY REQUIRED" image instead of a map, because the provider now requires a key. Use another base map.

## 1.0.0 – 2026-11-06

First public release.
