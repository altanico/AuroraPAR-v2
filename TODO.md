# AuroraPAR v2 — To do

## Next update

- **Azimuth tilt: option to swap left and right.** Today left/right is as seen by the pilot (relative to the traffic). Depending on where the controller "looks" it can be more intuitive to have right = the side where the controller looks. Setting in the profile (default: as now), applied to the AZ TILT knob, the keyboard arrows, the readout (L/R on the console panel and on the screen) and, later, the external knob.

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

## Maintenance

- GitHub Actions: update the actions (Node 20 is deprecated).
- Delete the `phase1` test branch on GitHub.
- Before the pull request to bornac1: check upstream changes, prepare the README.
