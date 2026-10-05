# AuroraPAR v2

Unofficial version of [AuroraPAR](https://github.com/bornac1/AuroraPAR) by bornac1 — a Precision Approach Radar (PAR) display for the IVAO Aurora ATC client.

This is a test version with bug fixes. It is not an official release of the original project. All credit for the original program goes to bornac1; it is distributed under the same MIT license (see `LICENSE`).

## What it does

AuroraPAR connects to Aurora's local interface (port 1130) and shows a PAR display for the selected runway:

- **Top – elevation (profile):** glide path with ±0.5° tolerance, MDH and missed approach point.
- **Bottom – azimuth:** runway centreline with ±1.5° tolerance.

Range marks and the distance shown next to each aircraft are measured **from the touchdown point** (small yellow mark on the runway in both views), as controllers give them on final. The glide path, the extended centreline and their tolerance limits also start at the touchdown point: dashed between touchdown and threshold, solid beyond it. Aircraft are green when within tolerance and red when outside. Use the mouse wheel or the box on the right to change the displayed range.

## Download

Go to the **Actions** tab, open the latest successful **Build** run and download **AuroraPAR-win-x64** from the *Artifacts* section. Unzip it and run `AuroraPAR.exe` (Windows 64-bit, no .NET installation required). Keep `runways.par` in the same folder.

## Settings and profiles

Click **Settings...** to manage profiles and display options. Changes are applied and saved immediately.

- **Profiles:** several named profiles can be kept and switched; *Duplicate*, *Rename*, *Delete*, and *Export* / *Import* to share a profile (a `.json` file) with other controllers.
- **Runway position:** left or right of the screen. With the runway on the right the azimuth view is rotated by 180°, so the side of the centreline shown above/below stays consistent with the direction of flight.
- **Range at start:** last used, the runway's default from `runways.par`, or a fixed value.
- The runway, range, window size and position (and whether it was maximized) are restored at the next start. If the saved position is no longer on any screen (e.g. a monitor was disconnected), the window is centred on the primary screen.

Settings are saved in `AuroraPAR.settings.json` in the user's settings folder (`%AppData%\AuroraPAR` on Windows), so they are kept when a new version is downloaded. **Portable mode:** create an empty file named `AuroraPAR.settings.json` next to `AuroraPAR.exe`, and the program will use that one instead. A damaged settings file is kept as `AuroraPAR.settings.json.bad` and the defaults are used.

## Runway file (`runways.par`)

One runway per line, fields separated by `;`, decimals written with a dot:

```
ICAO;DESIGNATOR;HEADING;ELEVATION;LATITUDE;LONGITUDE;LENGTH_M;WIDTH_M;GLIDE_SLOPE;TCH;MDH;DEFAULT_DISTANCE[;TOUCHDOWN_M]
```

LATITUDE / LONGITUDE are those of the **landing threshold**.

| Field | Unit |
|---|---|
| HEADING | degrees |
| ELEVATION, TCH, MDH | feet |
| LATITUDE, LONGITUDE | decimal degrees (north / east positive) |
| LENGTH_M, WIDTH_M | metres |
| GLIDE_SLOPE | degrees |
| DEFAULT_DISTANCE | NM (1, 2.5, 5, 10, 15 or 20; other values use the closest) |
| TOUCHDOWN_M *(optional)* | metres from the threshold to the touchdown point. If omitted, it is the point where the glide path reaches the runway: TCH / tan(GLIDE_SLOPE), about 290 m for 50 ft and 3° |

Invalid or incomplete lines are ignored.

## Changes from the original

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
- `runways.par` is copied next to the program when building; a clear message is shown if it is missing.
- Automatic build on GitHub (see *Download*).
