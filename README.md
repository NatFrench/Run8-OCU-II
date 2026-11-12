# Run8 OCU II

A desktop emulation of the CANAC / Cattron Beltpack OCU II remote control unit for
[Run8 Train Simulator V3](https://www.run8studios.com/). It drives a Run8 locomotive the way a yard
crew would with the real beltpack: speed selector, direction, train and locomotive brakes, the RSC
vigilance timer, penalty brake applications and the recovery procedures, the talker, and the
self-tests.

![The emulated OCU II with its controls labelled](docs/images/ocu-labelled.png)

> **Status:** public beta (v2.0-beta), after play testing with people who ran the real unit. Things
> may still change. Where the real unit's behaviour isn't documented, the emulation makes a best
> guess; those places are marked **[guess]** in the manual.

## Feedback

Ran the real OCU II? Your knowledge is the most valuable thing this project can get.

- **Something differs from the real unit, or you can fill a gap in the to-do list:** open an issue
  and choose [Compare with the real unit](https://github.com/NatFrench/Run8-OCU-II/issues/new/choose).
- **Bugs and ideas:** open an [issue](https://github.com/NatFrench/Run8-OCU-II/issues/new/choose).
- **Questions, chat and stories about the real unit:**
  [Discussions](https://github.com/NatFrench/Run8-OCU-II/discussions).
- **To talk privately:** message me on Discord, **n_french**.

Please keep feedback to what an operator sees, hears and does. Don't post or send radio, protocol,
addressing or pairing details of real equipment.

## Scope and safety

This is a simulator for Run8 only. It talks to Run8 on the same PC and nothing else: it has no
radio, no connection to other computers, and no way to communicate with real locomotive or remote
control equipment.

This project is not, and won't become, a guide to reverse engineering remote control locomotive
systems. It doesn't document how real equipment communicates: no radio protocols, commands,
telemetry, addressing or pairing details. Read [Scope and safety](docs/Scope-and-Safety.md) for what
the project will and won't contain.

## Getting started

**Needs:** Windows 10 or 11 (64-bit) and Run8 V3. The download needs nothing else installed.

1. Download the latest `Run8-OCU-II-….zip` from
   [Releases](https://github.com/NatFrench/Run8-OCU-II/releases).
2. **Unblock it.** The program isn't signed by a publisher yet, so Windows may warn about it.
   Right-click the zip, choose **Properties**, tick **Unblock** at the bottom of the General tab, and
   click **OK**. Then unzip it anywhere.
    - Your browser may say the file "isn't commonly downloaded". That's because it's a new program
      with few downloads, not because anything harmful was found. Choose **Keep**.
    - If Windows says "Windows protected your PC" when you run it, click **More info**, then
      **Run anyway**.
3. In Run8, open **Controls**, find **Custom Input Device**, set the port to **18888** and enable it.
   Close any other Custom Input Device tool first.
4. Run `Run8 OCU II.exe`. The beltpack appears as a small window on top of Run8.
5. Hover over the top of the antenna mount to show the button strip, and click **CAB: MANUAL** so it
   reads **CAB: BELTPACK**. The beltpack now has the locomotive.

The [operator's manual](docs/OCU-II-Manual.md) (also included as a PDF in the download) covers every
control, the procedures, keyboard bindings and settings. Section 7 is the place to start.

## Building from source

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
dotnet build
dotnet test
```

The overlay builds to `src\Beltpack.Overlay\bin\Debug\net10.0-windows\Beltpack.Overlay.exe`.

- `tools\publish.ps1` installs a Release build with desktop and Start menu shortcuts.
- `tools\package.ps1` builds the release zip (needs Python with `markdown`, and Microsoft Edge to
  make the manual PDF).
- `tools\fake_run8.py` stands in for Run8 when testing without the simulator.
- `tools\make_skin.py` builds the photo skin from the author's photos of the real unit, which aren't
  in the repository; the finished skin is, in `src\Beltpack.Overlay\Skin`.
- `tools\make_labels.py` draws the labelled picture above from the overlay's own render.

## Privacy

The program doesn't collect or send any information. Its only network traffic is to Run8 on the same
PC (127.0.0.1). Its settings and an event log for troubleshooting stay in
`%LocalAppData%\Run8RclController`.

## Licence

Copyright © 2026 Nathan French.

The code and the artwork (the unit skin, icon and diagrams, made from the author's photos of a real
OCU II) are licensed under the [GNU General Public License v3.0](LICENSE). You may use, change and
share them, but anything you distribute that's based on this project must also be released under the
GPL with its source code.

The program includes NAudio and the .NET runtime, both under the MIT licence; see
[THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt), which is also in the download.

The talker recordings in release downloads (`talker.pak`, beside the program) are by Christian Currao
(SketchyBlueFlag) and are used with his permission. They aren't covered by the GPL and aren't in this
repository; please don't reuse them outside this project without asking him.

This project isn't affiliated with or endorsed by Cattron, CANAC, any railroad, or Run8 Studios.
Product names are trademarks of their respective owners and are used only to identify the equipment
being simulated.

## Credits

Designed, photographed and tested by Nathan French, and written together with Cursor's AI coding
agent. Operating behaviour follows the CANAC RCLS Job Aid, with feedback from people who have run the
real unit.

Talker audio: Christian Currao (SketchyBlueFlag).
