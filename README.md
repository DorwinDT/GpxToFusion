# GPX → Fusion

WinForms appka (.NET 10), ktorá z GPX trasy vyrobí **mapový podklad + trasu ako Fusion polyline** pre DaVinci Resolve. Animáciu (kreslenie trasy, pohyb kamery) si potom robíš sám keyframami vo Fusion.

## Spustenie

```
dotnet run --project src/GpxToFusion -- "D:\!!!\Afternoon_Walk.gpx"
```
alebo spusti `GpxToFusion.exe` a GPX otvor tlačidlom / pretiahni do okna.

## Postup

1. **Otvor GPX.** Dole je časová os s rýchlosťou (žltá) a výškou (modrá).
2. **Orež trasu.** Zelené ťahadlo je začiatok, červené koniec. Ťahaním stredu posúvaš celý výber. Presné hodnoty nastavíš v poliach *Začiatok/Koniec*. Klávesy: ←/→ = 1 bod, Shift = 60 bodov, Ctrl = 600 bodov, Home/End = výber ťahadla.
3. **Doplň trasu** (voliteľné, napr. keď si tracker zapol neskôr): **Doplniť trasu → Doplniť začiatok / koniec** a klikaj do mapy. Body sa reťazia od zeleného (začiatok) alebo červeného (koniec) bodu smerom von a spájajú sa rovnými čiarami. **Ťahanie** bodu = posun, **klik na úsek** = vloženie bodu (dá sa hneď ťahať), **pravé tlačidlo** = zmazanie, **Esc** = koniec. Doplnené úseky sú v náhľade prerušovanou čiarou. Čas sa im dopočíta z rýchlosti *5. Doplnené body → Rýchlosť* (0 = priemer v pohybe z GPX), výška sa preberie z krajného bodu. Body sa ukladajú do projektu, pôvodný GPX ostane nedotknutý; *Doplniť trasu → Uložiť GPX ako…* zapíše nový GPX s celou trasou.
4. **Nastav mapu** (panel vpravo): podklad (OSM / satelit / CARTO), rozlíšenie, okraj, extra oddialenie, stmavenie, farbu a hrúbku čiary.
   - Náhľad ukazuje presný výrez `map.png`. Prepínač *Náhľad: celá trasa* ukáže celú trasu aj s rámom výrezu.
   - **Ručný výrez**: v náhľade **ťahaj mapu** myšou (alebo stredným tlačidlom, keď pridávaš popisy či body) a **kolieskom** približuj/odďaľuj (Shift = jemnejšie). V režime *celá trasa* ťaháš priamo rám výrezu. Výrez sa uloží do projektu a ostane, aj keď zmeníš orezanie. Tlačidlo **Auto výrez** vráti automatický výrez podľa trasy (rovnako aj zmena okraja alebo extra oddialenia).
5. **Popisy na mape** (voliteľné): tlačidlo **Pridať popis** a klik do mapy otvorí okno s textom, fontom, veľkosťou, farbou, obrysom a zarovnaním. V náhľade: **dvojklik** = upraviť, **ťahanie** = posunúť, **pravé tlačidlo** = upraviť/zmazať. Nový popis preberie štýl posledného. Popisy sú naviazané na GPS polohu a ukladajú sa do projektu (*Uložiť projekt…*), takže ich vieš kedykoľvek zmeniť. V *4. Popisy → Výstup popisov* zvolíš:
   - **Fusion Text+** (predvolené): každý popis je uzol `Label1`, `Label2`… s `MergeLabel1`… Text, font, farbu aj polohu meníš priamo v Resolve a dajú sa animovať (napr. Opacity na `MergeLabelN` → Blend).
   - **Vpáliť do map.png**: popisy sú priamo v obrázku mapy, presne ako v náhľade.
6. **Exportovať…** (Ctrl+E) vytvorí v zvolenom priečinku:

| súbor | čo to je |
|---|---|
| `map.png` | mapa v zvolenom rozlíšení (bez trasy) |
| `GpxTrack.setting` | Fusion uzly: Loader s mapou + polyline trasy + farba + Merge |
| `track.png` | iba trasa na priehľadnom pozadí (rastrová alternatíva) |
| `track_points.csv` | body polyline: čas, km, pixel, `write_length` (podiel dĺžky) – pomôcka na časovanie keyframov |

Dlaždice sa ukladajú do `%LOCALAPPDATA%\GpxToFusion\tiles`, takže druhý export je okamžitý.

## Import do DaVinci Resolve

1. Na timeline daj **Fusion Composition** (Effects → Toolbox → Effects → Fusion Composition) alebo otvor Fusion stránku na ľubovoľnom klipe.
2. Na **Fusion** stránke klikni do Node editora a stlač **Ctrl+V**. Obsah `GpxTrack.setting` vložíš tlačidlom *Kopírovať .setting do schránky*, prípadne súbor pretiahni do Node editora.
3. Výstup **`MergeCalib`** (alebo `MergeTrack`) pripoj na **`MediaOut1`**.

### Uzly

```
MapLoader ──────────────┐
TrackPath ─► TrackColor ─┴► MergeTrack ─► MergeCalib (vypnutý) ─► MediaOut1
```

- **TrackPath** (Polygon/PolylineMask) je trasa. Animácia: Inspector → *Controls* → **Write On** (Length / Position) a keyframe 0 → 1. Ak bola pri exporte zapnutá voľba *Pridať keyframy*, animácia už je pripravená (lineárne alebo podľa reálneho GPX času). So zapnutým *Prispôsobiť dĺžke klipu* (predvolené) je na Write Length výraz namiesto keyframov: animácia vždy trvá celú dĺžku Fusion klipu, takže kratší klip = rýchlejšie kreslenie, dlhší = pomalšie. Kedy trasa začne a kedy dobehne, nastavíš na uzle *TrackPath* → Inspector → *Controls* → **Start Delay (frames)** a **End Hold (frames)** (predvolené hodnoty sú z nastavení exportu).
- **Obrázok na čele čiary** (napr. vláčik): v appke nastav *Trasa → Obrázok na čele čiary* (PNG s priehľadným pozadím) a *Veľkosť obrázka*. V .setting pribudnú uzly `MarkerLoader`, `MarkerPath` (Path s rovnakými bodmi ako trasa) a `MergeMarker`. *Displacement* na `MarkerPath` je výraz odvodený z `TrackPath.WriteLength`, takže obrázok ide vždy s koncom čiary, nech animuješ Write Length akokoľvek. Výraz prepočítava dĺžku trasy, lebo maska ju meria v pixeloch a Path v normalizovaných súradniciach. Ak by obrázok predsa len predbiehal alebo zaostával, skús `MarkerPath` → *Inspector → Controls → Path Sync* (predvolené *Mask px / Path normalized*; *Off* = pôvodné správanie). Veľkosť a natočenie zmeníš na `MergeMarker` → *Size* / *Angle*. Výstup potom berieš z `MergeMarker` (alebo `MergeCalib`).
- **TrackPath → Border Width** je hrúbka čiary. Ak je čiara iná ako v náhľade, uprav ju tu.
- **TrackColor** (Background) je farba čiary.
- **Kamera / zoom**: za `MergeTrack` vlož **Transform** a keyframuj *Center* a *Size*. Exportuj väčšiu mapu (8K alebo *Extra oddialenie* 0.5–1), aby pri zoome nebolo vidieť pixely a bolo kam posúvať.
- Mapa je väčšia ako timeline (napr. 4K na HD timeline)? Fusion pracuje v rozlíšení mapy a MediaOut ju na timeline zmenší. Prípadne pridaj Resize/Transform.

### Kalibrácia (prvé použitie)

Uzol **MergeCalib** je vypnutý (PassThrough). Zapni ho (vyber ho a stlač Ctrl+P). Mal by sa zobraziť **fialový rámik 10 px od okrajov mapy**.
- Rámik sedí na okrajoch → súradnice sú správne a trasa sedí na cestách.
- Rámik nesedí → treba upraviť jedinú funkciu `FusionCoords.ToPolyline` v `src/GpxToFusion/Export/FusionCoords.cs`.

### Čierny obraz → diagnostika

Vo Fusion vyber uzol a stlač **1**, tým ho zobrazíš vo vieweri:
- `MapLoader` je čierny alebo červený → Loader nenačítal `map.png` (skontroluj cestu v Inspectore → File) alebo mapa nie je na aktuálnom frame (Inspector → *Hold First Frame / Hold Last Frame*).
- `TrackColor` ukazuje čiaru, ale `MergeTrack` je čierny → Merge nemá pozadie, problém je v mape.

### Spoľahlivá alternatíva: MediaIn (bez Loadera)

1. V appke nastav **Fusion → Zdroj mapy vo Fusion = MediaIn1** a exportuj.
2. `map.png` naimportuj do Media Poolu a polož na timeline. Roztiahni ho na požadovanú dĺžku.
3. Na tom klipe otvor stránku **Fusion**. Je tam `MediaIn1 → MediaOut1`.
4. Ctrl+V. Ak sa `MediaIn1` nepripojí sám, potiahni jeho výstup na žltý (Background) vstup `MergeTrack`.
5. `MergeCalib` pripoj na `MediaOut1`.

## Mapové podklady a licencie

- OpenStreetMap: © OpenStreetMap contributors (dodržuj [tile usage policy](https://operations.osmfoundation.org/policies/tiles/), appka sťahuje max. 2 dlaždice naraz a cachuje ich)
- Esri World Imagery: Tiles © Esri — Source: Esri, Maxar, Earthstar Geographics
- CARTO: © OpenStreetMap contributors © CARTO

Attribution text sa štandardne vpisuje do `map.png` (dá sa vypnúť).

## Vývoj

```
dotnet build
dotnet test
# test s reálnym sťahovaním dlaždíc:
set GPX2FUSION_E2E_OUT=C:\temp\e2e && dotnet test --filter EndToEnd
```

Štruktúra: `Gpx/` (parser, Track), `Geo/` (Web Mercator), `Map/` (dlaždice, výrez, skladanie), `Export/` (nastavenia, zjednodušenie, Fusion writer, exporter), `UI/` (MainForm, CropTimeline, MapPreview).
