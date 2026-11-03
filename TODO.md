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
- [x] **Tilt timing:** alarm after a second tilted, emergency 5 seconds later.
- [ ] **Emergency recovery:** how long the wait is before recovering.
- [ ] **Display wording:** typical messages are in; the OCU II's own wording on its four-character
  display is still unknown.
- [ ] **Beeps and alarms:** what they sound like.
- [x] **Bell/Horn toggle:** Off and Bell latch; Horn is momentary and rings the bell too.
- [x] **Brake positions:** IND Low, Med and Full at 15, 30 and 45 psi; AUTO Min, Light, Med and
  Full at 7, 12, 18 and 26 psi.
- [ ] **AUTO paddle:** which way it tips for apply and release.
- [x] **Headlights:** both ends dim at power-up; bright lights the leading end and follows the
  direction, with the other end dim.

## Features

- [ ] Bring back tilt protection and the Time (tilt time-out) button. The timings are settled.
- [ ] Replace the stand-in talker voice with the real unit's voice.
- [ ] Build the confirmed speeds into the program instead of leaving them as settings.
- [ ] Refresh the photo skin with cleaned-up photos of the switch bases.

## Releasing

- [x] Settle on one name for the program: **Run8 OCU II**.
- [ ] Use the name everywhere: the program file, the About box and the manual still say "Run8
  Beltpack OCU" or "Run8 RCL Controller".
- [ ] Publish the source code here under GPL-3.0.
- [ ] Put downloads on GitHub Releases.
- [ ] Apply to the SignPath Foundation for free code signing, so Windows stops warning about the
  download.
