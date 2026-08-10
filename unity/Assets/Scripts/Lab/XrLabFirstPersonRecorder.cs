using System;
using System.Collections;
using System.IO;
using System.Linq;
using MLOmega.XR.Core;
using UnityEngine;
using Unity.XR.XREAL;

namespace MLOmega.XR.UI
{
    /// <summary>
    /// Lab-only first-person capture using the XREAL 3.1 capture stack. It
    /// records the Eye RGB stream blended with Unity holograms and microphone
    /// audio, then publishes the completed MP4 to the Android gallery.
    /// Product and the validated Atelier never instantiate this component.
    /// </summary>
    public sealed class XrLabFirstPersonRecorder : MonoBehaviour
    {
        private enum RecorderState
        {
            Idle,
            Preparing,
            Recording,
            Stopping,
            Publishing,
            Error,
        }

        private XREALVideoCapture _capture;
        private XrSessionController _xrSession;
        private RecorderState _state = RecorderState.Idle;
        private float _recordStartedAt;
        private string _outputPath = string.Empty;
        private string _uiStatus = string.Empty;
        private bool _stopRequested;
        private bool _microphoneEnabled;

        public event Action StateChanged;

        public bool IsRecording => _state == RecorderState.Recording;
        public bool IsBusy =>
            _state == RecorderState.Preparing ||
            _state == RecorderState.Stopping ||
            _state == RecorderState.Publishing;
        public float ElapsedSeconds => IsRecording
            ? Mathf.Max(0f, Time.realtimeSinceStartup - _recordStartedAt)
            : 0f;
        public string UiStatus => _uiStatus;
        public string OutputPath => _outputPath;

        public void SetMicrophoneEnabled(bool enabled) =>
            _microphoneEnabled = enabled;

        private void Awake()
        {
            _xrSession = FindAnyObjectByType<XrSessionController>();
        }

        public void Toggle()
        {
            if (IsRecording)
            {
                RequestStop();
                return;
            }
            if (IsBusy) return;
            StartCapture();
        }

        public void RequestStop()
        {
            _stopRequested = true;
            if (!IsRecording || _capture == null) return;
            SetState(RecorderState.Stopping, "Sauvegarde…");
            try
            {
                _capture.StopRecordingAsync(OnRecordingStopped);
            }
            catch (Exception exception)
            {
                Fail("arrêt refusé", exception);
            }
        }

        private void StartCapture()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            _stopRequested = false;
            string directory = Path.Combine(
                Application.persistentDataPath,
                "Recordings");
            Directory.CreateDirectory(directory);
            _outputPath = Path.Combine(
                directory,
                "MLOmega_XREAL_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".mp4");
            SetState(RecorderState.Preparing, "Préparation…");
            try
            {
                XREALVideoCaptureUtility.CreateAsync(true, OnCaptureCreated);
            }
            catch (Exception exception)
            {
                Fail("capture indisponible", exception);
            }
#else
            SetState(RecorderState.Error, "Android requis");
#endif
        }

        private void OnCaptureCreated(XREALVideoCapture capture)
        {
            if (capture == null)
            {
                Fail("caméra indisponible");
                return;
            }
            _capture = capture;
            Resolution resolution = XREALVideoCaptureUtility.SupportedResolutions
                .OrderByDescending(value => value.width * value.height)
                .FirstOrDefault();
            if (resolution.width <= 0 || resolution.height <= 0)
            {
                Fail("résolution indisponible");
                return;
            }
            int frameRate = XREALVideoCaptureUtility
                .GetSupportedFrameRatesForResolution(resolution)
                .Where(value => value <= 30)
                .DefaultIfEmpty(30)
                .Max();
            var parameters = new CameraParameters
            {
                cameraType = Unity.XR.XREAL.CameraType.RGB,
                hologramOpacity = 1f,
                frameRate = frameRate,
                cameraResolutionWidth = resolution.width,
                cameraResolutionHeight = resolution.height,
                pixelFormat = CapturePixelFormat.PNG,
                blendMode = BlendMode.Blend,
                audioState = _microphoneEnabled
                    ? AudioState.MicAudio
                    : AudioState.None,
                captureSide = CaptureSide.Single,
                backgroundColor = Color.black,
            };
            try
            {
                _capture.StartVideoModeAsync(parameters, OnVideoModeStarted, true);
            }
            catch (Exception exception)
            {
                Fail("mode vidéo refusé", exception);
            }
        }

        private void OnVideoModeStarted(XREALVideoCapture.VideoCaptureResult result)
        {
            if (!result.success)
            {
                Fail("mode vidéo refusé");
                return;
            }
            try
            {
                _capture.StartRecordingAsync(_outputPath, OnRecordingStarted);
            }
            catch (Exception exception)
            {
                Fail("enregistrement refusé", exception);
            }
        }

        private void OnRecordingStarted(XREALVideoCapture.VideoCaptureResult result)
        {
            if (!result.success)
            {
                Fail("enregistrement refusé");
                return;
            }
            _recordStartedAt = Time.realtimeSinceStartup;
            SetState(RecorderState.Recording, string.Empty);
            Debug.Log("[XrLab][REC] recording path=" + _outputPath);
            if (_stopRequested) RequestStop();
        }

        private void OnRecordingStopped(XREALVideoCapture.VideoCaptureResult result)
        {
            if (!result.success)
            {
                Fail("arrêt refusé");
                return;
            }
            try
            {
                _capture.StopVideoModeAsync(OnVideoModeStopped);
            }
            catch (Exception exception)
            {
                Fail("finalisation refusée", exception);
            }
        }

        private void OnVideoModeStopped(XREALVideoCapture.VideoCaptureResult result)
        {
            ReleaseCapture();
            if (!result.success)
            {
                Fail("finalisation refusée");
                return;
            }
            SetState(RecorderState.Publishing, "Relance gestes…");
            StartCoroutine(RestoreEyeAndPublish());
        }

        private IEnumerator RestoreEyeAndPublish()
        {
            // XREALVideoCapture and the gesture adapter share the Eye RGB
            // device successfully while recording. StopVideoMode, however,
            // closes the native camera globally while XrealDeviceAdapter still
            // believes its singleton is active. Force a clean local teardown,
            // wait for the native lease, then reacquire it. GestureBridge stays
            // alive and consumes the first resumed EyeCaptureSource frame.
            if (_xrSession == null)
                _xrSession = FindAnyObjectByType<XrSessionController>();
            bool resumed = _xrSession == null;
            if (_xrSession != null)
            {
                _xrSession.SetEyeCapturePaused(true);
                yield return new WaitForSecondsRealtime(.35f);
                for (int attempt = 1; attempt <= 4; attempt++)
                {
                    resumed = _xrSession.SetEyeCapturePaused(false);
                    Debug.Log(
                        "[XrLab][REC] Eye restart attempt=" + attempt +
                        " resumed=" + resumed);
                    if (resumed) break;
                    yield return new WaitForSecondsRealtime(.45f);
                }
            }
            SetState(
                RecorderState.Publishing,
                resumed ? "Galerie…" : "REC OK · gestes KO");
            yield return PublishToGallery();
        }

        private IEnumerator PublishToGallery()
        {
            // The encoder closes asynchronously inside the SDK. One frame is
            // insufficient on some phones; a short bounded delay keeps the
            // gallery from indexing an incomplete file.
            yield return new WaitForSecondsRealtime(.65f);
#if UNITY_ANDROID && !UNITY_EDITOR
            bool published = false;
            try
            {
                string filename = Path.GetFileName(_outputPath);
                var gallery = new NativeGalleryDataProvider();
                gallery.InsertVideo(_outputPath, filename, "MLOmega");
                Debug.Log("[XrLab][REC] published=" + filename);
                published = true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[XrLab][REC] gallery publish failed; file kept at " +
                                 _outputPath + "\n" + exception);
            }
            if (published)
            {
                SetState(RecorderState.Idle, "Vidéo enregistrée");
                yield return new WaitForSecondsRealtime(2.4f);
                if (_state == RecorderState.Idle)
                    SetState(RecorderState.Idle, string.Empty);
                yield break;
            }
#endif
            SetState(RecorderState.Idle, "MP4 sauvegardé");
        }

        private void Fail(string message, Exception exception = null)
        {
            if (exception != null)
                Debug.LogWarning("[XrLab][REC] " + message + "\n" + exception);
            else
                Debug.LogWarning("[XrLab][REC] " + message);
            ReleaseCapture();
            SetState(RecorderState.Error, message);
        }

        private void ReleaseCapture()
        {
            if (_capture == null) return;
            try { _capture.Dispose(); }
            catch (Exception exception)
            {
                Debug.LogWarning("[XrLab][REC] dispose failed: " + exception.Message);
            }
            _capture = null;
        }

        private void SetState(RecorderState state, string status)
        {
            _state = state;
            _uiStatus = status ?? string.Empty;
            StateChanged?.Invoke();
        }

        private void OnDestroy()
        {
            ReleaseCapture();
        }
    }
}
