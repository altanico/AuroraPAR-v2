# AuroraPAR v2

Unofficial version of [AuroraPAR](https://github.com/bornac1/AuroraPAR) by bornac1 — a Precision Approach Radar (PAR) display for the IVAO Aurora ATC client.

This is a test version with bug fixes. It is not an official release of the original project. All credit for the original program goes to bornac1; it is distributed under the same MIT license (see `LICENSE`).

## What it does

AuroraPAR connects to Aurora's local interface (port 1130) and shows a PAR display for the selected runway:

- **Top – elevation (profile):** glide path with its approach limits and decision height (DH): a line from the touchdown point to 3 NM, and a dashed vertical line where it meets the glide path. The DH distance is also marked in the azimuth view.
- **Bottom – azimuth:** runway centreline with its approach limits.

**Approach limits** (red lines, default 0.5° above/below the glide path and 1.5° left/right of the centreline, each side set separately in *Settings*) start at the touchdown point: inside them a track is green, outside red. **Scan limits** (blue lines) start from the radar antenna (small blue square, at half the runway length) and can be set in *Settings* (default 8° up, −1° down, 10° left and right): a track is shown only inside them. The scale of the views depends only on the range (the default scan limits fill the view): wider or tilted scan limits are drawn wider or moved, also beyond the edges, while the centreline, approach limits and tracks keep the same size. **Antenna tilt:** the scan limits can be moved up/down and left/right with the *Antenna tilt* buttons or the keyboard arrows (step 2°, up to 10°, both set in *Settings*); *Neutral* or the Home key brings the antenna back. While the antenna is tilted an orange reminder (e.g. `EL TILT 2.0 UP`, `AZ TILT 2.0 L`) is shown in the information area. The tilt goes back to neutral when the runway changes. Left/right are as seen by the pilot on the approach.

**Information area** (top of the profile view): runway, magnetic final course (`CRS`: the true heading of the runway corrected with its magnetic variation, from `runways.par` or the default in *Settings*, rounded to the degree), glide path angle (`GP`), QNH or QFE (computed from the METAR QNH and the threshold elevation), the minimum (e.g. `DA 392 ft` with QNH, `DH 241 ft` with QFE), antenna tilt when not neutral, connection status and traffic refresh check. An **altitude scale** (altitudes with QNH, heights with QFE; feet or metres) is drawn on the runway side.

Range marks (every 2 NM at 20 NM; every NM at 15 and 10 NM; every NM plus dashed half miles without text at 5 NM; every quarter mile at 2.5 and 1 NM) and the distance shown next to each aircraft are measured **from the touchdown point** (small yellow mark on the runway in both views), as controllers give them on final. The glide path, the extended centreline and their tolerance limits also start at the touchdown point: dashed between touchdown and threshold, solid beyond it. Aircraft are green when within tolerance and red when outside. Use the mouse wheel or the box on the right to change the displayed range. The DH can be changed on the fly with the **−/+** buttons or by typing it (10 ft steps): it is not saved and goes back to the `runways.par` value when the runway changes.

**Tracks and labels:** each aircraft is shown with a track symbol (default: circle with cross), green inside the approach limits and red outside, followed by its **history tail** (previous positions, default 50 dots, 3 to 100, one every 2 s: 0.5 to 10 s). The **label** is configurable separately for the two views with *Settings → Edit labels...*: rows and columns, and in each cell one of callsign, distance from touchdown, altitude (`A`, QNH) or height (`H`, QFE), ground speed, vertical speed, deviation from the glide path (`U`/`D`) or from the centreline (`L`/`R`, as seen by the pilot); a label with no values shows only the symbol. Labels appear 45° up-right of the track and can be **dragged** with the mouse (a leader line then joins them to the track); **double click** puts a label back, **right click** on a label hides it; right click near a track hides/shows its label, and where several tracks are close (formation) or some labels are hidden a menu lists them by callsign (with *Show all hidden labels*); the **Hide labels** button or the **L** key hides/shows all labels (showing them again also brings back the ones hidden one by one).

**Antenna scan effect:** as on the old PAR screens, a beam sweeps the elevation view (up and down) and the azimuth view (left and right) in turn. It is only a graphic effect drawn over the display: tracks are updated as usual, whatever the position of the beam. It can be switched off, or made slower/faster, in *Settings → Display*.

**Analog mode:** the *Analog (A)* button (or the A key) turns the display into an old PAR scope: one round screen in a thin metal ring on the console panel, with the elevation view above and the azimuth view below, everything drawn in green phosphor with a soft glow. The beam is always on; each aircraft is an echo that lights up when the beam passes over it and then fades until the next pass (its position is still the latest one from Aurora). As on the real scopes there are no labels and no altitude scale; the history tail fades with age. The screen is as large as the window allows: in a low window its top and bottom (frame and glass only) are cut by the window edges, so the views stay large; the console panel and the knobs are scaled to fit the window. Range, antenna tilt and decision height are set with **knobs** on the right: turn them with the mouse wheel, drag up/down, or click on the right/left half; double click puts the tilt back to neutral or the DH back to the runway value. The information is on a **console panel** at the left of the scope: amber 14-segment readouts (5 characters) for airport, runway, final course, glide path angle, QNH/QFE, minimum, range and antenna tilt, and status lamps — **STS** (green connected, red not connected), **ANT. R/R** (antenna refresh rate, i.e. Aurora's traffic refresh: green good; flashing red too slow: set Aurora's traffic refresh rate to 0.5 s; off while not yet measured; the details are in its tooltip) and **TILT** (amber while the antenna is tilted). Labels use the B612 cockpit font (SIL Open Font License, `Fonts/B612-OFL.txt`). *Modern (A)* goes back to the normal display; the choice is saved in the profile.

**Traffic refresh check:** AuroraPAR measures how often the positions received from Aurora really change and shows it below the connection status (`DATA 0.5s` in green). If Aurora's traffic refresh rate is left at the normal 3 s, tracks move in jumps and a red warning appears (`DATA 3.0s - SET AURORA TRAFFIC REFRESH TO 0.5s`); it disappears by itself once the setting is changed. Only moving aircraft (above 50 kt) are measured, and about 10–20 seconds of traffic are needed.

## Download

Go to the **Actions** tab, open the latest successful **Build** run and download **AuroraPAR-win-x64** from the *Artifacts* section. Unzip it and run `AuroraPAR.exe` (Windows 64-bit, no .NET installation required). Keep `runways.par` in the same folder.

## Settings and profiles

Click **Settings...** to manage profiles and display options. Changes are applied and saved immediately.

- **Profiles:** several named profiles can be kept and switched; *Duplicate*, *Rename*, *Delete*, and *Export* / *Import* to share a profile (a `.json` file) with other controllers.
- **Display mode:** Modern or Analog (old radar scope, see above).
- **Runway position:** left or right of the screen. With the runway on the right the azimuth view is rotated by 180°, so the side of the centreline shown above/below stays consistent with the direction of flight.
- **Radar:** approach limits (above, below, left, right), scan limits (up, down, left, right), tilt step and maximum. Each profile can hold the values of a different radar type.
- **Tracks and labels:** history tails on/off, number of dots and seconds between dots; symbol and size of the track, history dots (smaller, default 3 px dots), threshold, touchdown point and antenna (circle, filled circle, circle with cross, square, diamond, triangle, inverted triangle, +, ×, capsule (filled vertical bar with rounded ends), line, none); *Edit labels...* for the label layouts.
- **Units and references:** QNH or QFE for all runways; pressure in hPa or inHg; name of the minimum (DA/DH, OCA/OCH, MDA/MDH: the altitude form is used with QNH, the height form with QFE); heights, deviations and vertical speed in ft and ft/min or m and m/s (altitude scale and labels); altitude scale on/off; default magnetic variation for the final course.
- **Range:** a *preferred range*, and separate choices for the range at start (last used, runway default from `runways.par`, or preferred) and when the runway changes (keep current, runway default, or preferred).
- The runway, range, window size and position (and whether it was maximized) are restored at the next start. If the saved position is no longer on any screen (e.g. a monitor was disconnected), the window is centred on the primary screen.

Settings are saved in `AuroraPAR.settings.json` in the user's settings folder (`%AppData%\AuroraPAR` on Windows), so they are kept when a new version is downloaded. **Portable mode:** create an empty file named `AuroraPAR.settings.json` next to `AuroraPAR.exe`, and the program will use that one instead. A damaged settings file is kept as `AuroraPAR.settings.json.bad` and the defaults are used.

## Runway file (`runways.par`)

Runways can be edited with **Runways...**: list on the left, fields on the right, *New*, *Duplicate* (handy for the opposite end of the same runway) and *Delete*. Values are checked while typing (wrong fields turn red) and written only with *Save*; the previous file is kept as `runways.par.bak`. The layout of the file is kept: unchanged runways are written back exactly as they were, separator/comment lines stay in place, a duplicated runway is saved right after the original. The designator is free text, so it can also describe the approach (e.g. `14 3.0` and `14 2.5` for two glide slopes on the same runway). Threshold coordinates can be typed in any common format, e.g. `44.838694`, `44,838694`, `N44.838694`, `44°50'19.3"N`, `44 50 19.3 N`, `44°50.32'N`, `445019N`, `445019.30N`, `0004204W` (also `O` for west); a latitude and longitude pasted together in one field (e.g. `445019N 0004204W` or `44.8387, -0.701`) are split automatically.

The file can also be edited with a text editor:

One runway per line, fields separated by `;`, decimals written with a dot:

```
ICAO;DESIGNATOR;HEADING;ELEVATION;LATITUDE;LONGITUDE;LENGTH_M;WIDTH_M;GLIDE_SLOPE;TCH;MDH;DEFAULT_DISTANCE[;TOUCHDOWN_M[;MAGVAR]]
```

LATITUDE / LONGITUDE are those of the **landing threshold**.

| Field | Unit |
|---|---|
| HEADING | degrees, **true** |
| ELEVATION, TCH, MDH | feet |
| LATITUDE, LONGITUDE | decimal degrees (north / east positive) |
| LENGTH_M, WIDTH_M | metres |
| GLIDE_SLOPE | degrees |
| DEFAULT_DISTANCE | NM (1, 2.5, 5, 10, 15 or 20; other values use the closest) |
| TOUCHDOWN_M *(optional)* | metres from the threshold to the touchdown point. If omitted, it is the point where the glide path reaches the runway: TCH / tan(GLIDE_SLOPE), about 290 m for 50 ft and 3° |
| MAGVAR *(optional)* | magnetic variation, e.g. `3E` or `2W` (or `3`, `-2`), used for the magnetic final course. If omitted, the default set in *Settings* is used. To give it without TOUCHDOWN_M leave that field empty: `...;10;;3E` |

Invalid or incomplete lines are ignored.

## Changes from the original

- Analog display mode (round phosphor scope with knobs) and antenna scan effect (graphic only, can be switched off).
- Configurable, draggable labels with leader lines; history tails; symbol library; labels on/off.
- Approach limits set separately for each side; scan limits from the antenna at half the runway, adjustable; antenna tilt from buttons or keyboard with on-screen reminder.
- QNH/QFE, hPa/inHg, DA/DH, OCA/OCH or MDA/MDH; altitude scale in feet or metres.
- Aircraft stay visible while inside the drawn scan limits, also over the runway after the threshold (previously they disappeared when crossing it).
- Settings window with user profiles (saved, exportable/importable) and runway on the left or right of the screen.
- Drawing rewritten: screen elements are updated instead of being recreated at every refresh.
- Fixed traffic disappearing until restart: answers from Aurora are now matched to their request, so one late answer can no longer shift all the following ones.
- Glide path, centreline and tolerances drawn from the touchdown point (dashed up to the threshold), in/out of tolerance computed the same way.
- Range marks measured from the touchdown point instead of the threshold; distance from touchdown shown next to each aircraft; aircraft placed by their distance along the centreline.
- Numbers are read correctly whatever the Windows language (previously coordinates were wrong on English Windows).
- Automatic reconnection when Aurora is started later or the connection drops; network errors no longer crash the program.
- Fixed possible crashes on incomplete answers from Aurora and on incomplete lines in `runways.par`.
- Refreshes no longer overlap; the METAR is requested once a minute instead of ten times a second; US altimeter settings (`A2992`) are converted to hPa.
- Default range is 10 NM; the range box now follows the selected runway's default distance.
- Profile view: callsign label correctly placed at airports above sea level.
- Warning when Aurora's traffic refresh rate is too slow for a PAR.
- Graphical runway editor, accepting coordinates in any common format.
- `runways.par` is copied next to the program when building; a clear message is shown if it is missing.
- Automatic build on GitHub (see *Download*).
