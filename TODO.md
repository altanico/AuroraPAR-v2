# AuroraPAR v2 — To do

## Next update

- **Distance text with fractions is hard to read** (*Display style → Range marks → Fractions*): the ¼ ½ ¾ characters are too small on the radar. Proposal: fractions with normal digits (`1 1/4NM`, `3/4NM`) and an adjustable size of the distance text (e.g. 10–16 px).

## To verify with live traffic

- Labels, colours (plots / tracks inside and outside the limits), history dots at 2–3 s, only inside the scan.
- Antenna tilt on both sides, runway on the left and on the right.
- QFE and final course (CRS) against the published values.
- Analog mode: echoes lit by the beam, console panel, knobs, BRT.
- Range marks and reminders at each range.
- Coordination panel (AuroraPAR + AuroraCoord, radar / tower / monitor): automatic pairing from the callsign (`#CONN` answer), relay reachable (port 8883), alert sound.

## Decisions pending

- Smoothed altitude in the elevation view (to hide the steps of the real altitude updates)?
- Runway reminders following the runway when it is renamed in the editor?

## Later

- Shout line (always-open intercom) in the coordination panel.
- Event log for the instructor (monitor), with saving to a file.
- Joystick and USB knob (when a device is available).
- Range marks in km, tablet view, Mac/Linux version (Avalonia).
- Coordination panel: button gently pulsing when the selected traffic reaches a reminder distance.
- Translations of the user manual.

## Maintenance

- GitHub Actions: update the actions (Node 20 is deprecated).
- Delete the `phase1` test branch on GitHub.
- Before the pull request to bornac1: check upstream changes, prepare the README.
