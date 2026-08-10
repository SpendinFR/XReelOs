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
            -> XR keyboard
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

## Privacy boundary

There is no always-on dictation component and first-person recording is created
with audio disabled. The Android manifest still contains permissions required by
the XREAL RGB/video-capture stack; permission presence does not mean XReel OS is
recording microphone audio. Contributors must preserve this distinction and
document any future audio feature explicitly.
