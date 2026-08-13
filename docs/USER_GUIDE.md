# XReel OS user guide

This guide describes the controls that are easy to miss when using XReel OS for
the first time. Controls are context-sensitive: two palms open the dock in the
normal spatial shell, but the same gesture exits an immersive VR video.

## 1. Interaction model

The pointer is the center head-gaze ray. Look at a control, then pinch thumb and
index to select it. The XREAL Eye supplies the outward RGB image used by
MediaPipe to recognize the hand gesture; it does not measure the user's eyeball
direction.

Keep the hand inside the Eye camera field and use even room lighting. Window
chrome deliberately remains invisible until the corresponding hit area is
looked at.

## 2. Start, resume and quit

XReel OS normally starts on the spatial dock. If a previous layout was saved,
the startup card offers:

- **Resume**: restore the saved browser/application layout;
- **Dock**: discard that session layout, close its hosted surfaces and start
  from a clean dock.

Use the power control in Settings to quit cleanly. XReel saves window/session
state, stops an active first-person recording and releases protected Android
application surfaces. Android pause/termination also triggers a best-effort
session save, but the explicit quit control is the preferred path.

## 3. Gesture reference

| Gesture | Result in the spatial shell |
| --- | --- |
| Look + short pinch | Click the looked-at control |
| Look + held pinch | Drag a window handle, page or slider |
| One open palm | Recenter the active window |
| One open palm with no active window | Restore the last closed window |
| Two open palms | Open and center the application dock |
| Thumb up | Open the compact quick menu near the gaze direction |
| Closed fist | Put windows/interaction into standby; repeat to restore |
| Pointing index with deliberate vertical stroke | Scroll the active surface |

Pinch has priority over fist recognition so that a deep pinch should not put the
shell into standby. Index scrolling requires a deliberate stroke; merely
raising an index is not intended to scroll.

## 4. Window controls

Look just outside a window to reveal its controls:

- top-right **X**: close that window;
- bottom move bar: pinch and move the hand in the viewing plane; turning the
  head while holding carries the window around the user;
- depth handle: pinch and move horizontally to bring the window closer or push
  it farther away;
- tilt handle: a predominantly horizontal motion turns the window left/right;
  a predominantly vertical motion pitches it up/down;
- lower corner handles: proportional resize;
- **H/W** control: independent width/height editing where supported;
- portrait and landscape controls: apply deterministic layouts rather than
  stretching the old window;
- aspect menu: **AUTO** restores the original factory footprint; 16:9, 21:9 and
  32:9 apply fixed physical formats;
- block lock: join the active window to the shared multi-window block. The block
  uses up to three windows per row and bends the side windows toward the user.

Position, scale and supported format choices are saved per surface. A saved
layout is not the same thing as a persistent physical-room anchor; see
[Known limitations](KNOWN_LIMITATIONS.md).

## 5. Quick menu and modes

Show a thumb up to open the compact menu. It exposes dock access, Settings,
keyboard, hand profile, electrochromic state and the window mode.

The window mode cycles through:

- **6DoF anchor**: normal world-locked windows during the active XR session;
- **head follow**: windows preserve a head-relative layout;
- **manual frozen anchor**: freezes the current group and hides manipulation
  chrome until another mode is selected.

The hand profiles are Off, Light and Reinforced. Reinforced processing can help
with a noisy low-light silhouette, but it cannot recreate hand detail that the
camera did not capture.

## 6. Head-only interaction

Head-only mode is an explicit fallback for moments when hands are unavailable.
In interaction mode, holding the head-gaze on a target fills a cyan circle. Once
it completes, the orange decision circle appears:

- remain still until the orange circle completes to click;
- begin moving the head during the orange phase to start a drag;
- stop moving and allow the release progress to complete to drop the item.

Passive mode hides the cursor and prevents accidental dwell clicks while
watching content. Looking at the quick-menu reveal area briefly wakes
interaction so passive mode can be changed without a hand.

## 7. Android application windows

Chrome/Google, YouTube, Reddit and Spotify use movable Android surfaces. Several
ordinary application slots can coexist. The rear face is intentionally static
glass; XReel does not decode and render the application a second time merely to
show live pixels from behind the panel.

If every Android window is black, check Shizuku before reinstalling the APK. If
one application is black, close that window and open it again from the dock.
Applications must be installed on the phone under one of the package names in
[Installation](INSTALLATION.md). Regional or beta variants may require an
adapter.

The XR keyboard works with the internal browser and the validated input routes.
It is not a universal Android input-method replacement for every protected or
custom native text field.

## 8. Netflix and Prime protected cinema

Netflix and Prime use Widevine-protected surfaces. Browse them in their spatial
window; protected playback then hands the glasses to the full-screen system
cinema path. This is intentionally different from an ordinary Unity 3D window.

While cinema is active:

1. lower the gaze toward the bottom edge and wait briefly for the discreet dock;
2. use its return, play/pause, supported seek, volume, XREAL brightness,
   electrochromic and size controls;
3. for recenter, select the control, look where the new center should be and let
   its countdown finish;
4. select **Return to XReel** to restore the 3D shell and gesture pipeline.

Do not use the glasses' physical X/2D-3D mode button as the normal return path.
A brief phone/system frame can be visible during a 3D-to-2D transition; that is
separate from a persistent DeX desktop or phone mirror.

Protected cinema cannot coexist visually with Unity spatial windows during
playback. Video quality is decided by the streaming service, account, device,
DRM/HDCP policy and network; the UI cannot promise 1080p or 4K.

## 9. Moonlight

Moonlight supports two paths:

- a movable spatial Android window;
- a full-screen 2D desktop surface for maximum compatibility.

The 2D desktop uses the same hidden return-dock idea as cinema. Return through
that dock so XReel can restore XREAL display ownership and restart gestures.
After return, the old spatial Moonlight surface is intentionally closed because
its Android surface is no longer valid.

If Samsung opens Moonlight on the phone, produces a black task or loses input,
close its XReel window and launch Moonlight again from the XReel dock. Sunshine
must be running on the host PC; Tailscale is needed only for the configured
remote route, not for a normal local-LAN session.

## 10. Internal browser and immersive VR

For a compatible direct, non-DRM video:

1. open **VR Browser**;
2. navigate to the site and start the actual video first;
3. select **VR**;
4. start with **AUTO**, then choose Mono/SBS and the appropriate flat, VR180 or
   VR360 projection if the source is doubled or projected incorrectly;
5. use the immersive play/pause and seek controls;
6. show **two open palms** to leave immersive VR and restore the browser.

AUTO is a heuristic, not metadata guaranteed by every site. YouTube's web
player is not a universal direct WebVR source. Authentication, advertisements,
script-replaced video elements and protected streams can prevent discovery or
make a site select the wrong element.

If immersive exit succeeds but the browser content itself is stale, close only
the browser window and reopen it from the dock; do not reset the entire XR
runtime first.

## 11. First-person recording

The recording control in Settings starts/stops a first-person Eye-camera video
and shows its active red state. Finished recordings are published to the Android
gallery. Microphone audio is deliberately disabled. Stopping a recording can
briefly pause and restart the shared Eye capture path, so hand gestures may take
a moment to return.

## 12. Physical controls and Bluetooth

Pair headphones, keyboard, mouse and gamepad with the S24. The phone, not the
glasses, is the Bluetooth host and routes audio/input to Android applications or
Moonlight.

Use XReel's brightness, electrochromic and recenter controls while the shell is
running. On the validated One Pro setup, pressing the glasses' physical X or
2D/3D mode control can move the panel out of the display mode expected by the XR
runtime.
