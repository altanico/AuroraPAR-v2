# AuroraPAR v2 — To do

## Next update

- (nothing pending)

## To verify with live traffic

- Labels, colours (plots / tracks inside and outside the limits), history dots at 2–3 s, only inside the scan.
- Antenna tilt on both sides, runway on the left and on the right.
- QFE and final course (CRS) against the published values.
- Analog mode: echoes lit by the beam, console panel, knobs, BRT.
- Fractions `1 1/4NM` and text size; BRT 110–150% (Modern and Analog); visual colour picker (live preview, Cancel, recent colours, also in AuroraCoord).
- Range marks and reminders at each range.
- Coordination panel (AuroraPAR + AuroraCoord, radar / tower / monitor): automatic pairing from the callsign (`#CONN` answer), relay reachable (port 8883), alert sound.

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

## Maintenance

- GitHub Actions: update the actions (Node 20 is deprecated).
- Delete the `phase1` test branch on GitHub.
- Before the pull request to bornac1: check upstream changes, prepare the README.
