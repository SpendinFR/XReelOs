package com.xreal.glasses.api;

/** Minimal JNI declaration matching the private XREAL Control class. */
public final class Control {
    private static final Control INSTANCE = new Control();

    private Control() {}

    public static Control getInstance() { return INSTANCE; }

    public native int nativeGetDisplayBrightnessLevel();
    public native int nativeGetDisplayBrightnessLevelCount();
    public native boolean nativeSetDisplayBrightnessLevel(int level);
    public native int nativeGet2D3DMode();
    public native boolean nativeSet2D3DMode(int mode);
    public native int nativeGetDpCurrentEdid();
    public native boolean nativeSetDpCurrentEdid(int mode);
    public native boolean nativeSetDpInputMode(int mode);
    public native int nativeGetEcLevel();
    public native int nativeGetEcLevelCount();
    public native boolean nativeSetEcLevel(int level);
    public native boolean nativeRecenterGlasses();
}
