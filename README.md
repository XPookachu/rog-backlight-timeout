# ROG Backlight Timeout

Keyboard backlight timeout for **ASUS ROG laptops** that reacts to the **built-in keyboard and touchpad only**.
Typing on an external keyboard or moving an external mouse no longer keeps (or turns) the backlight on.

It is a companion to [G-Helper](https://github.com/seerge/g-helper), not a replacement. G-Helper's own timeout uses
Windows' system-wide idle timer, so any input counts (maintainer's explanation:
[discussion #1210](https://github.com/seerge/g-helper/discussions/1210)). This tool leaves G-Helper in charge of
brightness and lighting effects and only adds the "who counts as activity" part.

## Use

Download the zip from [Releases](../../releases), extract it and double-click `RogBacklightTimeout.exe`. That's the whole program: no runtime
or installer needed. Choose **Install (start at logon)** once (it asks for administrator approval); after that the
background agent starts with Windows and you only open the exe to change settings.

```
  ROG Backlight Timeout                                   v1.0.5
  Keyboard backlight timeout for ROG laptops · built-in input only

  ──────────────────────────────────────────────────────────────
  Agent       ● Running
  Autostart   ● Installed · starts at logon
  Timeout     ● On · 30 seconds
  Brightness  ● Level 2 · plugged in · from G-Helper
  Laptop      ● ROG Strix G16 G614PR_G614PR · supported
  ──────────────────────────────────────────────────────────────
  › 1  Turn timeout off
    2  Set timeout…
    3  Stop agent
    4  Reinstall / update
    5  Uninstall
    6  Input devices
    7  Open log
    8  Quit
```

The exe is unsigned, so SmartScreen may warn. In G-Helper, make sure its own keyboard timeout is **Off**, otherwise the
two fight (the menu warns if it is on). To update, run the new exe and choose *Reinstall / update*.

## How it works

```
Raw Input (keyboard, touchpad digitizer, the keyboard's Fn-key collections; header only)
        |  which device? -> Plug and Play facts -> built-in or external?
        v
IdleController (Active / Dimmed)  --timeout-->  brightness 0
        ^  built-in input / resume / lid / AC switch
        '--- restores the level G-Helper is configured for (AC or battery)
```

- **One executable, three roles.** Started normally it opens the menu in its own console window. Started with
  `--agent` (by its logon task) it is the windowless background agent, so nothing ever flashes on screen. Install
  copies the exe to `%ProgramFiles%\RogBacklightTimeout` and registers the task.
- **Built-in detection is automatic.** Windows puts every non-removable device that belongs to the computer in the
  well-known *local machine container* (`{00000000-0000-0000-FFFF-FFFFFFFFFFFF}`); removable peripherals get their own
  container. A device only counts as built-in if its parent chain also reaches the platform's own ACPI/PCI hardware
  without passing through anything virtual or Bluetooth. Sources are in
  [`DeviceClassifier.cs`](src/RogBacklightTimeout.Core/Input/DeviceClassifier.cs). The *Input devices* screen shows the
  verdict for every keyboard and touchpad. If no keyboard looks built-in (unusual firmware), all hardware input counts
  instead of nothing.
- **Fn keys count too.** One physical keyboard exposes several Raw Input collections: typing arrives on the standard one,
  but the Fn row (power profile, screen brightness, ROG key, media keys) is reported by the keyboard's firmware on its
  consumer-control and ASUS vendor-defined (usage page `0xFF31`) collections. The agent listens to those collections of the built-in
  keyboard as well, so no key needs to be listed. Rule and tests in
  [`InputSources.cs`](src/RogBacklightTimeout.Core/Input/InputSources.cs).
- **Brightness uses G-Helper's own mechanism**: the HID feature report `5A BA C5 C4 <level>` sent to the ASUS Aura
  keyboard controller (see G-Helper `app/USB/AsusHid.cs` and `app/USB/Aura.cs`; the Linux `hid-asus` driver sends the
  same report). Only brightness changes, so your lighting mode and colours keep running and are simply hidden while
  dimmed.
- **Restore level** is read from G-Helper's `config.json` (`keyboard_brightness` while plugged in,
  `keyboard_brightness_ac` while on battery - G-Helper's naming is inverted), so Fn brightness changes just work.
  Changes G-Helper makes behind the agent's back (Fn keys, lid, AC switch, resume) are noticed and the agent re-syncs.
- **Privacy**: only the Raw Input *header* is read. The agent never sees which key was pressed, any report contents or any touch data and
  logs only state changes and device paths.
- **Safety**: it writes only on an ASUS computer, only on models G-Helper also drives through this HID report (not
  ROG Ally, TUF, Vivobook, Zenbook, ProArt, ExpertBook), only to controllers whose USB product ID is in
  [`RogAuraProducts.cs`](src/RogBacklightTimeout.Core/Rog/RogAuraProducts.cs) and that expose feature report `0x5A`. The
  bytes are constants; configuration cannot change them. If nothing supported is found (after retrying for about two minutes, since the controller can appear late at logon) the agent exits.
- The agent restores the light when it stops, at session end, and on the first keypress after a crash. It runs elevated
  because Windows hides keyboard input from non-elevated background programs while an elevated window has focus, which
  is why it is installed under `%ProgramFiles%` (administrator-writable only).

Settings and the log live in `%LOCALAPPDATA%\RogBacklightTimeout` (`config.json`, `log.txt`). `config.json` also has
`fallbackLevel`, `gHelperConfigPath`, and the escape hatches `internalDeviceOverrides` / `externalDeviceOverrides`
(case-insensitive substrings of a device path or instance ID, for unusual hardware).

## Requirements

Windows 11 and an ASUS ROG laptop with an Aura HID keyboard controller, as supported by G-Helper. G-Helper itself
should be installed: it owns the brightness setting.

## Build

Needs the .NET 10 SDK.

```powershell
.\scripts\publish.ps1     # runs the tests, writes dist\RogBacklightTimeout.exe (self-contained, single file, ~11 MB)
```

| Project | Role |
| --- | --- |
| `src/RogBacklightTimeout.Core` | library: state machine, settings, Win32 message window, Plug and Play inspection, classifier, ROG backlight, autostart/installer |
| `src/RogBacklightTimeout` | the executable: menu, agent, composition root |
| `tests/RogBacklightTimeout.Tests` | unit tests (fake clock; classifier fixtures are real devices captured with `Get-PnpDeviceProperty`) |

Adding a model: add its controller's product ID to `RogAuraProducts` (only after it is verified in G-Helper or on
hardware) and extend the table test. Classifier fixtures come from the *Input devices* screen; add yours.

## Limits

- ROG laptops with an Aura HID keyboard controller only. ROG Ally and the TUF/Vivobook/Zenbook/ProArt/ExpertBook lines
  use other mechanisms in G-Helper and are refused.
- Touchpads that report as plain mice (non-precision) are not detected as activity; only keyboards and precision
  touchpads are. Detachable keyboards (e.g. ROG Flow Z13 folio) may need an `internalDeviceOverrides` entry.
- Lid close, AC switch, resume and lock are handled; fast user switching and Remote Desktop are
  handled by checking, on every session connect/disconnect, whether this session still owns the physical console.

## Licence

GPL-3.0-only, see [LICENSE](LICENSE). The code follows [G-Helper](https://github.com/seerge/g-helper) (GPL-3.0-only)
closely in places; attribution is in [NOTICE.md](NOTICE.md) and bundled third-party licences are in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). The release download is a zip that includes these files; keep them
with the exe. Not affiliated with ASUS.
