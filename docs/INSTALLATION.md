# Installation and first run

## Validated configuration

The release APK was exercised on a Galaxy S24 running Android 16 / One UI 8,
XREAL One Pro + Eye, and the GlassesControl/ControlGlasses build displayed as
15.1.0 on that phone. XREAL SDK 3.1.0 and current glasses firmware were used.

This is the validation matrix, not a promise that every USB-C Android phone has
the same external-display, DeX or XR lifecycle behavior.

## 1. Prepare the phone

1. Update Android applications, but do not change glasses firmware in the middle
   of a validation session.
2. Enable Developer options, USB debugging and Wireless debugging.
3. Set XReel OS, GlassesControl, Shizuku and (if used) Tailscale/Moonlight to
   unrestricted battery use.
4. Pair Bluetooth headphones, keyboard, mouse or gamepad with the **phone**.
   The glasses are not the Bluetooth host; Android routes audio and input.
5. Keep Samsung DeX disabled. Neither a DeX desktop nor ordinary phone mirroring
   is the XR presentation surface.

## 2. GlassesControl and firmware

Download GlassesControl/ControlGlasses and the SDK only from
<https://developer.xreal.com/download/>. Open GlassesControl once and grant the
requested USB/device permissions. Connect the One Pro + Eye and confirm that the
app reports the glasses and firmware before installing XReel OS.

## 3. Shizuku

Install Shizuku from <https://github.com/RikkaApps/Shizuku> or its official store
listing. Pair it through Wireless debugging and start the service. XReel OS uses
Shizuku for Android task/display and input operations that ordinary applications
cannot perform.

Important: on a non-rooted Android phone, Shizuku normally stops after a reboot.
Open Shizuku and press **Start** again before expecting external Android app
windows to work. XReel OS can call the existing official start script, but it
cannot bypass Android's initial post-reboot authorization model.

## 4. Install and preflight

From a PowerShell opened at the repository root:

```powershell
$adb = "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe"
& $adb devices
& $adb install -r ".\releases\XReelOs.apk"
.\scripts\PREPARE_XREEL_OS.ps1
```

With more than one ADB device, select it explicitly:

```powershell
.\scripts\PREPARE_XREEL_OS.ps1 -Serial "DEVICE_SERIAL"
```

The preflight sets all three Samsung DeX external-display keys to zero, starts
Shizuku when its standard script exists, force-stops only the previous XReel OS
task, and launches:

```text
com.spendinfr.xreelos/ai.nreal.activitylife.NRXRActivity
```

Launching the generic Unity activity can put the app on display 0 and produce
phone mirroring instead of stereo XR.

## 5. First headset session

1. Complete the ADB preflight.
2. Disconnect the USB debugging cable.
3. Connect the XREAL One Pro + Eye to the S24.
4. Launch XReel OS (or allow GlassesControl to launch it).
5. Confirm a transparent world view and spatial dock — no DeX taskbar, phone
   home screen, magenta slab or duplicated 2D display.
6. Hold a well-lit hand in the Eye camera field. Test pinch, open palm, two
   palms, thumb-up quick menu, then window move/resize.
7. Authorize Shizuku for XReel OS when asked.
8. Test a non-protected app window first (Chrome/YouTube/Reddit/Spotify), then
   Netflix or Prime cinema, Moonlight, and finally the internal VR browser with
   a direct non-DRM VR stream.

During XReel operation, use the application's own recenter and return controls.
On the validated One Pro setup, the glasses' physical X/2D-3D mode control can
move the panel out of the display mode expected by the active XR session.

## Installed applications

The current dock recognizes these Android package names:

| App | Android package |
| --- | --- |
| Chrome / Google | `com.android.chrome` |
| YouTube | `com.google.android.youtube` |
| Netflix | `com.netflix.mediaclient` |
| Spotify | `com.spotify.music` |
| Reddit | `com.reddit.frontpage` |
| Prime Video | `com.amazon.avod.thirdpartyclient` |
| Moonlight | `com.limelight` |

When an application is not installed, its surface cannot launch. Manufacturer,
beta or regional variants can expose a different package/activity and may need
a small adapter contribution. Dock artwork is not bundled: XReel OS asks
Android for the icon of the installed package and displays a fallback glyph if
that lookup fails.

## Internal VR browser

Open **Navigateur VR** from the dock, browse to a compatible direct-video site,
start the video and select **VR**. Use AUTO/mono/SBS as needed for the source.
The immersive view supports head tracking, play/pause and seek; show two open
palms to return to the spatial browser. Protected Netflix/Prime playback uses
the separate cinema route and is not a WebVR source.

## Moonlight outside the home network

Pair Moonlight with Sunshine while both machines are local first. For remote use,
run Sunshine on the PC, connect phone and PC through Tailscale, and add the PC's
Tailscale address as a Moonlight host. XReel OS itself remains usable without a
PC; only the remote desktop surface depends on Sunshine and network reachability.

## After reboot

1. Start Shizuku again.
2. Confirm GlassesControl sees the glasses.
3. Keep DeX disabled.
4. Launch XReel OS. If app windows are black, first verify Shizuku authorization
   rather than rebuilding Unity.

Next, read the [User guide](USER_GUIDE.md) for context-sensitive gestures,
cinema/Moonlight return and session recovery. Review
[Known limitations and roadmap](KNOWN_LIMITATIONS.md) before changing firmware,
refresh rate, phone or headset.
