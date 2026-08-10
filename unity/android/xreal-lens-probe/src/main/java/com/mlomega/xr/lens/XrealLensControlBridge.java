package com.mlomega.xr.lens;

import android.app.Activity;
import android.content.Context;

import com.xreal.glasses.api.Control;
import com.xreal.glasses.api.Startup;

/** Fail-closed bridge used only by an explicitly assembled Atelier probe. */
public final class XrealLensControlBridge {
    private static boolean loadAttempted;
    private static boolean loaded;
    private static String loadError = "not_loaded";
    private static boolean initializationAttempted;
    private static String initializationError = "not_initialized";

    private XrealLensControlBridge() {}

    private static synchronized boolean ensureLoaded() {
        if (loadAttempted) return loaded;
        loadAttempted = true;
        try {
            System.loadLibrary("nr_service");
            loaded = true;
            loadError = "";
        } catch (Throwable error) {
            loaded = false;
            loadError = compact(error);
        }
        return loaded;
    }

    public static String probe() {
        if (!ensureLoaded()) return "ERR|load=" + loadError;
        try {
            Control control = Control.getInstance();
            int brightness = control.nativeGetDisplayBrightnessLevel();
            int brightnessCount = control.nativeGetDisplayBrightnessLevelCount();
            int ec = control.nativeGetEcLevel();
            int ecCount = control.nativeGetEcLevelCount();
            if (!validState(brightness, brightnessCount, ec, ecCount))
                return state("ERR", brightness, brightnessCount, ec, ecCount) +
                    "|service=not_initialized";
            return state("OK", brightness, brightnessCount, ec, ecCount);
        } catch (Throwable error) {
            return "ERR|probe=" + compact(error);
        }
    }

    /** Rewrites current values only. It must not visibly alter the glasses. */
    public static String validateCurrent() {
        if (!ensureLoaded()) return "ERR|load=" + loadError;
        try {
            Control control = Control.getInstance();
            if (!ensureServiceReady(control))
                return "ERR|init=" + initializationError + "|" + rawState(control);
            int brightness = control.nativeGetDisplayBrightnessLevel();
            int brightnessCount = control.nativeGetDisplayBrightnessLevelCount();
            int ec = control.nativeGetEcLevel();
            int ecCount = control.nativeGetEcLevelCount();
            if (!validState(brightness, brightnessCount, ec, ecCount))
                return state("ERR", brightness, brightnessCount, ec, ecCount) +
                    "|service=invalid";
            boolean brightnessOk =
                control.nativeSetDisplayBrightnessLevel(brightness);
            boolean ecOk = control.nativeSetEcLevel(ec);
            int brightnessAfter = control.nativeGetDisplayBrightnessLevel();
            int ecAfter = control.nativeGetEcLevel();
            if (!brightnessOk || !ecOk || brightnessAfter != brightness ||
                    ecAfter != ec)
                return "ERR|noop=b" + brightnessOk + ",ec" + ecOk +
                    ",readback=" + brightnessAfter + "/" + ecAfter;
            return state("VALID", brightnessAfter, brightnessCount,
                ecAfter, ecCount) + "|nb=" + brightnessOk + "|ne=" + ecOk;
        } catch (Throwable error) {
            return "ERR|noop=" + compact(error);
        }
    }

    public static String stepBrightness(int direction) {
        if (!ensureLoaded()) return "ERR|load=" + loadError;
        try {
            Control control = Control.getInstance();
            if (!ensureServiceReady(control))
                return "ERR|init=" + initializationError;
            int count = control.nativeGetDisplayBrightnessLevelCount();
            int current = control.nativeGetDisplayBrightnessLevel();
            if (count <= 0 || current < 0)
                return "ERR|brightness_state=" + current + "/" + count;
            int target = clamp(current + (direction < 0 ? -1 : 1), 0, count - 1);
            boolean nativeResult =
                control.nativeSetDisplayBrightnessLevel(target);
            int actual = control.nativeGetDisplayBrightnessLevel();
            if (actual != target)
                return "ERR|brightness_set=" + target + ",native=" +
                    nativeResult + ",actual=" + actual;
            return state("OK", actual, count,
                control.nativeGetEcLevel(), control.nativeGetEcLevelCount());
        } catch (Throwable error) {
            return "ERR|brightness=" + compact(error);
        }
    }

    public static String stepEc(int direction) {
        if (!ensureLoaded()) return "ERR|load=" + loadError;
        try {
            Control control = Control.getInstance();
            if (!ensureServiceReady(control))
                return "ERR|init=" + initializationError;
            int count = control.nativeGetEcLevelCount();
            int current = control.nativeGetEcLevel();
            if (count <= 0 || current < 0)
                return "ERR|ec_state=" + current + "/" + count;
            int target = clamp(current + (direction < 0 ? -1 : 1), 0, count - 1);
            boolean nativeResult = control.nativeSetEcLevel(target);
            int actual = control.nativeGetEcLevel();
            if (actual != target)
                return "ERR|ec_set=" + target + ",native=" + nativeResult +
                    ",actual=" + actual;
            return state("OK", control.nativeGetDisplayBrightnessLevel(),
                control.nativeGetDisplayBrightnessLevelCount(),
                actual, count);
        } catch (Throwable error) {
            return "ERR|ec=" + compact(error);
        }
    }

    /** Recenters the physical XREAL display through the same initialized
     *  GlassesControl service used by brightness and electrochromic controls. */
    public static String recenterGlasses() {
        if (!ensureLoaded()) return "ERR|load=" + loadError;
        try {
            Control control = Control.getInstance();
            if (!ensureServiceReady(control))
                return "ERR|init=" + initializationError;
            boolean nativeResult = control.nativeRecenterGlasses();
            return nativeResult ? "OK" : "ERR|native=false";
        } catch (Throwable error) {
            return "ERR|recenter=" + compact(error);
        }
    }

    /** Returns XREAL's native display mode, or -1 when the service is unavailable. */
    public static int getDisplayMode() {
        if (!ensureLoaded()) return -1;
        try {
            Control control = Control.getInstance();
            if (!ensureServiceReady(control)) return -1;
            int legacyMode = control.nativeGet2D3DMode();
            if (legacyMode >= 0) return legacyMode;
            // XREAL One/One Pro are routed as the "Ethernet series" by the
            // official GlassesControl SDK. Its NRServiceControl.get2D3D()
            // reads the active DP EDID instead of nativeGet2D3DMode().
            return control.nativeGetDpCurrentEdid();
        } catch (Throwable error) {
            try {
                // Keep the One-series fallback independent: an unavailable
                // legacy JNI entry must not hide a valid DP/EDID entry.
                return Control.getInstance().nativeGetDpCurrentEdid();
            } catch (Throwable ignored) {
                return -1;
            }
        }
    }

    /**
     * Switches the physical glasses DP mode. XREAL's official SDK constants use
     * 1 for 1920x1080 2D; One-series SDK stereo modes are read back and restored
     * rather than guessed here.
     */
    public static boolean setDisplayMode(int mode) {
        if (!ensureLoaded()) return false;
        try {
            Control control = Control.getInstance();
            if (!ensureServiceReady(control)) return false;
            int dpMode = -1;
            try {
                dpMode = control.nativeGetDpCurrentEdid();
            } catch (Throwable ignored) {}
            if (dpMode >= 0) {
                if (dpMode == mode) return true;
                // Mirrors NRServiceControl.set2D3D() from the official
                // GlassesControl app. Modes 5..8 are stereo EDIDs.
                boolean accepted = control.nativeSetDpCurrentEdid(mode);
                if (accepted) {
                    boolean stereo = mode >= 5 && mode <= 8;
                    control.nativeSetDpInputMode(stereo ? 1 : 0);
                }
                return accepted;
            }

            if (control.nativeGet2D3DMode() == mode) return true;
            boolean accepted = control.nativeSet2D3DMode(mode);
            // A successful request immediately removes the old DP display. Do
            // not turn that expected transition into a false failure by polling
            // the disappearing USB service here; the caller waits for the new
            // physical Display and verifies the mode after re-enumeration.
            return accepted;
        } catch (Throwable error) {
            return false;
        }
    }

    private static String state(String status, int brightness,
            int brightnessCount, int ec, int ecCount) {
        return status + "|b=" + brightness + "|bc=" + brightnessCount +
            "|ec=" + ec + "|ecc=" + ecCount;
    }

    private static synchronized boolean ensureServiceReady(Control control) {
        try {
            if (hasValidState(control)) return true;
            if (initializationAttempted) return false;
            initializationAttempted = true;

            Context context = unityActivity().getApplicationContext();
            Startup.nativeInitService(context);
            Startup.nativeSetServiceMode(1);
            Startup.nativeInitSetForegroundService(false);
            Startup.nativeStartService();
            boolean initialized = Startup.nativeGlassesInit();
            if (!initialized) {
                initializationError = "nativeGlassesInit=false";
                return false;
            }

            String nativeDir = context.getApplicationInfo().nativeLibraryDir;
            if (nativeDir != null && !nativeDir.isEmpty())
                Startup.nativeSetNativeLibraryPath(nativeDir + "/");

            // NRServiceControl performs the same startup synchronously, but
            // property availability can trail the USB init very briefly.
            for (int attempt = 0; attempt < 12; attempt++) {
                if (hasValidState(control)) {
                    initializationError = "";
                    return true;
                }
                Thread.sleep(100L);
            }
            initializationError = "properties_unavailable:" + rawState(control);
            return false;
        } catch (Throwable error) {
            initializationError = compact(error);
            return false;
        }
    }

    private static Activity unityActivity() throws Exception {
        Class<?> unityPlayer = Class.forName("com.unity3d.player.UnityPlayer");
        Object activity = unityPlayer.getField("currentActivity").get(null);
        if (!(activity instanceof Activity))
            throw new IllegalStateException("Unity activity unavailable");
        return (Activity) activity;
    }

    private static boolean hasValidState(Control control) {
        return validState(control.nativeGetDisplayBrightnessLevel(),
            control.nativeGetDisplayBrightnessLevelCount(),
            control.nativeGetEcLevel(), control.nativeGetEcLevelCount());
    }

    private static boolean validState(int brightness, int brightnessCount,
            int ec, int ecCount) {
        return brightness >= 0 && brightnessCount > 0 &&
            brightness < brightnessCount && ec >= 0 && ecCount > 0 &&
            ec < ecCount;
    }

    private static String rawState(Control control) {
        return state("RAW", control.nativeGetDisplayBrightnessLevel(),
            control.nativeGetDisplayBrightnessLevelCount(),
            control.nativeGetEcLevel(), control.nativeGetEcLevelCount());
    }

    private static int clamp(int value, int minimum, int maximum) {
        return Math.max(minimum, Math.min(value, maximum));
    }

    private static String compact(Throwable error) {
        String name = error.getClass().getSimpleName();
        String message = error.getMessage();
        if (message == null || message.isEmpty()) return name;
        return (name + ":" + message).replace('|', '/').replace('\n', ' ');
    }
}
