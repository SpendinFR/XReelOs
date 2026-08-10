# Interaction reference

## Eye + MediaPipe path

XREAL SDK owns the XR session and Eye RGB camera. `EyeCaptureSource` exposes
ephemeral frames to `GestureBridge`; the Android `reflexvision` bridge runs
MediaPipe Hand Landmarker; `XrealNativeHandPointer` combines landmarks with the
head/gaze ray. No XREAL native hand-tracking payload is required on One Pro + Eye.

The active profile targets 25 inference frames per second at a 768-pixel maximum
dimension. Standby uses a low-rate sentinel so the same fist can wake gestures.

## Gestures

- **Pinch:** thumb/index closes to press. Release ends the click. Holding while
  moving controls window handles, page drag and sliders.
- **Open palm:** recenters the active window. With no active window, restores the
  last closed surface.
- **Two open palms:** opens and centers the app dock.
- **Thumb up:** opens the compact quick menu containing settings, keyboard,
  status and window controls.
- **Closed fist:** closes/sleeps the active layout and reduces hand inference;
  repeat to restore it.
- **Pointing index:** a deliberate vertical stroke scrolls the active surface.

Pinch intent has priority over fist classification to prevent a deep pinch from
putting the shell into standby.

## Window chrome

Chrome is gaze-revealed and normally hidden:

- top-right close;
- bottom move bar;
- depth handle;
- tilt handle;
- proportional corner resize;
- free width/height and aspect/orientation controls;
- optional window-block lock.

Window pose and size are saved per surface. Block mode arranges up to three
windows per row, bends side windows toward the user, and adds extra rows when
needed.

## Head-only mode

When hands are unavailable, enable head-only interaction in the quick/settings
menu. A stationary gaze arms the pointer, then dwell performs click or drag with
visible progress. Passive mode hides the cursor during ordinary viewing. Looking
at the quick-menu reveal area wakes interaction so the mode can be disabled
without a hand.

## Low light

The off/light/strong profiles alter only the ephemeral image passed to hand
inference. Strong mode uses bounded enhancement and does not save frames. It can
improve a noisy hand silhouette but cannot recover information absent from the
Eye camera. Use even illumination whenever possible; tracking-loss messages are
about 6DoF environment features, not necessarily hand visibility.
