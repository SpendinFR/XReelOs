package com.xreal.glassesdisplayplugevent.display;

import android.graphics.Point;
import android.hardware.display.DeviceProductInfo;
import android.os.Build;
import android.view.Display;

import java.lang.reflect.Method;
import java.util.HashMap;
import java.util.Map;

/**
 * Compatibility replacement for XREAL SDK 3.1's
 * GlassesDisplayPlugEvent-2.4.2 DisplayModel.
 *
 * The vendor implementation accepts a wired display only when Display.getName()
 * contains the literal "HDMI". Recent Samsung firmware exposes the EDID product
 * name ("One Pro") instead. Preserve the vendor API and product map, but accept
 * displays whose EDID manufacturer identifies XREAL (MRG/NRL).
 */
public class DisplayModel {
    private static final String DISPLAY_VID = "MRG";
    private static final String DISPLAY_VID_OLD_LIGHT = "NRL";
    private static final String DISPLAY_PID_OLD_LIGHT = "12594";
    private static final Map<String, Integer> DISPLAY_PID_MAP = new HashMap<>();

    static {
        DISPLAY_PID_MAP.put("12593", 1);
        DISPLAY_PID_MAP.put(DISPLAY_PID_OLD_LIGHT, 2);
        DISPLAY_PID_MAP.put("12596", 4);
        DISPLAY_PID_MAP.put("12597", 3);
        DISPLAY_PID_MAP.put("12598", 5);
        DISPLAY_PID_MAP.put("16640", 6);
        DISPLAY_PID_MAP.put("16641", 7);
    }

    private final boolean isHdmi;
    private final int width;
    private final int height;
    private int nrealDisplay = -1;

    public DisplayModel(Display display) {
        this.isHdmi = isWiredXrealDisplay(display);
        Point size = new Point();
        if (display != null) {
            display.getRealSize(size);
        }
        this.width = size.x;
        this.height = size.y;
        if (this.isHdmi) {
            this.nrealDisplay = identifyXrealDisplay(display);
        }
    }

    public boolean isHdmi() {
        return this.isHdmi;
    }

    public int getWidth() {
        return this.width;
    }

    public int getHeight() {
        return this.height;
    }

    public int getNrealDisplay() {
        return this.nrealDisplay;
    }

    private boolean isWiredXrealDisplay(Display display) {
        if (display == null) {
            return false;
        }
        String name = display.getName();
        if (name != null && name.contains("HDMI")) {
            return true;
        }
        if ("eva".equals(Build.PRODUCT)) {
            try {
                Method method = display.getClass().getMethod(
                    "isRealDisplayConnected");
                method.setAccessible(true);
                Object value = method.invoke(display);
                if (value instanceof Boolean && ((Boolean) value)) {
                    return true;
                }
            } catch (Exception ignored) {
                // EDID remains authoritative on current Samsung hosts.
            }
        }
        return identifyXrealDisplay(display) >= 0;
    }

    private int identifyXrealDisplay(Display display) {
        if (display == null) {
            return -1;
        }
        try {
            if (Build.VERSION.SDK_INT >= 31) {
                DeviceProductInfo info = display.getDeviceProductInfo();
                if (info == null) {
                    return -1;
                }
                return matchEdid(
                    info.getName(),
                    info.getProductId(),
                    info.getManufacturerPnpId());
            }
            Method getInfo = display.getClass().getMethod(
                "getDeviceProductInfo");
            getInfo.setAccessible(true);
            Object info = getInfo.invoke(display);
            if (info == null) {
                return -1;
            }
            Class<?> type = info.getClass();
            String productId = (String) type.getMethod(
                "getProductId").invoke(info);
            String manufacturer = (String) type.getMethod(
                "getManufacturerPnpId").invoke(info);
            String name = (String) type.getMethod("getName").invoke(info);
            return matchEdid(name, productId, manufacturer);
        } catch (Exception ignored) {
            return -1;
        }
    }

    private int matchEdid(
        String name,
        String productId,
        String manufacturerPnpId) {
        if (DISPLAY_VID_OLD_LIGHT.equals(manufacturerPnpId)
            && DISPLAY_PID_OLD_LIGHT.equals(productId)) {
            return 1;
        }
        if (DISPLAY_VID.equals(manufacturerPnpId)) {
            Integer type = DISPLAY_PID_MAP.get(productId);
            return type == null ? 0 : type;
        }
        return -1;
    }

    @Override
    public String toString() {
        return "DisplayModel{isHdmi=" + this.isHdmi
            + ", width=" + this.width
            + ", height=" + this.height
            + ", nrealDisplay=" + this.nrealDisplay + '}';
    }
}
