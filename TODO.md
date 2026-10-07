# AuroraPAR v2 — To do

## Next update

- **Analog echo: gradual brightening as the beam arrives (eye candy).** Today the echo jumps to full brightness when the centre of the sweep passes over it, then fades (about 0.6 s). Make the rise gradual too: the echo starts to brighten as the beam approaches (beam width), reaches the maximum at the centre of the beam, then fades as now. ScanEffect: add the time until the next pass; brightness = max(rise, fade). Same for the glow of the history dots.
- **Show / hide each line (Display style → Colours & lines).** Today a line can only be "hidden" by giving it the background colour. Add a **Show** check box in each row of the lines (glide path, centreline, approach limits, scan limits, decision height, runway and threshold, ground, touchdown point, altitude scale...; not the range marks, which have their own table, and not the background). Example: many analog radars had no approach limit lines. Hiding a line does not change anything else (e.g. the track stays green/red by the approach limits). Decided: separate for Modern and Analog (old radars lacked some lines).

## Done, to be tested

Last updates, newest first. When an item works in the test, delete it.

- **Track smoothing** (Modern; Off / Light / Strong, default Light): smooth track and steadier label values, history dots at the real positions; coasting starts from the smoothed position. Check: no lag felt on final with Light, turns with Strong, a new aircraft (no jump), landing.
- **Antenna beam on/off:** new profile (first start) = beam off, scan limits −1/+10°, ±15°; tilt controls show a hint. Turning the beam on sets it 4°/6° narrower than the scan limits. Existing profiles: beam on, as before. **Automatic elevation centre** (= GP angle in use). Check: first start without settings file, hint on knobs / buttons / arrow keys, turning on, auto centre when changing approach.
- **Analog:** fade out of the beam about 6 s; soft beam edge (echo weaker in the last 0.5°). **Tooltips** open after 1.1 s.
- **Antenna beam:** its edges are no longer drawn (only the thicker range marks show the beam); *Settings → Radar → Draw the edges of the beam* turns them on. Analog: tooltips dark with amber text.
- **Antenna beam and coasting tracks.** Scan limits are the physical limits (fixed); the antenna beam (EL/AZ width, centre in neutral) is moved by the tilt inside them; only traffic inside the beam is seen; range marks thicker inside the beam. Tilt range automatic, on the step grid (knobs with 0 where the neutral is, possibly asymmetric). Old profiles converted once with the same picture (customised scan limit colours go to the beam). Modern: coasting track (estimated position, diamond, label kept, 8 s, 0–30; not below the ground; the Track ID is kept meanwhile). Analog: out of the beam the echo goes dark, afterglow and dots fade. Check: old profile, narrow beam (e.g. EL 3°, AZ 4°) with tilt to the limits and back to 0, knobs, swapped azimuth, coasting and re-entry, analog fade.
- **Modern GP:** plain text box instead of the drop-down (angle in use; free angle + Enter; Esc / leaving: back).
- **Modern runway and GP keys** (flat PC style, the one in use in blue); no runway drop-down; the ICAO box sets the airport (4th letter, or Enter with a unique prefix; Esc / leaving: back). Check: typing in the ICAO box (keys A, L do not act), single-runway airport, unpublished angle, switching modes.
- **Label fields SSR code** (A2201, from #TRPOS field 7, else 8 — check that Aurora really fills it) **and Track ID** (random 01–99 never repeated; own ID with right click on the label → Set ID...).
- **Analog keys:** square, whole key backlit, Barlow Condensed Bold (text size computed so nothing is cut: check MODERN); RWY, GP DEG, MODERN / COORD / SETUP / blank spare key. APT readout window with keyboard entry (4 characters or Enter, Esc, Ctrl+V; flashes when unknown). Check: 8 or more runways, low window.
- **Altitude grid:** *Show horizontal lines every 1000* (style "Altitude lines"). Note: the distance text can already be limited to whole miles per range (Settings, range marks, text column).
- **Earlier blocks:** MAPt DIST and GP selector, antenna colour, azimuth swap, analog afterglow (strength 0.4 / 0.24 / 0.12 every 0.8 s: too strong or too weak?), knob mouse zones and curved-arrow cursors (sharp at 100 / 125 / 150 % scaling?), DH above QNH on the console, Hide labels in the top section, ICAO of the connected callsign at the connection.

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

- (Settled: see "Smooth normal tracks" in Next update.)
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

- GitHub Actions: update the actions (Node 20 is deprecated) — **do it during the move to the new AuroraPAR GitHub** (see above).
- Delete the `phase1` test branch on GitHub.
- Before the pull request to bornac1: check upstream changes, prepare the README.
