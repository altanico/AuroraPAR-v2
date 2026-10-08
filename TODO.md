# AuroraPAR v2 — To do

## Next update

- **Test traffic: rate of descent keys with full names** (mockup testtraffic7): **Reduce rate of desc.**, **Normal rate of desc.** (bold), **Increase rate of desc.**; the column of these keys as wide as the stick (150), all three keys the same width, text centred.

- **Test traffic: the stick follows the keys** (visual confirmation): while ◀ L / R ▶ are held the stick knob moves to the left / right edge (and back to the centre when released); the same for climb / descent. **New keys ▲ UP / ▼ DN** with the same logic as L / R: held = stick at its top / bottom edge (vertical speed changing while held), a click = at least 1 s; released, the vertical speed reached stays; the knob moves with them. Placed **under the stick, UP right above DN** (decided, less mouse travel; mockup testtraffic6): turn rate / stick / ▲ UP / ▼ DN / ◀ L · Final CRS · R ▶, all as wide as the stick.

- **Analog: each control group as a knob or as keys** (Settings, analog only, per profile; groups can be mixed). Keys in the style of the RWY / GP keys; each key also has a keyboard key (for a programmable USB keypad, see "External controls"). **Also a virtual keypad on a phone / tablet** (later): a web page like the coordination phone panel (same relay, same pairing/QR link) with the same keys, sending the commands to AuroraPAR — so every key needs a named command, not only a keyboard key.
  - **RANGE:** choice of (a) one key per range (1, 2.5, 5, 10, 15, 20 NM, the one in use lit) or (b) three keys `<` `#` `>`: steps down / up, `#` back to the default range (set in Settings, e.g. the profile's preferred range); the text of `#` is free, **max 5 characters**, default "DEF".
  - **EL TILT:** UP / 0 / DN. **AZ TILT:** L / 0 / R (following the left/right swap).
  - **DH:** + / − / RWY (back to the runway value). **BRT:** + / − / 100%.

- **(Done, to be tested) Test traffic, control panel v2** (second mockup sent): model on heading (CRS off final, up to about 30°) instead of drift; **turn rate selector 1.5°/s / 3°/s / Free** (Free: the longer the stick is held at full deflection, the higher the rate). No more "teleport" buttons (+200 ft, Left 300 m): the **initial offsets** go into New aircraft (lateral m +R/−L, vertical ft +above/−below GP). Vertical controls left of the stick: **▲ −100 ft/min** (less descent), **Optimal GP** (back to the glide path rate, offset kept), **▼ +100 ft/min** (more descent); step preset (100 ft/min), **aligned on the best VS**: the steps go through the best VS of the GP in use (e.g. best −740, from −990: −940, −840, −740, −640…), so the buttons always reach it exactly. ACTUAL VS follows every command (stick, buttons, Optimal GP). Under the stick: **Final CRS** (turns back to the final course at the selected rate, Free → 3°/s; offset kept). Aircraft panel: name, distance, speed, SSR. **Control panel (fourth mockup): readout strip at its top (dark, amber digits), only three values: BEST VS for the GP angle in use (ft/min, depends on speed and angle), ACTUAL VS (ft/min), actual TURN rate (°/s L/R, useful in Free)**. Offsets from centreline / GP are not shown there (they are on the labels). Footer: On GP / CL, Auto, Pause, Remove, Remove all; Aurora-like data.

## Done, to be tested

Last updates, newest first. When an item works in the test, delete it.

- **Test traffic panel:** turn rate keys over the stick and ◀ L / Final CRS / R ▶ under it, as wide as the stick; vertical keys renamed − / = / + rate of desc. (reduce / resume normal / increase rate of descent).
- **Test traffic turn keys** ◀ L / R ▶ beside Final CRS: held = stick at its edge, a click = at least 1 s of turn.
- **Test traffic joystick as a control stick:** held, it changes heading and climb/descent; released, they stay (no automatic return). New quick button *Final TRK + GP* (final track and GP rate, offset kept). Check: turn off the centreline and release, the aircraft keeps drifting.
- **Test traffic** (key T / Settings): Add, Auto, joystick, quick buttons, Aurora-like data, TEST TRAFFIC sign; removed when the window closes. Check: without Aurora, with Aurora (mixed with real traffic), runway change, analog, coasting with the joystick out of the beam.
- **Lock display mode** per profile (Settings → Display): no mode button (analog: blank key), key A shows "DISPLAY MODE LOCKED".
- **Fix: each view has its own beam.** A track leaving the elevation beam disappears (or coasts) only in the elevation view, and the same for azimuth. Check: tilt one beam away from a track, the other view keeps it.
- **Analog uses the line width and dash style** set in Colours & lines (only the colour is the phosphor). Check: scan limits at 1 px on the analog scope.
- **Profile keys:** P = next profile, Shift+P = previous (in a circle), name shown for 2 s; one profile: "ONLY ONE PROFILE". Check: not active while typing in ICAO / GP / APT, mode change when the profiles differ.
- **Show / hide each line** (Display style → Colours & lines, columns Modern and Analog; not the range marks). Check: hide the approach limits on the analog scope only; altitude scale hidden also hides its values.
- **Analog echo:** brightens as the beam arrives (rise time about a tenth of the sweep), brightest at the centre, then fades; same for the dots.
- **Tracks moved at every screen frame** (Modern): smoothed and coasting tracks glide. Check: smoothness, CPU use with many aircraft, dragging labels.
- **Track smoothing** (Modern; Off / Light / Strong, default Light): smooth track and steadier label values, history dots at the real positions; coasting starts from the smoothed position. Check: no lag felt on final with Light, turns with Strong, a new aircraft (no jump), landing.
- **Antenna beam on/off:** new profile (first start) = beam off, scan limits −1/+10°, ±15°; tilt controls show a hint. Turning the beam on sets it 4°/6° narrower than the scan limits. Existing profiles: beam off too, scan limits back to the old ones (v1 values; profiles of the first beam builds back to the old sector, or the new defaults when it cannot be found). **Automatic elevation centre** (= GP angle in use), on by default. Check: first start without settings file, hint on knobs / buttons / arrow keys, turning on, auto centre when changing approach.
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

- **Real joystick / game controller for the test traffic (and later the Instructor).** Any USB joystick, gamepad or throttle seen by Windows (Windows.Gaming.Input RawGameController, no driver needed): X axis = turn, Y axis = vertical speed (same logic as the virtual stick, with a dead zone and an invert option), buttons assignable to L / R / UP / DN / Final CRS / − = + rate of descent / next aircraft. Settings page with "move the axis / press the button to assign" and a live test. The virtual stick moves with the real one (visual confirmation). Could also serve the "External controls" (keypad knobs) later.

- **AuroraPAR Instructor (training simulator) — big project in phases, plan with Opus when the current tests are closed (decided).**
  - **Where:** an Instructor mode inside AuroraPAR (one program, one download; it reuses the radar views, the test traffic and the coordination relay). The simulation (flight model, scenarios, link) in its own classes, separate from the radar, so it can grow (or become a separate program) later.
  - **Flight model:** each aircraft has commanded altitude, speed, heading, turn rate (°/s) and rate of descent/climb (ft/min) and reaches them gradually. Values typed, or with +/− buttons, preset values (e.g. heading: runway, ±5°, ±10°; rate 500/700/1000 ft/min), mouse wheel over the field. Controls in the instructor's labels, plus a larger panel for the selected aircraft.
  - **Start positions:** final, base, downwind, 45° right of final... each = angle from the final (0 final, 90 abeam, 180 downwind), side, distance, altitude, initial heading, speed; the instructor edits, adds, deletes them; saved.
  - **Instructor plan view:** top-down view (runway, range rings, aircraft with tails, PAR sector), to see and steer the aircraft outside the PAR coverage (downwind, base: the student does not see them until they enter the beam — realistic).
  - **Link instructor → student:** only the instructor computes the flight; positions sent to the student's AuroraPAR, which shows them as normal traffic. Same PC, different PCs, different networks: via the relay of the coordination panel; **session code** generated by the instructor and typed by the student (only that student receives it, nobody else can inject traffic).
  - **IVAO:** the simulated traffic is never on the IVAO network (it is not allowed and not possible); on the student's AuroraPAR it is added to the real IVAO traffic. **"SIM" marker** on the simulated tracks only when the student is connected to IVAO (mixed with real traffic); in a closed environment (no IVAO) no marker.
  - **Phases:** (1) flight model + instructor controls + start positions + plan view on one PC; (2) network link with session code; (3) optional: scenarios saved/loaded, lesson tools.

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
