# Notices and attribution

ROG Backlight Timeout builds on the work and published knowledge of other open-source projects.

- **G-Helper** (https://github.com/seerge/g-helper, GPL-3.0-only) - the ASUS Aura keyboard HID brightness report
  (`app/USB/AsusHid.cs`, `app/USB/Aura.cs`), the list of supported controller product IDs, the model checks in
  `app/AppConfig.cs`, and the meaning of the `keyboard_brightness*` settings (`app/Input/InputDispatcher.cs`).
  `RogBacklight` follows `AsusHid.WriteInput`/`FindDevices` closely.
- **Linux `hid-asus`** (https://github.com/torvalds/linux/blob/master/drivers/hid/hid-asus.c, GPL-2.0-or-later) -
  independent confirmation of the same HID report and controller IDs. Used as a reference only; no code was copied.
- **HidSharp** (NuGet `HidSharp`, https://www.nuget.org/packages/HidSharp, Apache-2.0) - HID access,
  bundled into the executable. Licence text: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
- **.NET runtime** (https://github.com/dotnet/runtime, MIT) - bundled into the self-contained executable. Licence text
  and the runtime's own third-party list: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) and
  [THIRD-PARTY-NOTICES-dotnet.txt](THIRD-PARTY-NOTICES-dotnet.txt).
- **Microsoft documentation** for Raw Input, device container IDs and CfgMgr32 properties, referenced in the code
  comments where they are used.

## Licence of this repository

GPL-3.0-only (SPDX `GPL-3.0-only`, see LICENSE).

The code is derived in part from G-Helper, which is GPL-3.0-only: its LICENSE is the plain GPL-3.0 text without an
"or any later version" grant, the G-Helper source files used carry no per-file notice that adds one, and the
SignPath Foundation project page for G-Helper lists the licence as "GNU General Public License v3.0 only". No permission
for later GPL versions is claimed here because it cannot be granted for the borrowed code.
