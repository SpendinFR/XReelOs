package android.window;

import android.app.ActivityManager;
import android.view.SurfaceControl;

import java.util.List;

/**
 * Compile-only signature stub for the hidden platform TaskOrganizer API.
 *
 * This class is never packaged in an APK. Android supplies the real class from
 * the boot class path at runtime; the stub only lets javac compile the isolated
 * Shizuku/shell experiment against the exact JVM descriptors it uses.
 */
public class TaskOrganizer {
    public TaskOrganizer() {}

    @SuppressWarnings("rawtypes")
    public List registerOrganizer() { return null; }

    public void unregisterOrganizer() {}

    public void onTaskAppeared(
            ActivityManager.RunningTaskInfo taskInfo,
            SurfaceControl leash) {}

    public void onTaskVanished(ActivityManager.RunningTaskInfo taskInfo) {}

    public void onTaskInfoChanged(ActivityManager.RunningTaskInfo taskInfo) {}
}
