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

If only Moonlight was launched on the phone, returned black or lost input, close
its XReel window and launch it again from the XReel dock. If every ordinary app
is black, repair Shizuku/display ownership instead of retrying each app.

## I cannot find the exit or the dock

- Normal shell: two open palms opens/centers the app dock.
- No active window: one open palm restores the last closed window.
- Immersive VR: two open palms exits VR and restores the browser.
- Netflix/Prime cinema or Moonlight full 2D: lower the gaze to reveal the hidden
  bottom dock and select **Return to XReel**.
- Head-only passive mode: look at the quick-menu reveal area briefly to wake the
  interaction cursor.

Avoid using the glasses' physical X/2D-3D control as XReel's return button. On
the validated setup it can move the panel out of the XR mode expected by the
running application.

## Gestures disappear after cinema/Moonlight

The correct return path relaunches `NRXRActivity` exactly once, waits for the XR
compositor, and rebuilds the MediaPipe graph. Two relaunches can stop the Eye
camera and cause `NativeRGBCamera Start Failure`. Capture logcat and look for:

```text
external display return: MediaPipe graph restarted
HandLandmarker ready (GPU/LIVE_STREAM, 25.0 fps)
```

If Samsung opened another old XREAL/Lab APK automatically, disable that app's
autolaunch or uninstall the stale test build. Only one application should own
the XREAL session and Eye camera.

## App exits immediately or opens an empty XR world

Capture a clean log before rebuilding. If it contains `RGB=0`, `grayscale=0`,
`get device config failed`, `NativeRGBCamera Start Failure` or a native 6DoF
crash, the Eye/perception runtime did not expose its camera configuration. This
is upstream of menus and MediaPipe. Record the phone, Android/One UI, glasses
firmware, ControlGlasses and SDK versions with the full ADB log.

Do not try to fix this class of failure by changing shaders, declaring fake
camera counts or repeatedly launching the XR activity. See
[Known limitations](KNOWN_LIMITATIONS.md).

## Tracking asks to look around

That message concerns 6DoF environmental tracking. Add ambient light and look at
textured, non-reflective surfaces. It is independent from whether MediaPipe sees
your hand.

## Shizuku stopped after reboot

This is expected on non-rooted Android. Re-enable Wireless debugging, open
Shizuku and press Start. Pairing usually remains remembered.

## Phone becomes hot or Samsung closes applications

The phone powers the glasses while running Unity stereo/6DoF, Eye hand
inference and video decode. Close unused windows, leave reinforced low-light
mode when it is unnecessary and lower a 5K/6K/8K VR or high-bitrate Moonlight
stream. The validated baseline is 60 Hz. Let the phone cool after a Samsung
thermal warning rather than repeatedly relaunching the application.

## VR image is doubled, flat or incorrectly projected

Start the actual video before selecting VR. Try AUTO first, then manually choose
Mono/SBS and the matching flat, VR180 or VR360 projection. AUTO is heuristic and
site players can expose an advertisement or preview instead of the main stream.
YouTube's web player and protected DRM sites are not universal direct VR
sources.

## Unity says XREAL SDK field is missing

Start Unity/build with `-buildTarget Android`. Fresh clones opened first as a
Windows target compile out Android-only XREAL settings.

For full controls and context-specific exits, read the [User guide](USER_GUIDE.md).
