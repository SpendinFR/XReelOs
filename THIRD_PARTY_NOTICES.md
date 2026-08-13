# Third-party notices

XReel OS is MIT-licensed, but dependencies remain under their own licenses and
terms.

- **XREAL SDK for Unity** — proprietary, downloaded separately from XREAL and
  governed by XREAL's API terms and privacy policy. The SDK archive is not in
  this Git repository. The release APK necessarily contains XREAL runtime
  components permitted by the developer distribution terms accepted at download.
- **ControlGlasses native service** — proprietary. Its standalone vendor binary
  and generated AAR are not committed as source inputs. Developers must supply
  a matching licensed `libnr_service.so` locally to build lens, electrochromic
  and physical display-mode controls. A built APK necessarily packages the
  runtime component under the vendor terms accepted by its distributor.
- **MediaPipe / MediaPipe Tasks Vision** — Apache License 2.0, Google.
- **TLabWebView** and **TLabVKeyborad** — MIT License, copyright tlabaltoh. Their
  license texts are retained alongside the vendored source.
- **Unity packages** — governed by Unity package/editor licenses.
- **AndroidX Media3, Kotlin, Guava, OkHttp and Google Android libraries** —
  retain their respective Apache/BSD notices and licenses. Media3 is used by
  the direct immersive web-video bridge.

The following projects were research references and are not represented as
being authored by this repository:

- XREALSDKTemplate and MixedRealityToolkit-Unity-XREALSDK by dengxian-xreal;
- Xreal-tools by nudou350;
- hand-tracking-streamer by wengmister;
- PortalPad by Smart-Home-User.
- vr2xr by skarian and mpv-android-vr by the mpv-android-vr contributors.

Application names, icons and trademarks such as YouTube, Netflix, Spotify,
Reddit, Prime Video, Moonlight, Samsung and XREAL belong to their respective
owners. XReel OS is not endorsed by them.

The XReel OS launcher artwork in `unity/Assets/Brand/XReelOsIcon.png` is an
original project asset and does not embed a third-party application mark.
