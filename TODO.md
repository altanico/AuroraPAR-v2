# AuroraPAR v2 — To do

## Next update

(Done and published, to be tested: "Hide labels (L)" back in the top section, a little apart from the antenna tilt; the ICAO filter is for the session only, and the airport of the connected callsign has priority over it.)

(the previous blocks — GP selector and MAPt DIST, antenna colour, azimuth swap, analog afterglow, knob mouse zones, DH above QNH — is done and to be tested, see below.)

## Done, to be tested (last update)

- **Analog: slight "afterglow" trail on the track (echo) and on the history dots only**, for a more vintage look (phosphor persistence). Only if it turns out to be easy; keep it light, as the earlier stronger trail effect looked like a grid. Analog mode only.
- **Analog knobs: clearer and more reliable mouse control.** Today, depending on where the mouse is on the knob, it can work well or go "crazy" (each click jumps forward and then back). Proposal: change the mouse pointer over the knob to a **right half-arc arrow** (turn clockwise) or a **left half-arc arrow** (turn counter-clockwise), as in some flight simulators (see the reference photo: curved arrow on the Airbus knob), so the direction is obvious; make the clickable areas **distinct and separate** (left half = counter-clockwise, right half = clockwise, no overlap or ambiguity with the drag and wheel handling). Mouse wheel and drag stay.
  - Cause found in the code (to confirm with a test): the direction of a click is decided only by the horizontal position against the vertical centre line of the **whole control** (not of the knob circle), with **no dead zone**, and it is decided on mouse **release**; a click that moves 14 px or more vertically becomes a drag instead. So near the centre line, or with a slightly moving mouse, the direction flips or jumps. Fix: act only on the knob circle, a small neutral zone in the middle, the pointer showing the side under the mouse before the click, decision on mouse down.
- **Left panel (Analog console): the DH value (OCA, MDA, ...) goes above the QNH/QFE value.** Swap the order of the two readouts.
- **Glide path adjustable on the fly** (new function; decided). Change the GP angle of the selected runway without choosing another "runway" in the top drop-down: some airports have approaches with different angles for the same runway.
  - **The file format does not change.** If `runways.par` already has several lines for the same airport and runway (same ICAO and designator) with different glide path angles (e.g. 2.5° and 3.0°), AuroraPAR finds them by itself, groups them under one runway in the drop-down and shows a **selector with exactly those values**; the controller picks one (each line keeps its own MDH, distance, touchdown, magnetic variation). With a single line there is no selector.
  - **Modern display:** besides the selector with the published values, a **free field** for any angle. When the angle is not one of the published ones, a clear **warning** is shown ("unpublished approach", e.g. the value in another colour). The DH/DA value does **not** change with the angle (it is a published minimum, the controller sets it, as today); the drawing already shows where the glide path meets the DH line. 
  - **Missed approach point distance (published on the charts as "MAP RPI DIST") shown for every approach** (decided): the distance from the touchdown point at which the glide path reaches the DH/OCH, `MDH / (tan(GP) × 6076 ft)`. The program already computes it for the drawing (`MissedApproachPointNM`); checked against a real chart: OCH 260 ft gives 0.98 NM at 2.5° and 0.87 NM at 2.8°, as published. Show it on the left panel (Analog: 5 characters, e.g. `0.98`; Modern: in the information area), also as a **check that the runway file is correct** (compare it with the chart), and it is the number for the phraseology "approach terminating at [NM]". Label: **MAPt DIST** (MAPt = Missed Approach Point, the international abbreviation; the lower-case t tells it from MAP = map). The DH is fixed (it comes from the obstacles) and does not follow the angle; what changes with the angle is the MAPt. So with an **unpublished approach** (free GP angle) the **new MAPt is essential information**: always visible, next to the "unpublished approach" warning (Modern). The calculation uses the DH in effect (the file value, as today; if the controller changes it by hand, the MAPt follows, consistent with the drawing).
  - **Analog scope:** only the **locked selector** with the published approaches of the file (no free angle: it would not be realistic).
  - To check when planning: how the runway list and the editor treat several lines with the same name today (duplicates), the runway reminders (saved by "ICAO DESIGNATOR", shared by the approaches of the same runway), what is remembered in the profile. Bigger job: plan it first (candidate for planning with Opus).
- **Antenna colour separate from the scan limits.** Today the antenna symbol uses the "Scan limits and antenna" style. Add an **Antenna** element in *Display style → Colours & lines* (colour, same table as the others) used for the symbol in both views; the scan limits keep their own. Existing profiles start with the same colour as the scan limits, so nothing changes until it is edited. Analog: fixed theme, same brightness for both.
- **Azimuth tilt: option to swap left and right.** Today left/right is as seen by the pilot (relative to the traffic). Depending on where the controller "looks" it can be more intuitive to have right = the side where the controller looks. Setting in the profile (default: as now), applied to the AZ TILT knob, the keyboard arrows, the readout (L/R on the console panel and on the screen) and, later, the external knob.

- Analog afterglow: too strong / too weak? (strength of the copies 0.4 / 0.24 / 0.12, every 0.8 s; dots glow 60%→100% at the beam).
- Knob cursors (curved arrows) visible and sharp on Windows at 100% / 125% / 150% scaling.

## To verify with live traffic

- Labels, colours (plots / tracks inside and outside the limits), history dots at 2–3 s, only inside the scan.
- Antenna tilt on both sides, runway on the left and on the right.
- QFE and final course (CRS) against the published values.
- Analog mode: echoes lit by the beam, console panel, knobs, BRT.
- Fractions `1 1/4NM` and text size; BRT 110–150% (Modern and Analog); visual colour picker (live preview, Cancel, recent colours, also in AuroraCoord).
- Range marks and reminders at each range.
- Coordination panel (AuroraPAR + AuroraCoord, radar / tower / monitor): automatic pairing from the callsign (`#CONN` answer), relay reachable (port 8883), alert sound.
- Phone / tablet panel (docs/coord, GitHub Pages): QR code from the PC, link with the PC panel, portrait / landscape, alert sound, vibration, screen kept on, reconnection after the lock screen, home screen (Android / iPhone), presence when phone and PC have the same role.

## Decisions pending

- Smoothed altitude in the elevation view (to hide the steps of the real altitude updates)?
- Runway reminders following the runway when it is renamed in the editor?

## Later

- **Runway heading (true) from the two thresholds — external utility that does only that** (decided: separate small tool, not inside the radar). The two threshold coordinates (any format of the editor, at least 5–6 decimals or seconds with hundredths) give the true heading with two decimals; the distance between the thresholds is shown as a check against the runway length. Why: 0.1° = 0.026 NM (48 m) of lateral error at 15 NM, 0.3° = 145 m; a heading from the runway number or the magnetic value on the charts (rounded) is not enough; coordinates with 4 decimals (11 m) give about 0.25° on a 2500 m runway. Possible later: check with live traffic (aircraft established on the ILS should have a lateral offset near zero at all distances: a slope means a heading error; the simulator's runway can differ by a few tenths of a degree from the real one, and aircraft fly the simulator's).
- **Runway management as an external app (like AuroraCoord)** — only if the editor grows (heading tools, file checks, import). Then in this order: (1) runway reminders move into the radar (*Display style → Reminders*, they are a display preference and live in the settings file, which two programs must not both write); (2) the radar rereads `runways.par` by itself when it changes; (3) the external app (same source files linked, third download in the Releases). For now the editor stays in the radar, opened from *Settings → Edit the runways file*.

- Shout line (always-open intercom) in the coordination panel.
- Event log for the instructor (monitor), with saving to a file.
- **External controls — FROZEN until the keypad arrives** (mini keypad, 9 keys + 3 knobs, programmable with its own software, onboard memory). Agreed plan:
  - Large knob = RANGE (press: back to a chosen start range); small knob 1 = EL TILT, small knob 2 = AZ TILT (press: tilt back to zero).
  - 6 keys = coordination lights incl. RESET (AuroraCoord or the panel in AuroraPAR, whichever is open).
  - 3 spare keys = function chosen from a list in the settings (Modern/Analog, next traffic, open panel, BRT −/+, reminders on/off…).
  - DH and BRT stay on screen only.
  - Global hotkeys (work without focus). Defaults: knobs F13–F21, lights F22–F24 + Shift+F13–F15, spare Shift+F16–F18; reassignable with "press the key to assign".
  - Manual: table key → combination to set in the keypad software.
  - First check what the keypad software allows (F13–F24? Shift combinations?). Joystick input could be added later on the same mapping page.
- Range marks in km, tablet view, Mac/Linux version (Avalonia).
- Coordination panel: button gently pulsing when the selected traffic reaches a reminder distance.
- Translations of the user manual.

## Security of the coordination panel — DECIDE WHEN THE PROJECT IS FINISHED (not now, to keep the tests simple)

Review done: no open ports, only the state of the lights travels (no personal data), received messages are only read, the phone page is isolated in the browser; GitHub account has 2FA. Weak points and agreed ideas:

- **Channel can be computed** (airport + public formula): anyone reading the code can join LIRF and press lights / reset. Idea: optional **panel code** (shared secret) in the channel name; empty = as now. Same field on AuroraPAR, AuroraCoord and the phone page; saved until changed (fixed for the group; a "Generate code" button for a new one per session); the QR code / link already includes it; a small 🔒 in the status line when set. Optional: **encrypt the content** with the same code, so the relay sees nothing.
- **Phone page loads the MQTT library from a public CDN** (jsDelivr, unpkg) with no fixed version: pin the exact version with an integrity check (SRI), or include the file in the repository.
- Public relays (emqx, hivemq): free, best-effort, they see the IP address; a private relay is not worth it for this use.
- Hosting address of the phone page: now `altanico.github.io/AuroraPAR-v2/coord/` (appears in `Coordination.cs` `PhonePanelUrl`, manual, README). Options if it must not be on the personal GitHub: a neutral GitHub organization, Cloudflare Pages / Netlify, or an own domain. Undecided.

## Dedicated home for the project — to do at the end, as one block (decided: yes)

Move the project from the personal account (`altanico`) to a **GitHub organization with a neutral name** (not "IVAO", not confusable with the official project; the site says it is an unofficial add-on for Aurora). Development continues on GitHub there; downloads and the phone link live on the organization's site.

- Choose the name and create the organization (user), then transfer the repository (Settings → Transfer; history is kept, the project stays linked to bornac1's as a fork, so a pull request stays possible).
- **Site** (GitHub Pages of the organization, `name.github.io`): home page with the Download button, the manual, a few images and the link to the phone panel (`/coord/`). Do it before the old address spreads: the old Pages address does not redirect after the transfer.
- **Downloads as GitHub Releases** instead of Actions artifacts (public, permanent, no GitHub account needed, they do not expire): the workflow creates a Release for each version, with AuroraPAR and AuroraCoord.
- Change `PhonePanelUrl` in `Coordination.cs`, the manual and the README to the new address (the address is inside the programs: old versions keep the old QR link, so keep the old address working as long as possible).
- A custom domain (about 10 €/year) can be added later: GitHub then redirects the `github.io` address to it.
- Pull request to bornac1: separate and optional, when he wants to follow the development (ask him: one big pull request or smaller ones by topic; whether he wants the phone page in his project).

## Maintenance

- GitHub Actions: update the actions (Node 20 is deprecated).
- Delete the `phase1` test branch on GitHub.
- Before the pull request to bornac1: check upstream changes, prepare the README.
