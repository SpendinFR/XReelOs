using System;
using System.Collections.Generic;
using TLab.WebView;
using UnityEngine;
using UnityEngine.Rendering;

namespace MLOmega.XR.UI
{
    /// <summary>
    /// Imports Media3's authenticated decoded frames through TLab's proven
    /// HardwareBuffer/shared-texture bridge. The resulting Texture remains in
    /// Unity's OpenGLES/XREAL render graph and can therefore be sampled once per
    /// eye by XrLabWebVrPresenter.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class XrLabWebVrStreamTexture : MonoBehaviour
    {
        private AndroidJavaObject _capture;
        private Texture2D _texture;
        private int _nativeInstance;
        private bool _initialized;
        private bool _stopping;
        private bool _playbackBridgeAvailable = true;
        private bool _playbackBridgeWarningLogged;
        private float _nextMetadataPoll;
        private readonly List<AndroidJavaObject> _retiredCaptures =
            new List<AndroidJavaObject>();

        public Texture Texture => _texture;
        // Tiny square autoplay previews, animated logos and advertising loops
        // are observable through the authenticated WebView too. They are not VR
        // sources and must never open the immersive dome.
        public bool RejectedPreview =>
            VideoWidth > 0 && VideoHeight > 0 &&
            (VideoWidth < 960 || VideoHeight < 540);
        public bool Ready =>
            _texture != null && VideoWidth >= 960 && VideoHeight >= 540;
        public int VideoWidth { get; private set; }
        public int VideoHeight { get; private set; }
        public int ProjectionAngle { get; private set; } = 180;
        public string StereoMode { get; private set; } = "sbs";
        public long PlaybackPositionMs { get; private set; }
        public long PlaybackDurationMs { get; private set; }
        public bool IsPlaying { get; private set; } = true;
        public float PlaybackNormalized => PlaybackDurationMs > 0
            ? Mathf.Clamp01(PlaybackPositionMs / (float)PlaybackDurationMs)
            : 0f;
        public bool LikelyInterstitial =>
            PlaybackDurationMs > 0L &&
            PlaybackDurationMs <= 120000L &&
            VideoWidth <= 1920 && VideoHeight <= 1080;

        public bool StartCapture(
            string descriptor,
            int textureWidth,
            int textureHeight,
            int fps,
            string stereoMode)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            StopCapture();
            if (string.IsNullOrWhiteSpace(descriptor)) return false;
            try
            {
                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using AndroidJavaObject activity =
                    unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                using var bridge = new AndroidJavaClass(
                    "com.mlomega.xr.webvr.XrWebVrBridge");
                _capture = bridge.CallStatic<AndroidJavaObject>(
                    "startUnityTexture",
                    activity,
                    descriptor,
                    textureWidth,
                    textureHeight,
                    fps,
                    stereoMode ?? "auto");
                if (_capture == null) return false;
                _nativeInstance = unchecked((int)_capture.GetRawObject().ToInt64());
                _stopping = false;
                _playbackBridgeAvailable = true;
                _playbackBridgeWarningLogged = false;
                StereoMode = string.IsNullOrWhiteSpace(stereoMode)
                    ? "auto"
                    : stereoMode;
                Debug.Log(
                    $"[XrLabVR] authenticated decoder texture requested " +
                    $"{textureWidth}x{textureHeight}@{fps}.");
                return _nativeInstance != 0;
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "[XrLabVR] decoder texture start failed: " +
                    exception.GetType().Name + " " + exception.Message);
                StopCapture();
            }
#endif
            return false;
        }

        private void Update()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            CollectRetiredCaptures();
            if (_stopping || _capture == null || _nativeInstance == 0) return;
            try
            {
                if (!_initialized)
                {
                    _initialized = NativePlugin.GetIsFragmentInitialized(
                        _nativeInstance);
                    if (!_initialized) return;
                    Debug.Log("[XrLabVR] decoder HardwareBuffer initialized.");
                }

                if (SystemInfo.renderingThreadingMode == RenderingThreadingMode.MultiThreaded)
                    GL.IssuePluginEvent(
                        NativePlugin.UpdateSharedTextureFunc(), _nativeInstance);
                else
                    NativePlugin.UpdateSharedTexture(_nativeInstance);

                if (_texture == null &&
                    NativePlugin.ContentExists(_nativeInstance) &&
                    !NativePlugin.GetSharedBufferUpdateFlag(_nativeInstance))
                {
                    IntPtr textureId =
                        NativePlugin.GetPlatformTextureID(_nativeInstance);
                    if (textureId != IntPtr.Zero)
                    {
                        _texture = Texture2D.CreateExternalTexture(
                            1,
                            1,
                            TextureFormat.ARGB32,
                            false,
                            false,
                            textureId);
                        _texture.name = "XReel Authenticated VR Stream";
                        NativePlugin.SetSharedBufferUpdateFlag(
                            _nativeInstance, true);
                        Debug.Log(
                            "[XrLabVR] decoded stream imported as Unity GPU texture.");
                    }
                }

                if (Time.unscaledTime >= _nextMetadataPoll)
                {
                    _nextMetadataPoll = Time.unscaledTime + .2f;
                    VideoWidth = _capture.Call<int>("getVideoWidth");
                    VideoHeight = _capture.Call<int>("getVideoHeight");
                    ProjectionAngle = _capture.Call<int>("activeAngle");
                    string mode = _capture.Call<string>("activeStereo");
                    if (!string.IsNullOrWhiteSpace(mode)) StereoMode = mode;
                    if (_playbackBridgeAvailable) PollPlaybackMetadata();
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[XrLabVR] decoder texture update failed: " +
                    exception.GetType().Name);
            }
#endif
        }

        public void SeekNormalized(float normalized)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_capture == null || _stopping) return;
            try
            {
                using var bridge = new AndroidJavaClass(
                    "com.mlomega.xr.webvr.XrWebVrBridge");
                bool accepted = bridge.CallStatic<bool>(
                    "seekUnityPlaybackToFraction",
                    Mathf.Clamp01(normalized));
                if (!accepted)
                    Debug.LogWarning("[XrLabVR] seek refused: player unavailable.");
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[XrLabVR] seek failed: " + exception.GetType().Name);
            }
#endif
        }

        public void TogglePlayback()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_capture == null || _stopping) return;
            try
            {
                using var bridge = new AndroidJavaClass(
                    "com.mlomega.xr.webvr.XrWebVrBridge");
                bridge.CallStatic<bool>("toggleUnityPlayback");
                // The Java player updates the authoritative cached state on its
                // UI thread. Flip immediately as tactile visual feedback.
                IsPlaying = !IsPlaying;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[XrLabVR] play/pause failed: " + exception.GetType().Name);
            }
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private void PollPlaybackMetadata()
        {
            try
            {
                using var bridge = new AndroidJavaClass(
                    "com.mlomega.xr.webvr.XrWebVrBridge");
                PlaybackPositionMs = bridge.CallStatic<long>(
                    "getUnityPlaybackPositionMs");
                PlaybackDurationMs = bridge.CallStatic<long>(
                    "getUnityPlaybackDurationMs");
                IsPlaying = bridge.CallStatic<bool>("isUnityPlaybackPlaying");
            }
            catch (Exception exception)
            {
                _playbackBridgeAvailable = false;
                if (_playbackBridgeWarningLogged) return;
                _playbackBridgeWarningLogged = true;
                Debug.LogWarning(
                    "[XrLabVR] playback metadata unavailable: " +
                    exception.GetType().Name + " " + exception.Message);
            }
        }
#endif

        public void StopCapture()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_stopping) return;
            _stopping = true;
            try
            {
                using var bridge = new AndroidJavaClass(
                    "com.mlomega.xr.webvr.XrWebVrBridge");
                bridge.CallStatic("stopUnityPlayback");
            }
            catch (Exception) { }

            AndroidJavaObject retiring = _capture;
            if (_nativeInstance != 0 && retiring != null)
            {
                if (SystemInfo.renderingThreadingMode == RenderingThreadingMode.MultiThreaded)
                    GL.IssuePluginEvent(NativePlugin.DisposeFunc(), _nativeInstance);
                else
                    NativePlugin.Dispose(_nativeInstance);

                // TLab's render event still contains the raw JNI handle. Keep
                // the AndroidJavaObject (and therefore its global reference)
                // alive until Java reports that render-thread disposal and the
                // UI-thread cleanup have both completed.
                _retiredCaptures.Add(retiring);
            }
#endif
            if (_texture != null) Destroy(_texture);
            _texture = null;
            _capture = null;
            _nativeInstance = 0;
            _initialized = false;
            VideoWidth = 0;
            VideoHeight = 0;
            ProjectionAngle = 180;
            PlaybackPositionMs = 0L;
            PlaybackDurationMs = 0L;
            IsPlaying = true;
            _playbackBridgeAvailable = true;
            _playbackBridgeWarningLogged = false;
            _stopping = false;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private void CollectRetiredCaptures()
        {
            for (int index = _retiredCaptures.Count - 1; index >= 0; index--)
            {
                AndroidJavaObject capture = _retiredCaptures[index];
                if (capture == null)
                {
                    _retiredCaptures.RemoveAt(index);
                    continue;
                }

                try
                {
                    int instance = unchecked(
                        (int)capture.GetRawObject().ToInt64());
                    if (instance == 0 ||
                        !NativePlugin.GetIsFragmentDisposed(instance))
                        continue;

                    capture.Dispose();
                    _retiredCaptures.RemoveAt(index);
                    Debug.Log("[XrLabVR] decoder shared texture disposed safely.");
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        "[XrLabVR] deferred decoder disposal check failed: " +
                        exception.GetType().Name);
                }
            }
        }
#endif

        private void OnDestroy() => StopCapture();
        private void OnApplicationQuit() => StopCapture();
    }
}
