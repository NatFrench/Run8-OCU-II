# Changelog

## v2.0-beta (7 November 2026, first public beta)

The first public release, with the source code published under GPL-3.0.

- The program is now called **Run8 OCU II** everywhere: `Run8 OCU II.exe`, the window, the About
  box, the download and the manual. Settings stay in `%LocalAppData%\Run8RclController`, so they
  carry over from the tester builds.
- New faceplate photo: the owner's unit, cleaned up, seen from above as the operator looks down at
  it, straightened so the top of the face is level, and with the glare on the face evened out. No
  D-rings.
- The paddles, toggles and side knobs are now photos of the real ones, one for every position,
  instead of drawn models. Each change of position snaps straight over, and a knob clicks through
  every detent on the way to a distant one.
- A stub of the whip antenna stands on the jack (right-click menu: Antenna to hide it). It gives
  way to the emulator button strip, and hovering over it brings the strip up like the mount does.
- The Bell/Horn toggle sits toward the operator for Off, in the centre for Bell and away for
  Bell/Horn, as on the real unit. Before: upright for Off, leaning away for Bell and further for
  Bell/Horn.
- The labelled picture in the manual and the program icon show the new photo. The manual's picture
  labels both top corner buttons as RSC.
- The download includes `THIRD-PARTY-NOTICES.txt` for NAudio and the .NET runtime.

## v1.2-beta5 (6 November 2026, tester build)

### From a real OCU II on the bench

- Lamp colours as lit on the real unit. Before: IND Full and AUTO Full red, AUTO Charge green, DIR
  Rev green.
    - IND: Emerg red, Full, Med and Low yellow, Rel green.
    - AUTO: Charge red, Full, Med, Light and Min yellow, Rel green.
    - DIR: N red, Rev yellow, Fwd green. SPEED unchanged.
- Each lit lamp also tints a character or two in the middle of its printed word in the same
  colour, from the small LED behind each word on the real unit.
- Flashing lamps are lit for 0.4 seconds once a second. Before: 0.3 seconds twice a second.
- Power-up: every lamp and both icons light for 1.4 seconds with the display dark, then every dot
  of the display for 1.2 seconds. Before: 2 seconds of lamps with `8.8.8.8.`.
- The OCU always powers up with AUTO at Full, flashing from the end of the lamp test, whatever
  AUTO was left at. Recovering the power-up penalty is now RSC, then AUTO to Rel.
- Both top corner buttons are RSC. They aren't labelled on the real unit; one for each hand is a
  guess. PITCH is on the back of the real unit, so it's now on the emulator strip.
- The OCU is silent apart from its steady alarm (RSC, tilt) and the tilt time-out beep. Removed
  the stand-in sounds: the falling three-tone for penalties, the low double tone for losing the
  link, and the double beep for the PCS reset, dragging brakes and traction motor stall. The
  display and the talker still give those messages.
- Beeper at 3,640 Hz (was 3,620), short beep 0.27 s (was 0.25).
- Time works again: held for about 2 seconds with the train stopped, it gives the short beep,
  `TIME` on the display and the talker message, with or without a link to Run8, as on the real
  unit. Before: Time did nothing.
- Tilt (still switched off): the alarm sounds for 3.75 seconds before the emergency (was 5), as
  measured.
- The sun key on the face dims the lamps, icons and display: Full, Medium and Low, as on the real
  unit. Each click steps down, Low goes back to Full, and the OCU powers up at Full (the order and
  power-up setting are a guess). A `Brightness` key can be bound in the settings file.
- The operating settings reset to the new defaults once.

## v1.2-beta4 (4 November 2026, tester build)

### Speed control

- Picks up speed more gently and eases off power before the selected speed. Before: a light
  engine could gain 2.5 mph a second, overshoot 4 mph to 5 mph, brake hard and go round again.
  The program now works out the acceleration from the speed, because Run8's own figure lags by
  over a second and tops out at 100 mph per minute.
- Loads up one notch at a time towards a fixed ramp of about half a mph per second. A notch, 2
  seconds to see what it does, another only if the train is picking up at under a quarter of a mph
  per second, and one off only if it's over 1 mph per second. Otherwise the notch is held. Power
  comes off as the train arrives at the selected speed and goes back on only if the train drops
  half a mph below it or is clearly slowing. Before: the throttle went up and down while
  accelerating, cutting out from 2 mph and coming back every 2 seconds, which you could hear.
- The first notch comes on as soon as the brakes have released; later notches wait 2 seconds each.
- The locomotive brake is applied a step at a time the same way. When the train runs over half a
  mph above the selected speed, or keeps picking up with the power off going downhill, a 4 psi
  step goes on. Another follows about every second until the train is slowing, and a step comes
  off if it slows faster than 0.6 mph a second. Back at the selected speed the brake comes off a
  step at a time, keeping enough on to hold the train on a grade, up to full locomotive brake.
  Coast and Coast B work the same way above 2 mph. Before: the brake built up to about 50 psi in
  one go, which could take a light engine from 4 mph to a stand in Coast.
- Speed control doesn't use the train brake.
- New settings `BrakeStepPsi` (4), `BrakeStepUpSeconds` (1), `BrakeStepDownSeconds` (1.5),
  `BrakeArrivalStepSeconds` (1), `BrakeTooSlowMphPerSecond` (0.1) and `BrakeTooFastMphPerSecond`
  (0.6). Saved speed-control settings reset to the new defaults once, keeping the locomotive,
  railroad, OCU letter, channel and port.
- `events.log` now records the SPEED position.

### Talker recordings

- The talker now plays recordings by Christian Currao (SketchyBlueFlag), loaded from
  `talker.pak` beside the program. Before: a Windows speech voice for everything.
- Messages, or parts of them, that haven't been recorded are silent and noted in `events.log`.
  The manual lists them.
- Without `talker.pak`, the program says so at startup, offering not to remind you again (setting
  `WarnIfTalkerMissing`), and the Windows voice reads every message as before.
- The talker is much quieter: its default is about a quarter of the old level. Right-click,
  **Talker volume** offers 100%, 75%, 50% (the default) and 25%, and remembers the choice.
  `TalkerVolume` in the settings file takes the same steps; an old value goes back to 50.
- Right-click, **Railroad** picks the railroad spoken before the unit number in status reports:
  None, or one of the five with a recording (BNSF, CSX, NS, UP, RER). Before: only by typing it
  into the settings file. The choice is remembered.
- The About box and the manual credit him. His recordings aren't covered by the GPL.

### From videos of operators using the real OCU II

- The RSC timer's 50 seconds, and moving any control resetting it before the alarm, are confirmed
  by an operator in a video. No change to the program.
- **Automatic bell (new):** the bell rings for 2 seconds as soon as a stopped train is told to move
  (Couple or above selected, with nothing blocking power), before it starts rolling. New setting
  `AutoBellSeconds` (0 turns it off).
- **RSC and PITCH swapped corners:** RSC is now the top right button, above SPEED, and PITCH the top
  left. Before: RSC top left. The keyboard keys are unchanged (Num 5 for RSC, Num / for PITCH).
- Moving a control or pressing a button no longer beeps; the real unit is silent.
- The RSC warning and the tilt alarm are now the same steady tone. Before: RSC pulsed on and off,
  and tilt warbled between two pitches.
- The beeper's pitch is now about 3.6 kHz, matched to a recording from a video of the real unit
  (was 2.7 kHz). The tilt time-out's beep lasts a quarter of a second.
- Lamps are coloured by position. Before: all red.
    - SPEED: Stop red, Coast B and Coast yellow, Couple to Max green.
    - DIR: N red, Fwd and Rev green.
    - IND and AUTO (a guess following the same pattern): Rel and Charge green, Full and Emerg red,
      the positions between yellow.
- Tilt time-out (still switched off) will give a short beep when accepted, not a double beep.

### Emulator strip

- The SAND button is gone from the strip above the unit; hold RSC for 2 seconds to sand, as on the
  real unit. Sand is still applied automatically during wheel slip, and the Scroll Lock key still
  sands while held.

### Crashes and logs

- `startup.log` records each step of starting up as it happens, so the last line shows how far the
  program got if it dies. A crash the program can catch shows a message and writes `crash.log` with
  the error, the startup steps and the end of `events.log`. Both are in the settings folder.
- Diagnostic packages (`-diag-gpu`, `-diag-software`) for tracking down crashes on start; the
  software one draws without the graphics card and writes `startup-software.log` and
  `crash-software.log`.
- Closing the program no longer hits an error writing to the closed event log, and a normal stop
  of the talker's sound is no longer logged as a failure.

## v1.2-beta3 (3 November 2026, tester build)

Everything changed since v1.2-beta2. Values marked **[typical]** in the manual are typical railroad
settings, still to be confirmed on the real OCU II.

### Speed selector

- **Coast B:** no power and a light locomotive brake (about 15 psi), so the train gradually stops.
  Above 2 mph it brakes harder to bring the train down to 2 mph. Before: no power, with the
  locomotive brake only stopping the train from speeding up past where Coast B was selected.
- **Coast:** still no power and no brake, but above 2 mph the locomotive brake now brings the train
  down to 2 mph. Before: rolled freely at any speed.
- Speeds unchanged (Couple about 1 mph, then 4, 7, 10 and 15 mph), now marked as typical settings
  instead of guesses.

### Brakes

- **IND positions** are now brake cylinder pressures: Low 15, Med 30, Full 45 psi. The emulator
  adjusts the Run8 lever until the brake cylinder reading matches, so it works across locomotives.
  Before: fixed lever positions (about 27%, 59% and 100%), with Full at Run8's maximum of 71 psi.
- **AUTO** Light is now 12 psi and Med 18 psi (were 10 and 15). Min 7 and Full 26 unchanged.
- **Dragging brakes (new):** SPEED at 4 or higher with IND at Med or Full, or AUTO at Light or
  more, gives a double beep, a talker message and `IND BRAKES DRAGGING` or `AUTO BRAKES DRAGGING`.
  After 30 seconds without backing off, a service penalty applies. Recover by selecting Stop.

### Safety systems

- **Direction change while moving (new):** moving DIR while the train is moving now gives a full
  service (train brake) penalty and `INVALID DIRECTION CHANGE`. Before: the change was simply held
  until the train stopped.
- **RSC:**
    - 5 seconds to select a speed after pressing RSC (was 3). Recovery steps keep 3 seconds.
    - Once the RSC alarm is sounding, only RSC resets it. Other controls still reset the timer
      before the alarm.
    - Holding RSC for 2 seconds sands, for as long as it's held (new).
- **Tilt** (still switched off): ready for when it returns, with the alarm after 1 second tilted,
  a 5-second countdown to the emergency, and Time only accepted with the train stopped.

### Display and talker

- Penalties now show `RECOVER EMG BRK`, `RECOVER FS BRK` and `RECOVER SRVC BRK` (were `EMG PEN`,
  `TBK PEN` and `SVC PEN`).
- The RSC alarm counts down `RSC9` to `RSC0` (was `RSC`).
- Recovery messages: `SERVICE BRAKE RECOVERY COMPLETE`, and once the brakes have recharged,
  `FULL SERVICE BRAKE RECOVERY COMPLETE` or `EMERGENCY BRAKE RECOVERY COMPLETE`.
- New messages: `SPEED SELECT WITHOUT RSC`, `INVALID DIRECTION CHANGE`, `IND BRAKES DRAGGING`,
  `AUTO BRAKES DRAGGING`, `WHEEL SLIP`, `HDLT BRIGHT`, `HDLT DIM`, and `CAUTION TRAC MTR STALL`
  (Couple selected for 20 seconds without the train moving).
- New talker messages: "Independent brakes dragging" and "Train brakes dragging".
- Talker wording matched to a real unit's phrases:
    - Penalty causes "Operator requested emergency" (was "Emergency applied from OCU"),
      "Illegal reverser change" and "Communication interrupted" (was "Communication loss").
    - "Locomotive in remote mode" on changeover to BELTPACK (was "Changeover to beltpack").
    - New: "Brake pipe fully charged" (AUTO in Charge), "Severe wheel slip" (slipping for 3
      seconds) and "Locomotive movement failure" (with the traction motor stall warning).
    - Status messages read the unit number a digit at a time, after the railroad's initials if
      the new `Railroad` setting is filled in (for example "B N S F 4 0 2 6").
    - Tilt, when it returns, says "Tilt timeout from A" and "Operator A down".
- "Low brake pipe pressure": below 55 psi, power is blocked and Status says so.

### Faceplate

- The AUTO paddle now tips away from the operator when applying and toward them when releasing.

### Settings and logging

- Settings files from older versions get the new operating defaults once, keeping the locomotive
  number, OCU letter, channel, keys, window position and sound settings. Before: a saved file kept
  old values forever, so new defaults never reached existing installs.
- New settings: `IndLowPsi`, `IndMedPsi`, `IndFullPsi`, `CoastBPsi`, `CoastLimitMph`,
  `SpeedSelectWindowSeconds`, `DraggingBrakeSeconds` and a few more. `IndLow`, `IndMed`, `IndFull`
  are gone.
- `events.log` is capped at 5 MB; the previous log is kept as `events.old.log`.

### About box and licence

- The About box links to the project page (github.com/NatFrench/Run8-OCU-II) and states the GNU
  GPL v3.0 licence. The program and its art are licensed under GPL-3.0.

### Package and documents

- The manual and START HERE ship as PDFs with bookmarks, made from the Markdown.
- START HERE explains the browser's "not commonly downloaded" warning, how to unblock the download,
  and what's new in this build.
- Things to verify: Coast B findings from a BNSF operator, updated "Emulator now" lines, and a new
  item on dragging brakes and direction changes.
- One packaging script builds both the public release (with the licence) and the tester package.
- The manual adds a **[typical]** marker, a dragging-brakes section, and updated display, penalty,
  settings and differences tables.

### Project (not in the program)

- Name settled: **Run8 OCU II**. The public documentation repository is
  github.com/NatFrench/Run8-OCU-II, with issue forms for feedback.
- Scope and safety statement: simulation only, no radio, and not a guide to reverse engineering
  remote control locomotive systems.
