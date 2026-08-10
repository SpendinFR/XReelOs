# XReel OS hand-tracking plugin

This Android library is the native half of XReel OS hand interaction. XREAL Eye
frames are acquired by Unity, converted to bitmaps and submitted to MediaPipe.
The result is sent back to Unity through `GestureCallbacks`.

Included public paths:

- `EyePinchPipeline`: hand landmarks and product gestures at up to 25 FPS;
- `GesturePipeline`: generic MediaPipe gesture recognizer path;
- `GestureStateMachine`: deterministic pinch/palm/swipe hysteresis;
- `FrameThrottle`: bounded frame scheduling;
- `AppLauncher`: Android package/intents used by spatial application windows.

The module does not open a microphone and contains no speech recognition,
keyword spotting, translation, ONNX or Memory backend.

Build and export the AAR from the repository root:

```powershell
.\scripts\BUILD_HAND_PLUGIN.ps1
```

The script writes `mlomega-reflexvision.aar` and its MediaPipe dependencies to
`unity/Assets/Plugins/Android/`.
