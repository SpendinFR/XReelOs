# XReel OS 2.0 VR thermal candidate

This APK is a focused experimental build for immersive-browser thermal testing.
It is **not** promoted over the hardware-validated v1 release yet.

## What changed

After Media3 has imported a valid video frame, v2:

1. explicitly pauses every playing HTML video/audio element in the source page;
2. pauses Chromium's WebView and TLab's source capture surface;
3. hides only the source-site preview at the top of the phone; XReel return
   controls remain available;
4. keeps Media3 as the sole immersive decoder;
5. skips the full-resolution Eye YUV-to-RGB GPU blit while MediaPipe successfully
   consumes the same native Y/U/V planes as v1.

No source is suspended during stream discovery or Media3 warm-up. The normal
browser fallback remains unchanged. A failed handoff restores the browser, and
one failed native-I420 gesture submission restores the original RGB path.

## What is verified

- Unity C# and Android Java compilation succeeded with Unity 6000.0.23f1;
- package: `com.spendinfr.xreelos`;
- version: `2.0.0`, code `2`;
- Media3 bridge method and hand model are present in the APK;
- v1 APK and hash remain unchanged.

## What still requires hardware testing

1. normal browser navigation, login and keyboard before VR;
2. direct VR entry, image/projection, seek and play/pause;
3. no site preview in the upper phone area after VR starts;
4. pinch and two-palm VR exit;
5. restored browser after exit and after a refused stream;
6. 20-30 minutes at the same source resolution used with v1, recording phone
   temperature, throttling warnings and frame stability.

Lower load is expected because three real pieces of duplicate work are removed.
The exact temperature reduction is unknown until this matrix is run. High-resolution
Media3 decode, Unity stereo/6DoF and MediaPipe remain active and can still warm or
throttle the phone.

## Install

```powershell
$adb = "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe"
& $adb install -r ".\releases\XReelOs-v2.apk"
.\scripts\PREPARE_XREEL_OS.ps1
```

The v1 artifact remains at `releases/XReelOs.apk`.
