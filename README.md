# GPX → Fusion

A WinForms app (.NET 10) that turns a GPX track into a **map background + the track as a Fusion polyline** for DaVinci Resolve. You then build the animation (drawing the track, camera moves) yourself with keyframes in Fusion.

📖 **[User guide](https://dorwindt.github.io/GpxToFusion/)**: the full manual with screenshots, the Resolve workflow, a settings reference and troubleshooting.

## Download

Get `GpxToFusion-v<version>-win-x64.zip` from the **[latest release](https://github.com/DorwinDT/GpxToFusion/releases/latest)**, unzip it and run `GpxToFusion.exe`. Windows 10/11 (64-bit); no installation and no .NET runtime needed. Windows SmartScreen may warn because the exe is not code-signed: *More info → Run anyway*.

## Running from source

```
dotnet run --project src/GpxToFusion -- "D:\!!!\Afternoon_Walk.gpx"
```
or start `GpxToFusion.exe` and open a GPX with the button / drag it into the window.

## Workflow

All commands are in the top menu (File, Export, View, Edit); the most common ones are also in the toolbar below it. **F1** shows an overview of mouse controls and keyboard shortcuts.

1. **Open a GPX.** At the bottom there is a timeline with speed (yellow) and elevation (blue).
2. **Crop the track.** The green handle is the start, the red one the end. Drag the middle to move the whole selection. Set exact values in the *Start/End* fields. Keys: ←/→ = 1 point, Shift = 60 points, Ctrl = 600 points, Home/End = select handle.
3. **Extend the track** (optional, e.g. when you started the tracker late): **Extend track → Start / End** and click in the map. Points chain outward from the green (start) or red (end) point and are joined by straight lines. **Drag** a point = move, **click on a segment** = insert a point (you can drag it right away), **right button** = delete, **Esc** = done. Added segments are shown as a dashed line in the preview. Their time is computed from *5. Added points → Speed* (0 = moving average from the GPX); elevation is taken from the end point. The points are saved in the project and the original GPX stays untouched; *File → Save GPX with added points…* writes a new GPX with the whole track.
4. **Set up the map** (panel on the right): base map (OSM / satellite / CARTO), resolution, padding, extra zoom-out, darkening, line color and width.
   - **Track detail** slider (above the info box): fewer polyline points = faster rendering in Resolve. The preview line is drawn from the simplified points; the point count is shown next to the slider.
   - The preview shows the exact `map.png` frame. The **Full track** button (F6) shows the whole track with the frame outline.
   - **Manual frame**: in the preview, **drag the map** with the mouse (or with the middle button while adding labels or points) and use the **wheel** to zoom in/out (Shift = finer). In *full track* mode you drag the frame outline directly. The frame is saved in the project and stays even when you change the crop. The **Auto frame** button (Ctrl+0) restores the automatic frame fitted to the track (so does changing padding or extra zoom-out).
5. **Map labels** (optional): the **Label** button (Ctrl+L) and a click in the map open a window with text, font, size, color, outline and alignment. In the preview: **double-click** = edit, **drag** = move, **right button** = edit/delete. A new label takes over the style of the last one. Labels are anchored to a GPS position and saved in the project (*Save project…*), so you can change them any time. In *4. Labels → Label output* choose:
   - **Fusion Text+** (default): each label is a `Label1`, `Label2`… node with `MergeLabel1`… Text, font, color and position are changed directly in Resolve and can be animated (e.g. Opacity on `MergeLabelN` → Blend).
   - **Burn into map.png**: labels are part of the map image, exactly as in the preview.
6. **Export…** (Ctrl+E) creates in the chosen folder:

| file | what it is |
|---|---|
| `map.png` | the map at the chosen resolution (without the track) |
| `GpxTrack.setting` | Fusion nodes: Loader with the map + track polyline + color + Merge |
| `track.png` | the track only, on a transparent background (raster alternative) |
| `track_points.csv` | polyline points: time, km, pixel, `write_length` (fraction of length) – helps with keyframe timing |

Tiles are stored in `%LOCALAPPDATA%\GpxToFusion\tiles`, so a second export is instant.

## Importing into DaVinci Resolve

1. Put a **Fusion Composition** on the timeline (Effects → Toolbox → Effects → Fusion Composition), or open the Fusion page on any clip.
2. On the **Fusion** page, click into the Node editor and press **Ctrl+V**. Copy the content of `GpxTrack.setting` with the *Copy .setting* button (Ctrl+Shift+C), or drag the file into the Node editor.
3. Connect the **`MergeCalib`** (or `MergeTrack`) output to **`MediaOut1`**.

### Nodes

```
MapLoader ──────────────┐
TrackPath ─► TrackColor ─┴► MergeTrack ─► MergeCalib (disabled) ─► MediaOut1
```

- **TrackPath** (Polygon/PolylineMask) is the track. Animation: Inspector → *Controls* → **Write On** (Length / Position) and a keyframe 0 → 1. If *Add Write Length keyframes* was on at export, the animation is already set up (linear or by real GPX time). With *Fit to clip length* on (default), Write Length has an expression instead of keyframes: the animation always spans the whole Fusion clip, so a shorter clip = faster drawing, a longer one = slower. When the track starts and when it finishes is set on the *TrackPath* node → Inspector → *Controls* → **Start Delay (frames)** and **End Hold (frames)** (defaults come from the export settings).
- **Image at line head** (e.g. a little train): in the app set *Track → Image at line head* (PNG with a transparent background) and *Image size*. The .setting gets `MarkerLoader`, `MarkerPath` (a Path with the same points as the track) and `MergeMarker` nodes. *Displacement* on `MarkerPath` is an expression derived from `TrackPath.WriteLength`, so the image always follows the end of the line however you animate Write Length. The expression remaps the track length because the mask measures it in pixels and the Path in normalized coordinates. If the image still runs ahead or lags, try `MarkerPath` → *Inspector → Controls → Path Sync* (default *Mask px / Path normalized*; *Off* = the original behavior). Change size and rotation on `MergeMarker` → *Size* / *Angle*. Then take the output from `MergeMarker` (or `MergeCalib`).
- **TrackPath → Border Width** is the line width. If the line differs from the preview, adjust it here.
- **TrackColor** (Background) is the line color.
- **Camera / zoom**: insert a **Transform** after `MergeTrack` and keyframe *Center* and *Size*. Export a larger map (8K or *Extra zoom-out* 0.5–1) so pixels don't show when zooming and there is room to pan.
- Is the map larger than the timeline (e.g. 4K on an HD timeline)? Fusion works at the map resolution and MediaOut scales it down on the timeline. Optionally add Resize/Transform.

### Calibration (first use)

The **MergeCalib** node is disabled (PassThrough). Enable it (select it and press Ctrl+P). A **magenta rectangle 10 px inside the map edges** should appear.
- The rectangle sits on the edges → coordinates are correct and the track lines up with the roads.
- The rectangle doesn't fit → the single function `FusionCoords.ToPolyline` in `src/GpxToFusion/Export/FusionCoords.cs` needs adjusting.

### Black image → diagnostics

In Fusion, select a node and press **1** to show it in the viewer:
- `MapLoader` is black or red → the Loader did not load `map.png` (check the path in Inspector → File) or the map is not on the current frame (Inspector → *Hold First Frame / Hold Last Frame*).
- `TrackColor` shows the line but `MergeTrack` is black → the Merge has no background, the problem is the map.

### Reliable alternative: MediaIn (no Loader)

1. In the app set **Fusion → Map source in Fusion = MediaIn1** and export.
2. Import `map.png` into the Media Pool and put it on the timeline. Stretch it to the desired length.
3. Open the **Fusion** page on that clip. It contains `MediaIn1 → MediaOut1`.
4. Ctrl+V. If `MediaIn1` doesn't connect by itself, drag its output to the yellow (Background) input of `MergeTrack`.
5. Connect `MergeCalib` to `MediaOut1`.

## Map sources and licenses

- OpenStreetMap: © OpenStreetMap contributors (follow the [tile usage policy](https://operations.osmfoundation.org/policies/tiles/); the app downloads at most 2 tiles at a time and caches them)
- Esri World Imagery: Tiles © Esri — Source: Esri, Maxar, Earthstar Geographics
- CARTO: © OpenStreetMap contributors © CARTO

The attribution text is written into `map.png` by default (can be turned off).

## License

[MIT](LICENSE) © Peter Turcan (DorwinDT). You may use, modify and redistribute the code freely, as long as the license notice is kept. The map tiles are not covered by this license; each map source has its own terms (see above).

## Development

```
dotnet build
dotnet test
# test with real tile downloads:
set GPX2FUSION_E2E_OUT=C:\temp\e2e && dotnet test --filter EndToEnd
```

Structure: `Gpx/` (parser, Track), `Geo/` (Web Mercator), `Map/` (tiles, frame, composing), `Export/` (settings, simplification, Fusion writer, exporter), `UI/` (MainForm, CropTimeline, MapPreview).
