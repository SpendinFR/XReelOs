package com.xreal.glasses.api;

import android.content.Context;

/** Minimal JNI declarations used by XREAL's own NRServiceControl startup. */
public final class Startup {
    private Startup() {}

    public static native void nativeInitService(Context context);
    public static native void nativeSetServiceMode(int mode);
    public static native void nativeInitSetForegroundService(boolean foreground);
    public static native void nativeStartService();
    public static native boolean nativeGlassesInit();
    public static native void nativeSetNativeLibraryPath(String path);
}
