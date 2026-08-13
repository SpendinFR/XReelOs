# XREAL build and hardware guide

This is the long-form engineering runbook for XReel OS. It records the build,
renderer, Android-display, Eye-camera and immersive-video decisions behind the
community APK. It is intentionally independent from any private application,
backend, user profile or local workstation path.

## 1. Scope and validated baseline

The source in this repository is the OS-only extraction of the v52 interaction
baseline. The critical runtime path was exercised on:

- Samsung Galaxy S24;
- Android 16 / Samsung One UI 8;
- XREAL One Pro + XREAL Eye;
- GlassesControl/ControlGlasses version displayed by the tested phone: 15.1.0;
- XREAL Unity SDK 3.1.0;
- Unity 6000.0.23f1;
- OpenGL ES 3, ARM64/IL2CPP, single-pass instanced stereo and 6DoF.

This is a hardware test matrix, not an XREAL compatibility guarantee. Host
Android updates, glasses firmware, ControlGlasses and the Unity SDK can each
change activity/display or Eye-camera behavior. Change only one of them at a
time and preserve a known-good APK before testing an update.

The repository does not redistribute the proprietary XREAL SDK tarball. A
developer must accept XREAL's terms and download that archive independently.

## 2. Release boundaries

The public application has its own identity and scene:

```text
product: XReel OS
package: com.spendinfr.xreelos
entry:   ai.nreal.activitylife.NRXRActivity
scene:   Assets/Scenes/XReelOs.unity
stable:  releases/XReelOs.apk
v2 test: releases/XReelOs-v2.apk
```

`BuildCommunityOsScene` sets `_osOnlyMode = true`. Creator/map tools, private
memory services, speech/ONNX models and user data are excluded. The public hand
plugin contains MediaPipe gesture processing only.

Never debug a public release by changing a private product package or by
installing over it. Package isolation is part of the rollback strategy.

## 3. Repository inputs

Install:

- Unity 6000.0.23f1 with Android Build Support, OpenJDK, SDK, NDK and IL2CPP;
- Android platform tools;
- Gradle 8.7 on `PATH` when rebuilding the hand-tracking AAR;
- Git LFS if pulling the checked-in release APK;
- XREAL SDK for Unity 3.1.0;
- MediaPipe Hand Landmarker model;
- the matching licensed ControlGlasses native service used by the lens bridge.

Place the proprietary SDK here:

```text
unity/Packages/xreal-sdk/com.xreal.xr.tar.gz
```

The hand model is expected here:

```text
unity/models/hand_landmarker.task
```

The following public compatibility sources are already versioned:

```text
scripts/PATCH_XREAL_S24_DISPLAY.ps1
scripts/BUILD_XREAL_SECURE_TASK_PROBE.ps1
scripts/BUILD_XREAL_TASKORGANIZER_STUBS.ps1
scripts/xreal-compat/
```

Brightness, electrochromic control and the physical 2D/3D transition call
XREAL's native service. The reviewable Java wrapper is checked in under
`unity/android/xreal-lens-probe`; the vendor `libnr_service.so` is not. Build the
local ignored AAR before the OS:

```powershell
.\scripts\BUILD_XREAL_LENS_PROBE_AAR.ps1 `
  -PrivateLibrary "C:\path\to\matching\libnr_service.so"
```

They must resolve inside this repository. A build that silently reads them from
a sibling/private checkout is not reproducible.

## 4. Reproducible build

From a PowerShell at the repository root:

```powershell
.\scripts\BUILD_XREAL_LENS_PROBE_AAR.ps1 `
  -PrivateLibrary "C:\path\to\matching\libnr_service.so"
.\scripts\BUILD_HAND_PLUGIN.ps1
.\scripts\BUILD_XREEL_OS.ps1
```

The OS script performs two Unity passes:

1. `AndroidBuildXreal.PrepareDefines` imports the XREAL, AR Foundation, XR Hands
   and XR Interaction dependencies and defines `XREAL_SDK_PRESENT`.
2. `AndroidBuildXreal.BuildXReelOsApk` compiles the real XREAL path, builds the
   OS scene, injects the Android bridges and emits the APK.

The two-pass sequence matters on a fresh clone. Building in one pass can compile
the fallback adapter before Unity has refreshed assemblies for the new package.

Equivalent commands:

```powershell
$unity = "C:\Program Files\Unity\Hub\Editor\6000.0.23f1\Editor\Unity.exe"
$project = Join-Path $PWD "unity"

& $unity -batchmode -nographics -quit -buildTarget Android `
  -projectPath $project `
  -executeMethod MLOmega.XR.Editor.AndroidBuildXreal.PrepareDefines `
  -logFile (Join-Path $project "xreelos-prepare.log")

& $unity -batchmode -nographics -quit -buildTarget Android `
  -projectPath $project `
  -executeMethod MLOmega.XR.Editor.AndroidBuildXreal.BuildXReelOsApk `
  -logFile (Join-Path $project "xreelos-build.log")
```

Always start Unity with `-buildTarget Android`. Several XREAL setting fields are
compiled only for Android; opening a fresh project as a Windows target first can
produce misleading missing-field errors.

On success the script updates:

```text
unity/build/android/XReelOs-v2.apk
releases/XReelOs-v2.apk
releases/XReelOs.apk (preserved v1 rollback)
releases/SHA256SUMS.txt
```

## 5. Android and renderer invariants

The builder enforces the hardware-proven renderer contract:

- ARM64 and IL2CPP;
- OpenGL ES 3, never Vulkan for this baseline;
- single-pass instanced stereo;
- 6DoF tracking;
- XREAL Controller as the SDK input source;
- Eye/MediaPipe gestures as a parallel application input layer;
- built-in render pipeline for the community scene;
- transparent optical clear and explicitly included runtime shaders;
- no ordinary Samsung DeX/freeform window.

Why explicit shader reachability matters: `Shader.Find` can work in the Unity
Editor while the same shader is stripped from an APK. Runtime materials then
become invisible or magenta. XReel keeps its runtime shaders in assets that the
builder includes, including `Assets/Resources/XrLabWebVr.shader`.

A full-screen magenta/violet layer is a shader/pipeline fault, not a normal
XREAL optical background. Check the active render pipeline, shader inclusion
and post-processing resources before changing camera alpha repeatedly.

## 6. Samsung external display and DeX

The glasses are exposed as an external display. Samsung can claim that display
for DeX before the XREAL activity creates its stereo presentation. Symptoms
include a taskbar, duplicated phone content, a freeform window or the Unity app
landing on display 0.

Use the included preflight:

```powershell
.\scripts\PREPARE_XREEL_OS.ps1
```

With a specific ADB serial:

```powershell
.\scripts\PREPARE_XREEL_OS.ps1 -Serial "DEVICE_SERIAL"
```

The script:

- sets `dex_on_external_display` to `0` in system/global/secure settings;
- starts Shizuku through its official on-device script when available;
- force-stops only the old XReel OS task;
- clears logcat for a clean diagnostic window;
- launches `com.spendinfr.xreelos/ai.nreal.activitylife.NRXRActivity`.

Useful verification:

```powershell
$adb = "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe"
& $adb shell dumpsys activity activities |
  Select-String "SecondaryLauncher|dexservice|mode=freeform|name=Desk"
```

No DeX desktop task should own the glasses. Launching the generic
`UnityPlayerActivity` from the phone launcher is not equivalent to entering via
`NRXRActivity`.

### SDK preview note

XREAL may provide Android 16 preview SDK/ControlGlasses combinations whose
recommended activity policy differs. A support preview available during the
August 2026 investigation paired Unity SDK 3.1.9 Preview with ControlGlasses
3.1.2 Preview, Samsung display mode `Mirrored`, and Support Multi Resume
disabled. Treat that as a separate compatibility branch. Do not mix a preview
runtime into this stable 3.1.0 release and publish it without re-running every
hardware gate below. When a preview contains a different licensed native
service, pass its explicitly reviewed SHA-256 to the lens build script through
`-ExpectedSha256` rather than disabling the integrity check.

## 7. S24 display-name compatibility patch

Some Samsung firmware reports the EDID product name (`One Pro`) instead of a
display name containing the literal `HDMI`. XREAL SDK 3.1's display plug event
can reject the otherwise valid display.

`PATCH_XREAL_S24_DISPLAY.ps1` restores the official AARs from the local SDK
archive on every build, recompiles only the reviewable `DisplayModel`
compatibility class, accepts XREAL EDID manufacturer identifiers, and replaces
the proxy activity's desktop background with optical black. The patch is
idempotent and operates only on the Unity package cache.

Do not commit modified proprietary AARs. Commit the patch source, not the SDK
cache output.

## 8. Eye camera and gestures

Runtime flow:

```text
XREAL Eye RGB frame
  -> GestureBridge scheduling and low-light profile
  -> Android MediaPipe Hand Landmarker
  -> landmarks/gesture state
  -> XrealNativeHandPointer
  -> gaze target + pinch/gesture action
```

The camera is processed locally. Normal hand tracking does not store frames.
First-person recording is a distinct, user-triggered feature and is configured
without microphone audio.

Validated gestures:

- gaze + thumb/index pinch: click; hold for drag;
- open palm: recenter active window or restore the last closed window;
- two open palms: show/recenter the dock;
- thumb up: compact quick menu;
- closed fist: window/interaction standby and restore;
- pointing index moved vertically: scroll.

The source also contains off/light/reinforced low-light profiles and a head-only
dwell fallback. Keep gesture recognition at the validated 25 fps target unless
profiling proves that a different rate improves latency without causing thermal
or frame-pacing regressions.

Healthy camera/session logs normally include a running XREAL session, RGB frame
delivery and a ready MediaPipe landmarker. `RGB=0`, `grayscale=0`, repeated
`get device config failed` or `NativeRGBCamera Start Failure` are camera/runtime
initialization faults, not UI click bugs.

## 9. v52 spatial UI contract

The v52 baseline includes:

- persistent spatial dock and application positions;
- move, depth, tilt, proportional resize and free resize;
- portrait, landscape and aspect presets;
- close, restore, session resume and dock reset;
- window blocks with shared movement;
- settings and compact thumb menu;
- small dark status toasts with white title, orange state and status dot;
- a glass backside when walking behind an Android app window, without decoding
  or duplicating the Android stream a second time;
- Moonlight 2D return that closes the invalid old Android surface rather than
  leaving a black spatial window.

Window handles appear only when gazed at. Input hit testing must use the XR eye
render geometry/window plane, not the portrait phone's `Screen.width` and
`Screen.height` as a second rejection gate.

## 10. Android applications and icons

The default dock targets:

| App | Package |
| --- | --- |
| Chrome/Google | `com.android.chrome` |
| YouTube | `com.google.android.youtube` |
| Netflix | `com.netflix.mediaclient` |
| Spotify | `com.spotify.music` |
| Reddit | `com.reddit.frontpage` |
| Prime Video | `com.amazon.avod.thirdpartyclient` |
| Moonlight | `com.limelight` |

`XrLabAndroidIconLoader` requests each installed application's drawable from
Android `PackageManager`, converts it to a transient Unity sprite and falls back
to a local glyph. Third-party icons are therefore neither downloaded nor
committed. Package queries are injected into the final Android manifest.

Adding a new ordinary app generally requires its package/activity policy and a
dock entry. Protected media applications must use the cinema policy; copying a
secure Widevine surface into a normal Unity texture will produce black video.

## 11. Shizuku and spatial Android surfaces

Shizuku supplies privileges needed for Android task/display and input routing.
On a non-rooted phone it normally stops after reboot. Start it again using its
official Wireless Debugging flow and authorize XReel OS.

Ordinary apps use Shizuku-backed task/display slots inside movable 3D windows.
Multiple slots can coexist. If all such windows are black after reboot, verify
Shizuku service state and authorization before rebuilding Unity.

The repository includes:

- a native `ASurfaceControl` probe;
- compile-only `TaskOrganizer` platform signatures;
- Android Java bridges injected by the build postprocessor.

The compile-only stub is never packaged as a replacement Android framework
class; the real class comes from the device boot class path.

## 12. Protected cinema

Netflix and Prime Video are secure-surface applications. XReel uses a system
cinema transition instead of pretending their protected pixels are an ordinary
Unity texture.

The cinema dock provides return, volume, brightness/electrochromic controls and
playback actions supported by the Android media session. On return, XReel:

1. gives the external display back to XREAL;
2. relaunches the 3D activity once;
3. restores the spatial session/dock;
4. restarts Eye/MediaPipe interaction.

Do not repeatedly relaunch the activity during one return. Duplicate XR tasks
produce blank displays, lost input or orphaned Android surfaces.

## 13. Moonlight

Moonlight can appear as:

- a movable spatial Android window; or
- a full 2D desktop/cinema surface for maximum remote-desktop compatibility.

Sunshine owns the PC stream. Tailscale can make that host reachable away from
the home LAN, but XReel OS itself remains usable with the PC offline.

After returning from the 2D Moonlight surface, the old Android surface is no
longer valid. v52 closes that host cleanly, restores the layout, displays the
dock only if no other window is visible and restarts gesture input.

## 14. Immersive web video

The internal VR browser is independent from the DRM cinema path.

```text
web page/video element
  -> JavaScript probe and stream descriptor
  -> XrWebVrBridge.java (Media3/SurfaceTexture)
  -> external OES texture
  -> XrLabWebVrStreamTexture
  -> XrLabWebVrPresenter
  -> XrLabWebVr.shader per eye
```

The browser can select auto/mono/SBS layouts and project direct streams as flat,
VR180 or VR360 media. The renderer samples the correct half of an SBS texture
for each eye; it does not display the two source images side by side on one
cinema rectangle.

Immersive controls include play/pause, seek, close/return, zoom/distance and
layout selection. Two open palms exits immersive view and restores the browser.

The Android build postprocessor must inject `XrWebVrBridge` for
`com.spendinfr.xreelos`. It also adds the Media3 HLS dependency used by native
stream playback. A source build that forgets this package gate can compile the
Unity browser while failing only when the VR button is pressed.

Limitations:

- direct media URLs and site player implementations vary;
- a site may replace its video element, hide the real stream behind scripts or
  require authentication;
- YouTube's web player is not a universal WebVR source;
- selecting 6K/8K is useful only when the source, network and S24 decoder can
  sustain it;
- protected DRM playback remains in system cinema.

## 15. Performance and thermal behavior

The S24 decodes video, runs Unity stereo rendering, XREAL 6DoF and MediaPipe
while powering the glasses over USB-C. Warm operation is expected; long high
resolution VR sessions can trigger Samsung thermal management.

Keep:

- no per-frame RGB readback for the VR decoder;
- the Android external texture path;
- only one hand inference scheduler;
- no duplicate back-face video decoder (the backside is static glass);
- 25 fps gesture target unless measured otherwise;
- 60 Hz as the validated XR baseline.

The v2 thermal candidate additionally waits for a valid Media3 frame and then:

- explicitly pauses HTML video/audio in the source page;
- pauses/hides the source WebView and TLab capture host, removing the site preview
  from the top of the phone while leaving return controls available;
- keeps only Media3 decoding the immersive stream;
- feeds MediaPipe from native Eye Y/U/V planes without a redundant full-size RGB
  blit, with an automatic RGB fallback after any native submission failure.

The raw-WebView VR fallback is not suspended because it is the fallback's source.
These changes prove less duplicated work, not a measured thermal result. Keep v1
available until a 20-30 minute hardware comparison passes.

Selecting 90/120 Hz in ControlGlasses does not automatically make the Unity XR
player render at that rate. Higher refresh increases thermal/GPU pressure and
requires a complete frame-pacing, camera and gesture regression pass.

## 16. Diagnostic commands

Install and confirm identity:

```powershell
$adb = "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe"
& $adb install -r ".\releases\XReelOs-v2.apk"
& $adb shell dumpsys package com.spendinfr.xreelos |
  Select-String "versionName|versionCode"
```

Clean launch log:

```powershell
& $adb logcat -c
& $adb shell am force-stop com.spendinfr.xreelos
& $adb shell am start -n `
  com.spendinfr.xreelos/ai.nreal.activitylife.NRXRActivity
& $adb logcat -v time |
  Select-String "XReel|NRSDK|NRExternalSensor|MediaPipe|XrWebVr|SecureSurface"
```

APK verification:

```powershell
$apk = ".\releases\XReelOs-v2.apk"
$analyzer = "$env:LOCALAPPDATA\Android\Sdk\cmdline-tools\latest\bin\apkanalyzer.bat"
& $analyzer manifest application-id $apk
& $analyzer files list $apk |
  Select-String "hand_landmarker|XrWebVr|onnx|sherpa|webrtc"
Get-FileHash $apk -Algorithm SHA256
```

Expected: XReel package, hand model and VR bridge present; private speech,
ONNX, WebRTC/live-transport payloads absent.

## 17. Release hardware gate

Before publishing a new APK, verify in this order:

1. transparent stereo world, no DeX/taskbar/phone mirror;
2. stable 6DoF and world-locked dock;
3. pinch, palm, two palms, thumb, fist and index scroll;
4. move/depth/tilt/resize/aspect/close and saved placement;
5. settings, quick menu and low-light/head-only modes;
6. Chrome/Google, YouTube, Reddit and Spotify spatial windows;
7. Android application icons loaded from installed packages;
8. XR keyboard and text entry;
9. multiple ordinary app windows simultaneously;
10. Netflix and Prime protected cinema, then clean 3D return;
11. Moonlight spatial and 2D modes, then clean return and working pinch;
12. direct VR stream in mono/SBS/VR180 as applicable, head tracking, seek,
    play/pause and two-palm exit;
13. first-person recording with no microphone audio;
14. session resume/dock reset after clean and abrupt exits;
15. 20-30 minute thermal/frame-pacing observation.

If a candidate fails, keep the previous `releases/XReelOs.apk` and its hash.
Do not overwrite the only hardware-proven artifact while debugging.

## 18. Known limits

- XREAL One Pro + Eye exposes one RGB viewpoint, not Air 2 Ultra's native hand
  tracking/depth stack. Hand input is application-level MediaPipe tracking.
- Persistent object-by-object spatial anchors depend on SDK/hardware support;
  session world-space placement is not the same as cross-reboot relocalization.
- Android secure surfaces cannot be sampled like normal Unity textures.
- Shizuku must be restarted after a non-rooted phone reboot.
- Site-specific browser and DRM behavior can change without a source update.
- This software is experimental and not appropriate for driving or other
  safety-critical use.
