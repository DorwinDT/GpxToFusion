# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

GpxToFusion is a .NET 10 WinForms app. It turns a GPX track into material for a DaVinci Resolve animation:
- a map background (`map.png`, built from web tiles);
- the track as a **Fusion polyline** inside a `.setting` file. The user pastes it into the Fusion page (Ctrl+V) and animates it there (Write On, Transform).

The app does **not** render video. The user crops the track start/end in the app before exporting. UI text and README are in English.

## Commands

```
dotnet build                                   # from GpxToFusion/
dotnet test
dotnet test --filter "FullyQualifiedName~FusionSetting"   # single test / group
dotnet run --project src/GpxToFusion -- "D:\!!!\Afternoon_Walk.gpx"
```

- Tests read the sample `Afternoon_Walk.gpx` one directory **above** the solution (`D:\!!!\`). They resolve it from the test bin dir via `../../../../../../`, so moving the solution breaks them.
- `EndToEndTests` downloads real map tiles. It is a no-op unless the env var `GPX2FUSION_E2E_OUT` points to an output folder.
- Close a running `GpxToFusion.exe` before building, otherwise the exe stays locked.

## User documentation (keep it current)

- `docs/index.html` is the user guide: a single self-contained page with inline CSS, intended for GitHub Pages from `/docs`. Screenshots live in `docs/images/`.
- **Whenever a change affects what the user sees or does**, update `docs/index.html` in the same change. That covers UI texts, menus, shortcuts, settings (the "Settings reference" tables mirror `ExportSettings` / `MapLabel`), export files, the Fusion node graph or the Resolve workflow. Keep `README.md` consistent too.
- If the UI changes visibly, retake `docs/images/app-main.png`. Launch the built exe with `Afternoon_Walk.gpx` at a 1400×900 window, capture it with `PrintWindow` and use the default settings.

## Releases

- The version lives only in `<Version>` in `src/GpxToFusion/GpxToFusion.csproj`. Help → About reads it from the assembly.
- Pushing a tag `vX.Y.Z` that matches the csproj version runs `.github/workflows/release.yml`. It publishes a self-contained single-file win-x64 exe, zips it with LICENSE and README and creates the GitHub Release. The workflow fails if the tag and the version differ, or if `CHANGELOG.md` has no `## X.Y.Z` section for that version (the section becomes the release notes).
- CI does not run tests, because the sample GPX is outside the repository.

## Build gotchas

- The TFM must be `net10.0-windows10.0.19041.0` in both projects. With plain `net10.0-windows`, `SkiaSharp.Views.WindowsForms` 4.x falls back to its .NET Framework assets (NU1701).
- SkiaSharp 4.x marks `SKPath.MoveTo/LineTo/AddRect` obsolete. Use `SKPathBuilder` and finish with `.Detach()`.

## Architecture

The pipeline is `Gpx` → `Map` → `Export`, with `UI` on top.

- **Coordinate source of truth: `Map/MapFrame`.** It holds center (world px), fractional zoom and output size, and `ToPixel(lat, lon)`. The same frame drives three things, so they stay aligned:
  - the map image (`MapComposer`);
  - the preview (`UI/MapPreview`, which builds a scaled MapFrame for the control size);
  - the Fusion polyline (`Export/TrackGeometry`).

  `MapFrame.Fit` computes the export frame from the **cropped** track's bounding box, padding and `ExtraZoomOut` — unless `ExportSettings.ManualFrame` (center lat/lon + zoom) is set. The user sets it by panning (left/middle drag) and wheel-zooming in `MapPreview` (`FrameChanged` event); "Auto výrez" clears it, and so do opening a new GPX and changing padding / ExtraZoomOut.
- **Fractional zoom:** tiles come from `ceil(zoom)` and are downscaled (`MapFrame.TileZoom` / `Tiles`). The preview passes an `outputScale` so it fetches lower-zoom tiles.
- **`Map/TileCache`:** disk cache in `%LOCALAPPDATA%\GpxToFusion\tiles`, an in-memory LRU, and at most 2 concurrent downloads with a User-Agent header (needed for the OSM tile policy).
  - `TryGet` is for the preview: it never blocks and raises `TileLoaded` when a tile arrives.
  - `GetAsync` / `PrefetchAsync` are for export, which prefetches every tile before composing.
- **`Export/TrackGeometry`:** projects the cropped track to pixels, applies the GPS-noise filter, then runs Douglas–Peucker (`PolylineSimplifier`, which returns kept indices). `LengthFractionAtTime` maps GPX time to a WriteLength value for the optional time-based keyframes.
- **`Export/FusionSettingWriter`:** writes the Fusion Lua-table text with a `StringBuilder`. Always format numbers with InvariantCulture; a test runs under the `sk-SK` culture to catch decimal commas.
  - Node graph: map source → `MergeTrack` BG; `TrackColor` (Background masked by the `TrackPath` PolylineMask) → FG; optional `MergeCalib`.
  - The map source is either a `MapLoader` (Loader with hold frames) or an external `MediaIn1`, chosen by `ExportSettings.MapSource`.
- **`Export/FusionCoords.ToPolyline`** is the single px → Fusion conversion: `X = px/W - 0.5`, `Y = 0.5 - py/H`, Y pointing up. This was checked in Resolve: an earlier version that scaled Y by H/W squashed the track. The `CalibrationFrame` node (a magenta rectangle 10 px inside the edges, behind a pass-through `MergeCalib`) exists to verify this.
  - Optional map labels (`Export/MapLabel`, stored in `ExportSettings.Labels` by lat/lon): either `TextPlus` nodes chained through `MergeLabelN` after `MergeTrack`, or baked into map.png via `Map/LabelRenderer` (the same renderer draws them in the preview). Unverified in Resolve: Text+ `Size` = font px / width, `Thickness2` = outline px / font px, `HorizontalLeftCenterRight` anchor.
  - Optional marker image: `MarkerLoader` → FG of `MergeMarker`, whose Center comes from `MarkerPath` (PolyPath with the same points as TrackPath). Verified in Resolve: the path lies exactly on the line and matches at both ends, but with Displacement = WriteLength the marker lagged in between, so the mask and the path measure arc length differently. Displacement is therefore a piecewise remap of `TrackPath.WriteLength` (`ArcSyncCurve`), selectable by the `PathSync` combo on MarkerPath; default assumes mask = pixel length, path = normalized X/W, Y/H length. Not yet confirmed that this default removes the lag.
  - Still unverified: the `BorderWidth` conversion (`LineWidthPx / Width / 2`) and whether the Loader hold-frame fields are honored.
- **Added points (`Gpx/TrackExtension`):** points the user clicks before the recorded start / after the end are stored in `ExportSettings.ExtraStart/ExtraEnd` (outward order, index 0 next to the recorded track). `MainForm` keeps the recorded points in `_recorded` and rebuilds `_track` = extended points (synthetic times from `ExtensionSpeedKmh`, 0 = moving average), shifting the crop so it stays on the same recorded points. `MapPreview.ExtendMode` handles click/drag/insert/delete and freezes its view while dragging (a refit would otherwise move the point away from the cursor). `Gpx/GpxWriter` saves the extended track as a new GPX.
- **Drawn routes (`ExtendMode.Route`):** a track with no GPX file. Points live in `ExportSettings.RoutePoints` (so a project has `GpxPath = null`); `MainForm._isRoute` makes `SetTrack` use `TrackExtension.FromRoute` (constant-speed times from a fixed start, no elevation). `_track` stays null below 2 points, but the preview still draws the map. While drawing, the frame is pinned as `ManualFrame` (an automatic one would refit on every click) and goes back to automatic at the end if it was automatic before.
- **`Export/ExportSettings`:** shown directly in a `PropertyGrid`. It uses Category/DisplayName/Description attributes and `UI/EnumDescriptionConverter` for enum labels, and is serialized to `.g2f.json` (`ProjectFile`).
  - `LineColor` is stored through `LineColorHex`.
  - `Resolution` / `Width` / `Height` keep each other in sync. Declaration order matters for JSON deserialization.
- **`Export/Exporter`** orchestrates export: prefetch, then `map.png`, `GpxTrack.setting`, `track.png` (raster line, transparent background) and `track_points.csv`.
- **UI:** `UI/MainForm` is built in code (no Designer file). `CropTimeline` is a custom dual-handle slider whose x axis is **time**, so pauses show up, with speed and elevation drawn in the background. Crop changes call `Recompute()`, which refits the frame and updates the preview and info panel.
