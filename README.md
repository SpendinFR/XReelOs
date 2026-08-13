# XReel OS

XReel OS is an experimental spatial shell for **XREAL One Pro + XREAL Eye**.
It turns Android apps, browser surfaces, protected cinema playback and a remote
PC into movable world-space windows controlled by gaze and monocular
Eye-camera hand gestures.

This repository contains the Unity source, Android bridge code, the complete
Eye/MediaPipe interaction implementation, build scripts, troubleshooting notes
and a ready-to-install APK. It does **not** contain the private Memory/Brain2
backend, user data, cloud credentials, or the proprietary XREAL SDK archive.

The current public source is synchronized with the hardware-validated **v52 UI
baseline**: the complete spatial window system, Android-app/cinema bridges and
the immersive VR browser are included rather than represented by a demo stub.

## Hardware validation

The current interaction baseline was tested on real hardware on 11 August 2026:

- Samsung Galaxy S24;
- Android 16 / Samsung One UI 8;
- XREAL One Pro with XREAL Eye;
- current glasses firmware at test time;
- GlassesControl/ControlGlasses version shown on the tested phone: **15.1.0**;
- Unity 6000.0.23f1 and XREAL SDK for Unity 3.1.0;
- OpenGL ES 3, single-pass instanced stereo and 6DoF.

The S24 result is a project hardware test, not an XREAL certification claim.
XREAL's SDK 3.1 release notes list Beam Pro and Samsung S25 as tested hosts,
while its SDK 3.0 notes list the S24. Compatibility with other phones, firmware
or glasses must be re-tested. See the official [XREAL download and release-note
page](https://developer.xreal.com/download/).

## What works

- transparent, world-locked spatial dock and windows;
- Eye RGB frames processed locally by MediaPipe Hand Landmarker at up to 25 fps;
- pinch click/drag, open palm recenter/restore, two-palm dock, thumb quick menu,
  fist standby, and index scroll;
- three low-light hand profiles and a head-only dwell click/drag fallback;
- visionOS-style window chrome: move, depth, tilt, proportional/free resize,
  portrait/landscape/aspect controls, close and persistent placement;
- reorganizable 3-4-3 app dock, quick system menu and multi-row window blocks;
- Android app surfaces for Chrome/Google, YouTube, Reddit and Spotify;
- an internal spatial browser with XR keyboard, gaze/pinch text input, page
  zoom and scrolling;
- immersive browser video: direct media-stream capture, mono/SBS selection,
  VR180/VR360 projection, head-tracked viewing and in-VR seek/playback controls;
- protected cinema handoff for Netflix and Prime Video with an in-cinema dock
  and a clean return to the 3D shell;
- Moonlight as a spatial 3D window or a full 2D desktop surface;
- XREAL brightness and electrochromic controls, Android media volume, phone
  battery, time, thermal/tracking status and first-person video capture;
- saved window/session layout plus DeX/Shizuku startup repair.

Dock icons are loaded from each installed Android package at runtime, so the
real Chrome, YouTube, Netflix, Spotify, Reddit, Prime Video and Moonlight icons
appear without redistributing third-party artwork. A fallback glyph is shown
when Android cannot expose an icon. XReel OS itself ships with its own launcher
icon under `unity/Assets/Brand/`.

The first-person recorder is configured without microphone audio. Eye frames
are processed ephemerally and are not saved by the hand-tracking path.

## Install on the tested S24 setup

1. Update the glasses firmware from XREAL's official OTA flow, then install the
   current **GlassesControl/ControlGlasses** app from
   [XREAL Developer Downloads](https://developer.xreal.com/download/). Open it
   once, accept its device permissions and verify that it sees the glasses.
2. Install [Shizuku](https://github.com/RikkaApps/Shizuku). Enable Android
   Developer options, USB debugging and Wireless debugging; pair Shizuku and
   press **Start**. Authorize XReel OS when prompted. On a non-rooted phone this
   start step must be repeated after a reboot.
3. On the PC, install the APK:

   ```powershell
   $adb = "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe"
   & $adb install -r ".\releases\XReelOs.apk"
   ```

   The current release size and SHA-256 are published in
   [`releases/SHA256SUMS.txt`](releases/SHA256SUMS.txt).

4. Run the reproducible preflight while the phone is reachable by ADB:

   ```powershell
   .\scripts\PREPARE_XREEL_OS.ps1
   ```

   It disables Samsung DeX ownership of the external display, starts Shizuku if
   its official start script is present, clears only XReel OS's old task, and
   launches `NRXRActivity` rather than the generic Unity activity.
5. Disconnect the phone from the PC, connect the XREAL One Pro + Eye to the S24,
   keep Samsung DeX disabled, and launch **XReel OS**. If Android opens DeX or
   mirrors the phone, stop and run the preflight again; do not debug Unity UI
   while DeX still owns the display.

For a complete first-run checklist, Shizuku reboot behavior, Moonlight/Sunshine
and outdoor use, read [Installation and first run](docs/INSTALLATION.md).

## Interaction quick reference

| Gesture | Result |
| --- | --- |
| Look + pinch thumb/index | Click; hold to drag |
| Open palm | Recenter the active window, or restore the last closed window |
| Two open palms | Open and center the spatial dock |
| Thumb up | Open the compact quick menu |
| Closed fist | Put interaction/windows in standby; repeat to restore |
| Pointing index moved vertically | Scroll the active surface |

Window handles appear only when gazed at. Their exact behavior and the
head-only mode are documented in [Interaction reference](docs/INTERACTION.md).

## Build

Prerequisites:

- Unity `6000.0.23f1` with Android Build Support, IL2CPP and ARM64 tools;
- Gradle 8.7 available on `PATH` (only when rebuilding the hand AAR);
- XREAL SDK for Unity `3.1.0`, downloaded under XREAL's own terms;
- a matching licensed ControlGlasses `libnr_service.so` for the local lens
  control bridge (the proprietary library is not committed);
- the MediaPipe hand model at `unity/models/hand_landmarker.task`.

Place the XREAL archive at:

```text
unity/Packages/xreal-sdk/com.xreal.xr.tar.gz
```

Then run:

```powershell
.\scripts\BUILD_XREAL_LENS_PROBE_AAR.ps1 `
  -PrivateLibrary "C:\path\to\matching\libnr_service.so"
.\scripts\BUILD_HAND_PLUGIN.ps1
.\scripts\BUILD_XREEL_OS.ps1
```

The lens command compiles the reviewable Java bridge against the
developer-supplied ControlGlasses native library; that raw AAR remains
git-ignored. The hand command rebuilds the public hand-only Android bridge and
runs its JVM tests. It deliberately excludes the private ASR/ONNX/audio modules.
The final command builds the Unity APK with version `6000.0.23f1`.

The checked-in APK is development-signed for direct sideload testing. Forks
intended for store or production distribution must use their own release key.

The Android target is mandatory at editor startup because several XREAL SDK
settings are compiled only under `UNITY_ANDROID`. The output is
`unity/build/android/XReelOs.apk`. Start with the short
[build guide](docs/BUILD_GUIDE.md); the complete hardware and renderer runbook
is [Build Guide XREAL](docs/BUILD_GUIDE_XREAL.md).

## Source map

- `unity/Assets/Scripts/Reflex/GestureBridge.cs` — C# frame scheduling, gesture
  state and low-light profile control;
- `unity/Assets/Scripts/Reflex/HandLowLightEnhancer.cs` — low-light processing;
- `unity/android/reflexvision/` and `mlomega-reflexvision.aar` — Android
  MediaPipe Hand Landmarker bridge;
- `unity/Assets/Scripts/XrealSpatial/XrealNativeHandPointer.cs` — gaze + pinch,
  gesture arbitration and pointer state;
- `unity/Assets/Scripts/UI/WorldCreatorExternalWindows.cs` — shared spatial
  window surfaces and input routing;
- `unity/Assets/Scripts/UI/WorldCreatorQuickMenu.cs` and
  `WorldCreatorWindowBlock.cs` — quick menu, dock and grouped layouts;
- `unity/Assets/Scripts/Lab/WorldCreatorLabShell.cs` — OS app shell, Android app
  slots, internal browser, keyboard, cinema, Moonlight and VR orchestration;
- `unity/Assets/Scripts/Lab/XrLabWebVr*.cs` — media discovery, Android external
  texture transport and the Unity VR180/VR360 presenter;
- `unity/Assets/Resources/XrLabWebVr.shader` — per-eye mono/SBS sampling and
  immersive projection shader;
- `unity/Assets/Scripts/SecureSurfaceSpike/` — protected/system surface bridge;
- `scripts/xreal-compat/` — reviewable Samsung/XREAL display compatibility,
  secure-surface native probe and compile-only Android platform signatures;
- `unity/Assets/Scripts/Editor/AndroidBuildXreal.cs` — reproducible Android build.

See [Architecture](docs/ARCHITECTURE.md) for the runtime data flow.

## Projects that helped

XReel OS contains original integration work, but the investigation benefited
from these public projects and official samples:

- [XREALSDKTemplate](https://github.com/dengxian-xreal/XREALSDKTemplate) — the
  known-good XREAL Unity rig and Android rendering baseline;
- [MixedRealityToolkit-Unity-XREALSDK](https://github.com/dengxian-xreal/MixedRealityToolkit-Unity-XREALSDK)
  — XREAL/MRTK interaction reference;
- [Xreal-tools](https://github.com/nudou350/Xreal-tools) — demonstrated
  MediaPipe gesture control from the XREAL Eye on Samsung hardware;
- [hand-tracking-streamer](https://github.com/wengmister/hand-tracking-streamer)
  — useful hand-landmark and streaming reference;
- [PortalPad](https://github.com/Smart-Home-User/PortalPad) — Shizuku input,
  external-display and DRM/system-mirror research;
- [vr2xr](https://github.com/skarian/vr2xr) and
  [mpv-android-vr](https://github.com/mpv-android-vr/mpv-android-vr) — useful
  projection, stereo-layout and Android VR playback references;
- [MediaPipe](https://github.com/google-ai-edge/mediapipe) — Hand Landmarker;
- [TLabWebView](https://github.com/TLabAltoh/TLabWebView) and
  [TLabVKeyborad](https://github.com/TLabAltoh/TLabVKeyborad) — Unity Android
  browser and keyboard foundations.

See [Third-party notices](THIRD_PARTY_NOTICES.md) before redistribution.

## Status and safety

This is experimental research software, not a safety-certified operating
system. Never use overlays while driving or where delayed/incorrect input could
cause harm. Protected streaming behavior depends on Android, the streaming app,
DRM policy and firmware and can change independently.

Contributions are welcome. Please start with [CONTRIBUTING.md](CONTRIBUTING.md)
and preserve the hardware gates described in the build guide.
