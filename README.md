# Twinkle Zero Blackout

A small Windows companion for [Twinkle Tray](https://github.com/xanderfrangos/twinkle-tray). When the selected monitor's Twinkle Tray brightness reaches 0%, it covers the primary display with a black window and hides the mouse pointer. Raising brightness removes the cover and asks Twinkle Tray to show its normal brightness indicator.

This creates a black image; **it does not turn off the monitor or its backlight**. The app follows the selected monitor's brightness but covers the Windows **primary display**, so select the primary display in Twinkle Tray. It does not currently support choosing a different display for the cover. Exclusive fullscreen applications or higher-priority windows may appear over the cover briefly or bypass it.

## Requirements

- Windows with .NET Framework 4.5 or later and Twinkle Tray running with a working brightness control for the target monitor.
- A Twinkle Tray brightness shortcut that works independently of the mouse, so the screen can be brought back from 0%.
- Twinkle Tray installed in its per-user location (`%LOCALAPPDATA%\Programs\twinkle-tray\Twinkle Tray.exe`) for the indicator to be requested on unblank. Other installations can use the cover, but the indicator may not reappear automatically.

The helper polls Twinkle Tray's local named pipe every 250 ms. Twinkle Tray's session logging records pipe requests, so consider disabling **Save logs to file** in its Debug settings to avoid excessive log output. Those logs may include Twinkle Tray's UDP key; review them before sharing.

## Build and run

In PowerShell, from this directory, build using the .NET Framework C# compiler supplied with Windows:

```powershell
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /r:System.Windows.Forms.dll /r:System.Drawing.dll /out:Blackout.exe Blackout.cs
```

Start Twinkle Tray and adjust brightness once to let it detect the display. Then list its known display keys and names:

```powershell
$displays = Get-Content "$env:APPDATA\twinkle-tray\known-displays.json" -Raw | ConvertFrom-Json
$displays.PSObject.Properties | ForEach-Object { "{0}  {1}" -f $_.Name, $_.Value.name }
```

Run the helper with the key or a unique substring of the key for your primary display as the sole argument:

```powershell
Start-Process -FilePath (Resolve-Path .\Blackout.exe) -ArgumentList 'YOUR_MONITOR_KEY'
```

For automatic startup, keep `Blackout.exe` in a permanent location, open the Windows Startup folder by entering `shell:startup` in File Explorer, and create a shortcut there. Set its target to the quoted executable path followed by the same monitor-key argument, for example `"C:\Path\To\Blackout.exe" "YOUR_MONITOR_KEY"`. Enable Twinkle Tray's own **Start with Windows** option as well. If the monitor key changes after reconnecting the display or changing ports, update the argument.

## Behavior and recovery

At 0%, the cover intercepts mouse clicks and hides the pointer. Keyboard shortcuts continue to work. Raise brightness to remove the cover and restore mouse use. If Twinkle Tray stops responding or the display cannot be found, the cover is removed automatically. To exit the helper manually, press **Ctrl+Alt+Shift+F12**; if that shortcut is already in use, end `Blackout.exe` in Task Manager. The helper does not change the system cursor resources.

