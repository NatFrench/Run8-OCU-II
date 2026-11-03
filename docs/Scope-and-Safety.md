# Scope and safety

Run8 Beltpack OCU is a hobby simulator. It recreates the look and operation of the CANAC/Cattron
Beltpack OCU II remote control unit for use with Run8 Train Simulator V3, and nothing else.

## What it is

- A Windows program that controls a simulated locomotive in Run8 Train Simulator V3.
- It talks only to Run8, on the same PC (the local loopback address, 127.0.0.1). It makes no
  connection to any other computer and has no radio, Bluetooth or hardware output of any kind.
- It reproduces how the unit is operated: its controls, display, sounds and timers, and the
  operating procedures found in published material.

## What it is not

- It is not a tool for controlling real locomotives or communicating with real remote control
  equipment. It can't do that, and it will not be made to.
- It is not railroad training. Real remote control operation must follow your railroad's rules,
  training and qualification.
- It is not affiliated with or endorsed by Cattron, CANAC, any railroad, or the makers of Run8.
  Product names are trademarks of their respective owners and are used only to identify the
  equipment being simulated.

## Commitments

- **Simulation only.** The project will never contain code that transmits over radio, drives radio
  hardware, or communicates with real locomotive or remote control equipment.
- **Behaviour, not protocol.** Where the emulator is based on study of real hardware, the project
  publishes only what an operator sees, hears and does: display wording, lights, sounds, timings
  and what each control does. It does not publish radio protocols, message formats, addressing or
  pairing details, firmware, or radio recordings.
- **No transmitting.** No part of this project involves transmitting on railroad radio frequencies,
  including during research on real hardware.
- **Not a reverse-engineering guide.** The project is not, and won't become, a guide to reverse
  engineering remote control locomotive systems. It doesn't document how real equipment
  communicates: its radio protocols, commands, telemetry, addressing or pairing. (The locomotive
  data it uses comes from Run8's own published interface, not from real equipment.)

## Contributions

Issues or pull requests that add any of the excluded material (radio or protocol details, transmit
capability, firmware) will be closed and the material removed.

## Reporting a concern

If you find anything in this project that you think could help someone interfere with real
railroad equipment, please report it privately rather than opening a public issue: on the
repository's **Security** tab, choose **Report a vulnerability**. Only the maintainer sees these
reports.
