#include <jni.h>
#include <android/log.h>
#include <android/native_window_jni.h>
#include <android/surface_control.h>
#include <dlfcn.h>

#include <algorithm>
#include <string>

namespace {

constexpr const char* kTag = "MLOmegaSecureTaskProbe";

ASurfaceControl* g_mirror_parent = nullptr;

using ASurfaceControlFromJava = ASurfaceControl* (*)(JNIEnv*, jobject);

jstring Result(JNIEnv* env, const std::string& value) {
    return env->NewStringUTF(value.c_str());
}

extern "C" JNIEXPORT jstring JNICALL
Java_com_mlomega_xr_securesurface_SecureTaskSurfaceProbe_nativeAttachMirror(
        JNIEnv* env,
        jclass,
        jobject surface,
        jobject mirror,
        jint source_width,
        jint source_height,
        jint target_width,
        jint target_height) {
    if (surface == nullptr || mirror == nullptr) {
        return Result(env, "surface_or_mirror_null");
    }

    auto from_java = reinterpret_cast<ASurfaceControlFromJava>(
            dlsym(RTLD_DEFAULT, "ASurfaceControl_fromJava"));
    if (from_java == nullptr) {
        return Result(env, "ASurfaceControl_fromJava_unavailable");
    }

    ANativeWindow* window = ANativeWindow_fromSurface(env, surface);
    if (window == nullptr) return Result(env, "native_window_unavailable");

    ASurfaceControl* parent = ASurfaceControl_createFromWindow(
            window,
            "MLOmega XREAL secure spatial parent");
    if (parent == nullptr) {
        ANativeWindow_release(window);
        return Result(env, "spatial_parent_rejected");
    }

    ASurfaceControl* child = from_java(env, mirror);
    if (child == nullptr) {
        ASurfaceControl_release(parent);
        ANativeWindow_release(window);
        return Result(env, "java_mirror_unavailable");
    }

    const float scale = std::min(
            static_cast<float>(target_width) / std::max(1, source_width),
            static_cast<float>(target_height) / std::max(1, source_height));
    const int rendered_width = static_cast<int>(source_width * scale);
    const int rendered_height = static_cast<int>(source_height * scale);
    const int left = (target_width - rendered_width) / 2;
    const int top = (target_height - rendered_height) / 2;
    ARect source_rect{0, 0, source_width, source_height};
    ARect destination_rect{
            left,
            top,
            left + rendered_width,
            top + rendered_height};

    ASurfaceTransaction* transaction = ASurfaceTransaction_create();
    ASurfaceTransaction_reparent(transaction, child, parent);
    ASurfaceTransaction_setGeometry(
            transaction,
            child,
            source_rect,
            destination_rect,
            ANATIVEWINDOW_TRANSFORM_IDENTITY);
    ASurfaceTransaction_setZOrder(transaction, child, 1);
    ASurfaceTransaction_setVisibility(
            transaction,
            child,
            ASURFACE_TRANSACTION_VISIBILITY_SHOW);
    ASurfaceTransaction_apply(transaction);
    ASurfaceTransaction_delete(transaction);

    ASurfaceControl_release(child);
    if (g_mirror_parent != nullptr) ASurfaceControl_release(g_mirror_parent);
    g_mirror_parent = parent;
    ANativeWindow_release(window);

    return Result(env,
            "spatial_mirror_attached:" + std::to_string(source_width) + "x" +
            std::to_string(source_height) + "->" +
            std::to_string(target_width) + "x" + std::to_string(target_height));
}

extern "C" JNIEXPORT void JNICALL
Java_com_mlomega_xr_securesurface_SecureTaskSurfaceProbe_nativeReleaseMirrorParent(
        JNIEnv*,
        jclass) {
    if (g_mirror_parent == nullptr) return;
    ASurfaceControl_release(g_mirror_parent);
    g_mirror_parent = nullptr;
}

}  // namespace

extern "C" JNIEXPORT jstring JNICALL
Java_com_mlomega_xr_securesurface_SecureTaskSurfaceProbe_nativeProbe(
        JNIEnv* env,
        jclass,
        jobject surface) {
    if (surface == nullptr) return Result(env, "surface_null");

    ANativeWindow* window = ANativeWindow_fromSurface(env, surface);
    if (window == nullptr) {
        __android_log_print(ANDROID_LOG_ERROR, kTag,
                            "ANativeWindow_fromSurface returned null");
        return Result(env, "native_window_unavailable");
    }

    const int width = ANativeWindow_getWidth(window);
    const int height = ANativeWindow_getHeight(window);
    ASurfaceControl* child = ASurfaceControl_createFromWindow(
            window,
            "MLOmega secure task capability probe");
    if (child == nullptr) {
        __android_log_print(ANDROID_LOG_ERROR, kTag,
                            "ASurfaceControl_createFromWindow rejected %dx%d surface",
                            width,
                            height);
        ANativeWindow_release(window);
        return Result(env, "surface_control_parent_rejected");
    }

    // Do not attach content in the capability probe. Releasing our reference
    // removes the unused child and leaves the validated v14 surface untouched.
    ASurfaceControl_release(child);
    ANativeWindow_release(window);

    const std::string result = "surface_control_parent_ready:" +
            std::to_string(width) + "x" + std::to_string(height);
    __android_log_print(ANDROID_LOG_INFO, kTag, "%s", result.c_str());
    return Result(env, result);
}
