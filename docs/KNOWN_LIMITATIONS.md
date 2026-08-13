# Known limitations and roadmap

This page separates current architectural limits from recoverable setup errors
and future work. Read it before treating a black secure surface, unsupported
headset or high-resolution thermal shutdown as the same bug.

## Hardware and compatibility

- The validated matrix is Galaxy S24, Android 16 / One UI 8, XREAL One Pro +
  Eye, the firmware/ControlGlasses versions recorded in the README and XREAL SDK
  3.1.0.
- The current release requires the XREAL Eye path. Without Eye, the validated
  6DoF and application-level MediaPipe gesture pipeline are absent; this is not
  a phone-only or glasses-only build.
- XREAL One + Eye is a reasonable port target but was not hardware-validated by
  this project. Other XREAL glasses/phones require a complete XR, camera,
  display and input regression pass.
- VITURE and other vendors are not supported by this APK. Their display/runtime
  providers would require a port; changing an Android package name is not
  sufficient because XReel uses the XREAL Unity SDK lifecycle.
- XREAL's SDK 3.1 notes name Beam Pro and S25 as tested hosts, while SDK 3.0
  listed S24. The successful S24 result in this repository is project evidence,
  not an XREAL certification claim.

On Android 16/One UI 8-class Samsung software, XREAL engineering is
investigating cases where the runtime exposes no Eye camera descriptors and a
6DoF native crash follows. Typical logs include `RGB=0`, `grayscale=0`,
`get device config failed` or `NativeRGBCamera Start Failure`. This is not an
ordinary menu, shader or pinch bug. Preserve the full ADB log and report the
phone, firmware, ControlGlasses and SDK versions before changing application UI
code.

## Tracking and interaction

- The pointer is head gaze plus a MediaPipe-recognized physical pinch. There is
  no optical eye-gaze sensor in XREAL Eye.
- Hand tracking is monocular application-level tracking, not the native
  multi-camera hand/depth stack available on other devices. Lighting, occlusion,
  motion blur and the Eye field of view affect recognition.
- Reinforced low-light processing can improve contrast but adds work and cannot
  recover missing visual information.
- Index scrolling and fist wake are heuristic gestures. They were validated on
  the reference user/setup but need wider user testing for hand shapes,
  handedness and lighting.
- `Look around` is a 6DoF environmental-feature warning. It can appear even when
  the hand remains visible. Better room light and textured surroundings help.

## Spatial persistence

- Windows are world-locked during a healthy 6DoF session.
- Saved placement restores the layout relative to the new session/head pose. It
  does not guarantee that an object reappears at the same physical wall or table
  after a restart.
- Persistent object-by-object anchors, room relocalization, plane/depth mapping
  and the separate world Atelier remain future hardware/runtime work.
- Manual frozen and head-follow modes are interaction fallbacks, not replacements
  for persistent spatial-anchor recognition.

## Android applications and DRM

- XReel recognizes a documented package list. Adding an arbitrary installed app
  is not yet an automatic app-store flow: it can require package/activity policy,
  a dock entry and either ordinary-surface or protected-cinema routing.
- The XR keyboard is not guaranteed to inject text into every custom native
  Android field.
- Widevine-protected Netflix/Prime pixels cannot be sampled into a normal Unity
  texture. Protected playback uses a separate full-screen system cinema and
  therefore cannot coexist visually with multiple Unity 3D windows.
- Streaming resolution is controlled by the provider, DRM/HDCP eligibility,
  account, source and network. A 1080p/4K selector or source label is not proof
  that every eye/display path receives that resolution.
- Samsung can occasionally launch an Android task on the phone or return a stale
  black surface. Close/reopen that XReel window after confirming Shizuku rather
  than rebuilding Unity.
- Shizuku is non-root and normally stops after every phone reboot. Android does
  not allow XReel to silently bypass its initial authorization/start model.

## Browser and immersive VR

- Direct stream discovery is site-specific. Login, advertisements, expiring
  URLs, JavaScript player replacement and DRM can change behavior without an APK
  update.
- AUTO projection is heuristic. Some sources require manual Mono/SBS and
  VR180/VR360 selection; stereo strength also depends on how the source was
  authored.
- YouTube's web player is not a universal VR source. Sites exposing a direct
  non-DRM stream are the intended path.
- The current browser can also render on the phone while feeding the glasses.
  Avoiding that duplicate phone presentation is a future thermal optimization.
- The browser is not a complete replacement for Chrome: complex pop-ups, cookie
  layers and unusual nested scrolling can remain site-specific.

## Performance and thermal behavior

- 60 Hz is the hardware-validated baseline. Selecting 90/120 Hz in
  ControlGlasses does not prove Unity, Eye capture, MediaPipe and external
  Android surfaces all sustain that rate.
- The S24 simultaneously powers the glasses, runs Unity stereo/6DoF, processes
  Eye frames and can decode application or VR video. Warm operation is expected.
- Long 5K/6K/8K VR sessions, several application surfaces or reinforced hand
  processing can trigger Samsung thermal throttling or OS application closure.
  Lower the stream resolution, close unused windows, pause and cool the phone.
- Moonlight quality and latency depend on Sunshine encoder settings, the PC,
  Wi-Fi/Tailscale route and decoder load. 4K is not automatically the best mobile
  profile.
- 90 Hz, 120 Hz and sustained high-resolution VR require a complete frame-pacing,
  gesture latency and thermal validation before becoming defaults.

## Deliberately out of scope in this public OS build

- the Memory/Brain2 product backend and private user data;
- the world-map/anchor authoring Atelier and its GLB asset workflow;
- a universal Android app installer and automatic dock-policy generator;
- SteamVR/OpenXR PC mode;
- native optical eye tracking;
- safety-critical navigation or driving use.

## Roadmap candidates

1. remove the duplicate phone presentation during immersive VR;
2. profile a real 90 Hz path without regressing camera or gesture scheduling;
3. add user-approved dynamic application discovery and policy selection;
4. improve generic Android text-input compatibility;
5. harden Samsung/Moonlight task recovery;
6. improve stream metadata/projection calibration across VR sites;
7. add persistent relocalization only when the hardware/runtime gate is proven;
8. validate XREAL One + Eye, more Samsung hosts and future XREAL runtimes.
