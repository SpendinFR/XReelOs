# Architecture

## Runtime flow

```text
XREAL One Pro + Eye
  -> XREAL SDK 3.1 XR session / 6DoF / RGB camera
  -> EyeCaptureSource
  -> GestureBridge + HandLowLightEnhancer
  -> Android MediaPipe Hand Landmarker (reflexvision)
  -> XrealNativeHandPointer (gaze + gesture arbitration)
  -> WorldCreatorController OS mode
       -> dock / settings / quick menu / window blocks
       -> WorldCreatorLabShell
            -> Android spatial app hosts
            -> internal browser + XR keyboard
            -> raw media discovery / Android external texture
            -> Unity VR180/VR360 presenter
            -> protected cinema handoff
            -> Moonlight 3D / 2D handoff
```

## OS-only boundary

`BuildCommunityOsScene` creates a dedicated scene and sets `_osOnlyMode` before
runtime. In that mode the creator workspace, map import/export and creator-mode
provider are not started. The build has a separate Android package
`com.spendinfr.xreelos`; it cannot overwrite the private product application.

The public repository contains no Brain2/Memory service, database, cloud client
configuration, user profile or conversation data.

## Hand tracking

The Android bridge is deliberately in the repository so it is reviewable:

- Kotlin/Android code under `unity/android/reflexvision`;
- its Unity-facing AAR under `Assets/Plugins/Android`;
- C# scheduling and state in `GestureBridge.cs`;
- pointer and gesture arbitration in `XrealNativeHandPointer.cs`.

Eye frames are consumed in memory. Debug frame export is disabled in the OS
scene. The MediaPipe model is copied from `unity/models` into StreamingAssets and
then to app-private storage on first launch.

## Android app surfaces

Non-protected applications use Shizuku-backed Android task/display slots which
are composed into Unity spatial windows. Protected services use a separate
system-mirror/cinema transition because Widevine/secure surfaces cannot be
sampled into an ordinary Unity texture. The in-cinema dock remains a small system
surface, then XREAL's 3D activity is relaunched once and MediaPipe is restarted.

The shell never republishes third-party app icons. `XrLabAndroidIconLoader`
asks Android's `PackageManager` for the icon of an installed package and turns
that drawable into a transient Unity sprite; the dock falls back to a local
glyph if the package or icon is unavailable.

## Immersive web video

The VR browser has a separate pipeline from the protected cinema bridge:

```text
TLabWebView page
  -> JavaScript media probe and raw URL/blob discovery
  -> XrWebVrBridge (Android Media3 player)
  -> SurfaceTexture / external OES texture
  -> XrLabWebVrStreamTexture
  -> XrLabWebVrPresenter + XrLabWebVr.shader
  -> mono, SBS, VR180 or VR360 per-eye rendering
```

Keeping the decoder surface in Android and importing its external texture
avoids a CPU readback/copy per frame. The presenter owns head-tracked projection,
layout selection, seek/play/pause and the two-palm exit gesture. It deliberately
does not bypass DRM: protected Netflix/Prime playback remains in the system
cinema path.

### v2 thermal handoff

The source page remains fully alive while the stream is discovered and while
Media3 warms up. Only after the imported texture exists and reports a real video
size does v2 enter the presenter and perform the reversible handoff:

```text
first valid Media3 frame
  -> explicitly pause playing HTML video/audio
  -> WebView.onPause + hide the source WebView on the phone
  -> pause/hide TLab's source capture host
  -> Media3 remains the sole immersive video decoder
```

The normal exit still disposes the decoder and recreates the browser from its
saved URL/cookie store. A failed transition restores the original WebView. The
raw-WebView fallback is intentionally unchanged because it is itself the video
source and therefore cannot be suspended.

The OS scene also consumes XREAL's native Y/U/V Eye planes directly in the
existing MediaPipe path instead of first creating a full-resolution RGB render
texture. If native I420 submission fails once, RGB conversion is restored for
the rest of that session automatically.

## Rendering contract

The hardware-proven XREAL template contract is retained:

- Android target selected before compilation;
- OpenGL ES 3;
- single-pass instanced stereo;
- built-in render pipeline for the community scene;
- transparent optical background;
- runtime shaders explicitly included rather than discovered only by
  `Shader.Find`;
- DeX must not own the external display.

The build postprocessor injects the Java VR bridge and Media3 dependencies only
for isolated Lab packages and `com.spendinfr.xreelos`. This package gate is a
release invariant: a build that lacks `XrWebVrBridge` can show the browser but
cannot enter native immersive playback.

## Privacy boundary

There is no always-on dictation component and first-person recording is created
with audio disabled. The Android manifest still contains permissions required by
the XREAL RGB/video-capture stack; permission presence does not mean XReel OS is
recording microphone audio. Contributors must preserve this distinction and
document any future audio feature explicitly.
