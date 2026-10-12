# Kone AIMO Config

A small Windows app for the ROCCAT Kone AIMO 16K (Remastered), made after ROCCAT's own software stopped being available.

![Lighting page](docs/lighting.png)

## What it does

- DPI stages, polling rate, sensitivity and angle snapping
- Lighting effect, speed, brightness and the colour of all 11 LEDs, previewed live on the mouse
- Every button, normal and Easy-Shift layer: clicks, media keys, keyboard shortcuts
- Blender macros. One-step shortcuts are stored in the mouse; multi-step ones (reset pose, mirror pose) are typed by the app from the tray
- All 5 profiles, JSON backups, and a check at login that the mouse still has what you last saved

Settings are written to the mouse's own memory, so they keep working on any PC without the app. Only the multi-step macros need it running.

## Build

Needs the .NET SDK. The app targets .NET Framework 4.8, which ships with Windows 10 and 11.

```
dotnet build KoneAimoConfig -c Release
```

The exe ends up in `KoneAimoConfig/bin/Release/net48/`.

## Notes

- The protocol was pieced together from libratbag, OpenRGB and other open-source Kone AIMO projects, then checked against a real mouse.
- The UI uses the Outfit font when it's installed and falls back to Segoe UI.
- Not affiliated with ROCCAT or Turtle Beach.

Made by Miguel Valencia, with help from Claude.
