using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;

namespace MLOmega.XR.Core
{
    /// <summary>
    /// Explicit hardware gate for one XR provider at a time.
    ///
    /// This component is never added to PhoneOnly or XrealProduct. The editor gate
    /// builder clones the product scene and adds it only to the disposable gate
    /// player. AR Foundation managers are created through reflection so the
    /// production project keeps no hard dependency on AR Foundation or ARCore.
    /// </summary>
    public sealed class AugmentedRealityRuntimeGate : MonoBehaviour
    {
        [Serializable]
        public sealed class GateReport
        {
            public string schema_version = "mlomega.ar.provider_gate.v1";
            public string run_id;
            public string started_utc;
            public string completed_utc;
            public string expected_provider;
            public string verdict;
            public string report_path;
            public float measured_seconds;
            public int sample_count;
            public float average_render_fps;
            public float minimum_render_fps;
            public long eye_frames_start;
            public long eye_frames_end;
            public float eye_fps;
            public float pose_tracking_ratio;
            public float transport_connected_ratio;
            public float ar_session_running_ratio;
            public int maximum_thermal_status;
            public float peak_allocated_memory_mb;
            public AugmentedRealityCapabilityProbe.Report provider;
            public List<string> failures = new List<string>();
        }

        [SerializeField] private bool _autoStart;
        [SerializeField] private string _expectedProvider = "xreal_provider";
        [SerializeField] private float _warmupSeconds = 5f;
        [SerializeField] private float _measurementSeconds = 60f;
        [SerializeField] private float _sampleIntervalSeconds = 1f;
        [SerializeField] private float _minimumRenderFps = 24f;
        [SerializeField] private float _minimumPoseRatio = 0.9f;
        [SerializeField] private float _minimumTransportRatio = 0.9f;
        [SerializeField] private bool _requireEyeFrames = true;
        [SerializeField] private bool _requireTransport = true;
        [SerializeField] private bool _requireArFoundation = true;
        [SerializeField] private bool _startArFoundationManagers = true;

        private AugmentedRealityCapabilityProbe _probe;
        private Coroutine _running;
        private int _renderFrames;
        private int _totalRenderFrames;
        private float _renderElapsed;
        private float _minimumObservedFps = float.MaxValue;

        public GateReport LastReport { get; private set; }
        public bool IsRunning => _running != null;
        public string CurrentStatus { get; private set; } = "idle";

        private void Awake()
        {
            _probe = GetComponent<AugmentedRealityCapabilityProbe>();
            if (_probe == null)
                _probe = gameObject.AddComponent<AugmentedRealityCapabilityProbe>();
        }

        private void Start()
        {
            if (_autoStart) Begin();
        }

        private void Update()
        {
            if (_running == null) return;
            _renderFrames++;
            _totalRenderFrames++;
            _renderElapsed += Time.unscaledDeltaTime;
            if (_renderElapsed < 0.5f) return;
            float fps = _renderFrames / Mathf.Max(0.001f, _renderElapsed);
            _minimumObservedFps = Mathf.Min(_minimumObservedFps, fps);
            _renderFrames = 0;
            _totalRenderFrames = 0;
            _renderElapsed = 0f;
        }

        public bool Begin()
        {
            if (_running != null) return false;
            _running = StartCoroutine(Run());
            return true;
        }

        public void Cancel()
        {
            if (_running == null) return;
            StopCoroutine(_running);
            _running = null;
            CurrentStatus = "cancelled";
        }

        private IEnumerator Run()
        {
            string runId = $"ar-gate-{DateTime.UtcNow:yyyyMMddTHHmmssfff}";
            LastReport = new GateReport
            {
                run_id = runId,
                started_utc = DateTime.UtcNow.ToString("O"),
                expected_provider = _expectedProvider,
                verdict = "running",
            };
            CurrentStatus = "warmup";
            if (_startArFoundationManagers) EnsureArFoundationManagers();
            if (_warmupSeconds > 0f)
                yield return new WaitForSecondsRealtime(_warmupSeconds);

            EyeCaptureSource capture = FindAnyObjectByType<EyeCaptureSource>();
            long eyeStart = capture != null ? capture.PublishedFrameCount : 0L;
            int samples = 0;
            int poseTracking = 0;
            int transportConnected = 0;
            int arSessionRunning = 0;
            int maximumThermal = -1;
            long peakAllocated = 0L;
            float started = Time.realtimeSinceStartup;
            float deadline = started + Mathf.Max(1f, _measurementSeconds);
            float interval = Mathf.Max(0.1f, _sampleIntervalSeconds);
            _renderFrames = 0;
            _renderElapsed = 0f;
            _minimumObservedFps = float.MaxValue;
            CurrentStatus = "measuring";

            while (Time.realtimeSinceStartup < deadline)
            {
                samples++;
                PosePublisher pose = FindAnyObjectByType<PosePublisher>();
                if (pose != null && pose.Latest.IsTracking) poseTracking++;
                if (ResolveTransportConnected()) transportConnected++;

                AugmentedRealityCapabilityProbe.Report provider = _probe.Probe();
                if (HasRunningSubsystem(provider, "Session")) arSessionRunning++;

                maximumThermal = Mathf.Max(maximumThermal, ResolveThermalStatus());
                peakAllocated = Math.Max(
                    peakAllocated,
                    UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong());
                yield return new WaitForSecondsRealtime(interval);
            }

            float measured = Mathf.Max(0.001f, Time.realtimeSinceStartup - started);
            long eyeEnd = capture != null ? capture.PublishedFrameCount : eyeStart;
            float averageFps = _totalRenderFrames / measured;
            LastReport.completed_utc = DateTime.UtcNow.ToString("O");
            LastReport.measured_seconds = measured;
            LastReport.sample_count = samples;
            LastReport.average_render_fps = averageFps;
            LastReport.minimum_render_fps =
                _minimumObservedFps == float.MaxValue ? 0f : _minimumObservedFps;
            LastReport.eye_frames_start = eyeStart;
            LastReport.eye_frames_end = eyeEnd;
            LastReport.eye_fps = (eyeEnd - eyeStart) / measured;
            LastReport.pose_tracking_ratio = Ratio(poseTracking, samples);
            LastReport.transport_connected_ratio = Ratio(transportConnected, samples);
            LastReport.ar_session_running_ratio = Ratio(arSessionRunning, samples);
            LastReport.maximum_thermal_status = maximumThermal;
            LastReport.peak_allocated_memory_mb =
                peakAllocated / (1024f * 1024f);
            LastReport.provider = _probe.Probe();
            Evaluate(LastReport);
            Persist(LastReport);
            CurrentStatus = LastReport.verdict;
            _running = null;
        }

        private void Evaluate(GateReport report)
        {
            if (report.provider == null)
            {
                report.failures.Add("provider_probe_missing");
            }
            else
            {
                if (!string.Equals(
                        report.provider.ProviderBoundary,
                        _expectedProvider,
                        StringComparison.Ordinal))
                {
                    report.failures.Add(
                        $"provider_mismatch:{report.provider.ProviderBoundary}");
                }
                if (report.provider.SimultaneousActiveLoaderCount > 1)
                    report.failures.Add("multiple_simultaneous_xr_loaders");
                if (_requireArFoundation && !report.provider.ArFoundationLoaded)
                    report.failures.Add("ar_foundation_missing");
                if (_requireArFoundation &&
                    report.ar_session_running_ratio < 0.9f)
                    report.failures.Add("ar_session_not_running");
            }

            if (_requireEyeFrames && report.eye_frames_end <= report.eye_frames_start)
                report.failures.Add("eye_frames_missing");
            if (report.pose_tracking_ratio < _minimumPoseRatio)
                report.failures.Add("pose_tracking_ratio_low");
            if (_requireTransport &&
                report.transport_connected_ratio < _minimumTransportRatio)
                report.failures.Add("transport_connected_ratio_low");
            if (report.minimum_render_fps < _minimumRenderFps)
                report.failures.Add("render_fps_low");
            // Android thermal status: 4=critical, 5=emergency, 6=shutdown.
            if (report.maximum_thermal_status >= 4)
                report.failures.Add("thermal_status_critical");

            report.verdict = report.failures.Count == 0 ? "pass" : "fail";
        }

        private void Persist(GateReport report)
        {
            try
            {
                string directory = Path.Combine(
                    Application.persistentDataPath,
                    "mlomega-ar-gates");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, report.run_id + ".json");
                report.report_path = path;
                File.WriteAllText(
                    path,
                    JsonConvert.SerializeObject(report, Formatting.Indented));
                Debug.Log($"[AugmentedRealityGate] {report.verdict}: {path}");
            }
            catch (Exception ex)
            {
                report.failures.Add("report_persist_failed:" + ex.GetType().Name);
                report.verdict = "fail";
                Debug.LogError($"[AugmentedRealityGate] report write failed: {ex}");
            }
        }

        private static float Ratio(int count, int total) =>
            total <= 0 ? 0f : (float)count / total;

        private static bool HasRunningSubsystem(
            AugmentedRealityCapabilityProbe.Report report,
            string fragment)
        {
            if (report?.RunningArSubsystems == null) return false;
            foreach (string subsystem in report.RunningArSubsystems)
                if (!string.IsNullOrEmpty(subsystem) &&
                    subsystem.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        private static bool ResolveTransportConnected()
        {
            foreach (MonoBehaviour behaviour in
                FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (behaviour == null ||
                    !string.Equals(
                        behaviour.GetType().FullName,
                        "MLOmega.XR.Transport.LiveTransportBridge",
                        StringComparison.Ordinal))
                    continue;
                object value = behaviour.GetType()
                    .GetProperty("State", BindingFlags.Public | BindingFlags.Instance)?
                    .GetValue(behaviour);
                return string.Equals(
                    value?.ToString(),
                    "Connected",
                    StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        private static int ResolveThermalStatus()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using AndroidJavaObject activity =
                    player.GetStatic<AndroidJavaObject>("currentActivity");
                using AndroidJavaObject power =
                    activity.Call<AndroidJavaObject>("getSystemService", "power");
                return power.Call<int>("getCurrentThermalStatus");
            }
            catch
            {
                return -1;
            }
#else
            return -1;
#endif
        }

        private static void EnsureArFoundationManagers()
        {
            Type sessionType = FindType(
                "UnityEngine.XR.ARFoundation.ARSession");
            if (sessionType != null &&
                !HasBehaviourOfType(sessionType))
            {
                var sessionObject = new GameObject("AR Session (provider gate)");
                sessionObject.AddComponent(sessionType);
            }

            Type cameraManagerType = FindType(
                "UnityEngine.XR.ARFoundation.ARCameraManager");
            Camera camera = Camera.main;
            if (cameraManagerType != null && camera != null &&
                camera.GetComponent(cameraManagerType) == null)
            {
                camera.gameObject.AddComponent(cameraManagerType);
            }
        }

        private static Type FindType(string fullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type found = assembly.GetType(fullName, false);
                if (found != null) return found;
            }
            return null;
        }

        private static bool HasBehaviourOfType(Type type)
        {
            foreach (MonoBehaviour behaviour in
                FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (behaviour != null && type.IsInstanceOfType(behaviour))
                    return true;
            }
            return false;
        }
    }
}
