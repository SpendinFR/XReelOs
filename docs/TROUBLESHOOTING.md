# Troubleshooting

## DeX taskbar, phone screen or duplicated display

DeX still owns the external display. Disable it in Samsung settings and run
`scripts/PREPARE_XREEL_OS.ps1`. Confirm `SecondaryLauncher`, `dexservice`,
`mode=freeform` and `name=Desk` are absent from the external-display task.

## Magenta/violet background

Magenta is a missing or unsupported Unity shader, not a normal optical clear
color. Keep the proven built-in-pipeline scene, runtime shader inclusion and
transparent background. Do not enable URP post-processing with an incomplete
pipeline asset.

## App window is black

Check in this order:

1. the app is installed under the expected package name;
2. Shizuku is running and XReel OS is authorized;
3. DeX is disabled;
4. the app is not a protected DRM surface that requires cinema mode;
5. close stale XReel OS windows and retry — do not kill unrelated Shizuku users.

## Gestures disappear after cinema/Moonlight

The correct return path relaunches `NRXRActivity` exactly once, waits for the XR
compositor, and rebuilds the MediaPipe graph. Two relaunches can stop the Eye
camera and cause `NativeRGBCamera Start Failure`. Capture logcat and look for:

```text
external display return: MediaPipe graph restarted
HandLandmarker ready (GPU/LIVE_STREAM, 25.0 fps)
```

## Tracking asks to look around

That message concerns 6DoF environmental tracking. Add ambient light and look at
textured, non-reflective surfaces. It is independent from whether MediaPipe sees
your hand.

## Shizuku stopped after reboot

This is expected on non-rooted Android. Re-enable Wireless debugging, open
Shizuku and press Start. Pairing usually remains remembered.

## Unity says XREAL SDK field is missing

Start Unity/build with `-buildTarget Android`. Fresh clones opened first as a
Windows target compile out Android-only XREAL settings.
