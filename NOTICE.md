# Notices and attribution

ROG Backlight Timeout builds on the work and published knowledge of other open-source projects.

- **G-Helper** (https://github.com/seerge/g-helper, GPL-3.0) - the ASUS Aura keyboard HID brightness report
  (`app/USB/AsusHid.cs`, `app/USB/Aura.cs`), the list of supported controller product IDs, the model checks in
  `app/AppConfig.cs`, and the meaning of the `keyboard_brightness*` settings (`app/Input/InputDispatcher.cs`).
  `RogBacklight` follows `AsusHid.WriteInput`/`FindDevices` closely.
- **Linux `hid-asus`** (https://github.com/torvalds/linux/blob/master/drivers/hid/hid-asus.c, GPL-2.0-or-later) -
  independent confirmation of the same HID report and controller IDs.
- **HidSharp** (NuGet `HidSharp`, https://www.nuget.org/packages/HidSharp, Apache-2.0) - HID access,
  bundled into the executable.
- **.NET runtime** (https://github.com/dotnet/runtime, MIT) - bundled into the self-contained executable.
- **Microsoft documentation** for Raw Input, device container IDs and CfgMgr32 properties, referenced in the code
  comments where they are used.

This repository is licensed GPL-3.0-or-later (see LICENSE) because the code is derived in part from GPL-3.0 G-Helper sources.
