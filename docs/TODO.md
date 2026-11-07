# To-do

## Checking against the real unit

Behaviour still to confirm with people who ran the real OCU II. Items ticked off were settled from
typical railroad settings, marked **[typical]** in the manual; corrections from the real unit are
still welcome. The rest use a best guess, marked **[guess]**.

- [x] **Coast and Coast B:** no power; Coast B adds about 15 psi of locomotive brake, and above
  2 mph both brake the train down to 2 mph. Used on shoves to stretch the slack.
- [x] **Speeds:** Couple about 1 mph, then 4, 7, 10 and 15 mph.
- [ ] **Stop on a moving train:** how hard and how quickly it brakes.
- [ ] **Speed control feel:** how quickly it picks up and settles at the selected speed.
- [x] **RSC timer:** warning at 50 seconds and penalty 10 seconds later; only RSC resets it once
  the warning sounds; 5 seconds to select a speed after RSC; holding RSC sands.
- [x] **Dragging brakes and direction changes while moving:** a warning, then a service penalty
  after 30 seconds; moving DIR while moving is a full service penalty.
- [x] **Tilt timing:** alarm after a second tilted, emergency 3.75 seconds later (the alarm's
  length measured on a real unit).
- [ ] **Emergency recovery:** how long the wait is before recovering.
- [ ] **Display wording:** typical messages are in; the OCU II's own wording on its four-character
  display is still unknown.
- [x] **Beeps and alarms:** a real unit only sounds its steady alarm (RSC and tilt) and a short beep
  for the tilt time-out. It's silent otherwise, including for penalties.
- [x] **Lamp colours:** from a real unit. SPEED Stop red, Coast B and Coast yellow, Couple and up
  green; DIR N red, Rev yellow, Fwd green; IND Emerg red, Rel green, the rest yellow; AUTO Charge
  red, Rel green, the rest yellow.
- [x] **Bell/Horn toggle:** Off and Bell latch; Horn is momentary and rings the bell too.
- [x] **Brake positions:** IND Low, Med and Full at 15, 30 and 45 psi; AUTO Min, Light, Med and
  Full at 7, 12, 18 and 26 psi.
- [ ] **AUTO paddle:** which way it tips for apply and release.
- [x] **Headlights:** both ends dim at power-up; bright lights the leading end and follows the
  direction, with the other end dim.

## Features

- [ ] Bring back tilt protection and the Time (tilt time-out) button. The timings are settled.
- [x] Use real talker recordings (by Christian Currao, SketchyBlueFlag).
- [ ] Record the talker messages that are still silent (listed in the manual, section 6).
- [x] Permission to include the talker recordings in public downloads (Christian Currao, November
  2026). They're used with permission and aren't covered by the GPL.
- [ ] Build the confirmed speeds into the program instead of leaving them as settings.
- [x] Refresh the photo skin: new photos of the owner's unit, with every control photographed in
  each position.

## Releasing

- [x] Settle on one name for the program: **Run8 OCU II**.
- [x] Use the name everywhere: the program file, the window, the About box and the manual.
- [x] Publish the source code here under GPL-3.0 (v2.0-beta).
- [x] Put downloads on GitHub Releases (v2.0-beta).
- [ ] Apply to the SignPath Foundation for free code signing, so Windows stops warning about the
  download.
