# Build guide

## Toolchain

- Windows 11 (validated build host);
- Unity 6000.0.23f1 with Android Build Support;
- Android SDK/build tools and NDK installed through Unity or Android Studio;
- JDK 17;
- Gradle 8.7 on `PATH` when rebuilding `mlomega-reflexvision.aar`;
- XREAL SDK for Unity 3.1.0;
- Git LFS for release APKs.

## Proprietary SDK

Accept XREAL's API terms and download the SDK from
<https://developer.xreal.com/download/>. Do not commit or redistribute the SDK
archive in this repository. Place it here:

```text
unity/Packages/xreal-sdk/com.xreal.xr.tar.gz
```

`Packages/manifest.json` references the local archive. A missing archive must
fail early rather than silently compile a non-XREAL player.

## Hand model

Place MediaPipe's Hand Landmarker model at:

```text
unity/models/hand_landmarker.task
```

The OS builder copies only that model into StreamingAssets. It does not embed
the original project's speech, KWS, VAD or ONNX models.

## Command-line build

```powershell
.\scripts\BUILD_XREAL_LENS_PROBE_AAR.ps1 `
  -PrivateLibrary "C:\path\to\matching\libnr_service.so"
.\scripts\BUILD_HAND_PLUGIN.ps1
.\scripts\BUILD_XREEL_OS.ps1
```

`BUILD_HAND_PLUGIN.ps1` first runs the deterministic gesture unit tests and
exports a hand-only `mlomega-reflexvision.aar`. Its release AAR contains only
the app launcher, MediaPipe gesture pipelines, state machine and throttle; no
ASR, ONNX, translation or audio service is compiled.

`BUILD_XREAL_LENS_PROBE_AAR.ps1` compiles the checked-in Java lens bridge and
packages a developer-supplied `libnr_service.so` from the matching licensed
ControlGlasses distribution. The resulting
`Assets/Plugins/Android/xreal-private-lens-probe.aar` is intentionally ignored
by Git, just like the proprietary XREAL SDK archive.

`BUILD_XREEL_OS.ps1` deliberately runs Unity twice. Pass 1 imports the local
XREAL/AR Foundation packages and sets `XREAL_SDK_PRESENT`; pass 2 compiles and
packages the real XREAL adapter. It then copies the verified artifact to
`releases/XReelOs.apk` and regenerates `releases/SHA256SUMS.txt`.

Equivalent Unity invocation:

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.0.23f1\Editor\Unity.exe" `
  -batchmode -nographics -quit -buildTarget Android `
  -projectPath "$PWD\unity" `
  -executeMethod MLOmega.XR.Editor.AndroidBuildXreal.PrepareDefines `
  -logFile "$PWD\unity\xreelos-prepare.log"

& "C:\Program Files\Unity\Hub\Editor\6000.0.23f1\Editor\Unity.exe" `
  -batchmode -nographics -quit -buildTarget Android `
  -projectPath "$PWD\unity" `
  -executeMethod MLOmega.XR.Editor.AndroidBuildXreal.BuildXReelOsApk `
  -logFile "$PWD\unity\xreelos-build.log"
```

Do not omit `-buildTarget Android`: `XREALSettings.InitialInputSource` and other
fields are conditionally compiled under `UNITY_ANDROID`; opening a fresh clone
as a Windows player first produces misleading missing-field errors.

Output:

```text
unity/build/android/XReelOs.apk
package: com.spendinfr.xreelos
entry:   ai.nreal.activitylife.NRXRActivity
```

## Required build invariants

- graphics API is OpenGL ES 3, not Vulkan;
- XREAL loader is active for Android;
- stereo is single-pass instanced and tracking is 6DoF;
- controller remains the XREAL input source; Eye/MediaPipe gestures are a
  parallel application layer;
- multiresume/NRXRActivity bootstrap is retained;
- transparent optical background is enabled;
- only one XREAL 3D relaunch occurs after a cinema/desktop handoff;
- WebView is enabled for the OS build without adding audio/ONNX runtimes;
- the generated player contains `XrWebVrBridge`, Media3 HLS support and the
  `XrLabWebVr` shader;
- the XReel launcher icon is imported from `Assets/Brand/XReelOsIcon.png`;
- `scripts/xreal-compat` resolves inside this repository, never from a private
  parent checkout;
- the app package remains independent from every private product package.

## Release verification

```powershell
$apk = ".\unity\build\android\XReelOs.apk"
$analyzer = "$env:LOCALAPPDATA\Android\Sdk\cmdline-tools\latest\bin\apkanalyzer.bat"
& $analyzer manifest application-id $apk
& $analyzer files list $apk | Select-String "hand_landmarker|onnx|sherpa|webrtc|XrWebVr"
Get-FileHash $apk -Algorithm SHA256
```

Expected: package `com.spendinfr.xreelos`, hand model present, and no MLOmega
ONNX/Sherpa/WebRTC/live-transport libraries.

The checked-in release is verified by `releases/SHA256SUMS.txt`.

## Hardware gate before release

On S24 + One Pro + Eye:

1. transparent stereo dock, stable 6DoF;
2. pinch, palm, two palms, thumb, fist and index scroll;
3. window move/depth/tilt/resize/close and restored layout;
4. Chrome, YouTube, Reddit and Spotify spatial surfaces;
5. XR keyboard text input;
6. Netflix and Prime playback through protected cinema and clean 3D return;
7. Moonlight 3D, 2D desktop, return to 3D, then pinch/palm again;
8. no `NativeRGBCamera Start Failure` after return;
9. no microphone audio in first-person recording;
10. clean launch after DeX preflight and Shizuku restart.
11. internal browser enters/exits a direct VR180/SBS stream, with head tracking,
    pause/play and seek controls, then returns to a usable spatial browser.

The baseline from which the community build was extracted passed the critical
Moonlight 2D -> XREAL 3D -> MediaPipe restart gate on 11 August 2026.

The complete chronology, renderer diagnostics, Samsung external-display
recovery and VR-specific release gates are in
[BUILD_GUIDE_XREAL.md](BUILD_GUIDE_XREAL.md).
