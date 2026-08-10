using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace MLOmega.XR.Core
{
    /// <summary>
    /// Additive, on-demand probe for the future augmented-reality service.
    ///
    /// It deliberately imports neither AR Foundation nor ARCore. The product APK
    /// keeps its current XR provider untouched; reflection only reports what is
    /// already loaded when the user explicitly enables augmented reality.
    /// </summary>
    public sealed class AugmentedRealityCapabilityProbe : MonoBehaviour
    {
        [Serializable]
        public sealed class Report
        {
            public string DeviceModel { get; set; }
            public string OperatingSystem { get; set; }
            public string ActiveXrLoader { get; set; }
            public bool XrealSdkCompiled { get; set; }
            public bool ArFoundationLoaded { get; set; }
            public bool ArcorePluginLoaded { get; set; }
            public bool ArcoreExtensionsLoaded { get; set; }
            public bool SemanticSoundModelAvailable { get; set; }
            public string[] ConfiguredLoaderCandidates { get; set; }
            public string[] RunningArSubsystems { get; set; }
            public int SimultaneousActiveLoaderCount { get; set; }
            public string ProviderBoundary { get; set; }
            public string CoexistenceVerdict { get; set; }
        }

        public Report LastReport { get; private set; }

        public Report Probe()
        {
            bool xrealCompiled = false;
#if XREAL_SDK_PRESENT
            xrealCompiled = true;
#endif
            string activeLoader = ResolveActiveLoader();
            string[] configuredLoaders = ResolveConfiguredLoaders();
            LastReport = new Report
            {
                DeviceModel = SystemInfo.deviceModel ?? string.Empty,
                OperatingSystem = SystemInfo.operatingSystem ?? string.Empty,
                ActiveXrLoader = activeLoader,
                XrealSdkCompiled = xrealCompiled,
                ArFoundationLoaded = HasAssembly("Unity.XR.ARFoundation"),
                ArcorePluginLoaded = HasAssembly("Unity.XR.ARCore"),
                ArcoreExtensionsLoaded = HasAssembly("Google.XR.ARCoreExtensions"),
                SemanticSoundModelAvailable = HasDeviceModel("yamnet.tflite"),
                ConfiguredLoaderCandidates = configuredLoaders,
                RunningArSubsystems = ResolveRunningArSubsystems(),
                // XR Management exposes one activeLoader. Multiple configured
                // candidates are fallback/order metadata, not simultaneous
                // providers. Keep this number factual instead of inferring
                // coexistence from package presence.
                SimultaneousActiveLoaderCount =
                    string.Equals(activeLoader, "none", StringComparison.Ordinal) ? 0 : 1,
                ProviderBoundary = ResolveProviderBoundary(activeLoader),
                // A package, descriptor or configured loader is not evidence that
                // XREAL and Google ARCore camera sessions coexist on the S24.
                CoexistenceVerdict = "single_active_loader_architecture",
            };
            return LastReport;
        }

        private static bool HasAssembly(string fragment)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string name = assembly.GetName().Name ?? string.Empty;
                if (name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private static string ResolveActiveLoader()
        {
            try
            {
                Type settingsType = FindType(
                    "UnityEngine.XR.Management.XRGeneralSettings",
                    "Unity.XR.Management");
                object settings = settingsType?
                    .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?
                    .GetValue(null);
                object manager = settingsType?
                    .GetProperty("Manager", BindingFlags.Public | BindingFlags.Instance)?
                    .GetValue(settings);
                object loader = manager?.GetType()
                    .GetProperty("activeLoader", BindingFlags.Public | BindingFlags.Instance)?
                    .GetValue(manager);
                return loader == null ? "none" : loader.GetType().FullName;
            }
            catch (Exception ex)
            {
                return "probe_error:" + ex.GetType().Name;
            }
        }

        private static string[] ResolveConfiguredLoaders()
        {
            var names = new List<string>();
            try
            {
                object manager = ResolveManager();
                object candidates = manager?.GetType()
                    .GetProperty("activeLoaders", BindingFlags.Public | BindingFlags.Instance)?
                    .GetValue(manager);
                if (candidates is IEnumerable enumerable)
                {
                    foreach (object candidate in enumerable)
                    {
                        string name = candidate?.GetType().FullName;
                        if (!string.IsNullOrEmpty(name) && !names.Contains(name))
                            names.Add(name);
                    }
                }
            }
            catch
            {
                // The probe is diagnostic and must not make the product fail.
            }
            return names.ToArray();
        }

        private static string[] ResolveRunningArSubsystems()
        {
            var running = new List<string>();
            string[] subsystemTypes =
            {
                "UnityEngine.XR.ARSubsystems.XRSessionSubsystem",
                "UnityEngine.XR.ARSubsystems.XRCameraSubsystem",
                "UnityEngine.XR.ARSubsystems.XRPlaneSubsystem",
                "UnityEngine.XR.ARSubsystems.XRAnchorSubsystem",
                "UnityEngine.XR.XRMeshSubsystem",
                "UnityEngine.XR.ARSubsystems.XROcclusionSubsystem",
            };
            foreach (string fullName in subsystemTypes)
            {
                Type subsystemType = FindType(fullName, string.Empty);
                if (subsystemType == null) continue;
                AppendRunningSubsystems(subsystemType, running);
            }
            return running.ToArray();
        }

        private static void AppendRunningSubsystems(
            Type subsystemType,
            List<string> destination)
        {
            try
            {
                Type listType = typeof(List<>).MakeGenericType(subsystemType);
                object list = Activator.CreateInstance(listType);
                MethodInfo getInstances = null;
                foreach (MethodInfo method in typeof(SubsystemManager)
                    .GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (method.Name == "GetInstances" &&
                        method.IsGenericMethodDefinition &&
                        method.GetParameters().Length == 1)
                    {
                        getInstances = method;
                        break;
                    }
                }
                getInstances?
                    .MakeGenericMethod(subsystemType)
                    .Invoke(null, new[] { list });
                if (!(list is IEnumerable enumerable)) return;
                foreach (object subsystem in enumerable)
                {
                    object runningValue = subsystem?.GetType()
                        .GetProperty("running", BindingFlags.Public | BindingFlags.Instance)?
                        .GetValue(subsystem);
                    bool isRunning =
                        runningValue is bool runningFlag && runningFlag;
                    if (!isRunning) continue;
                    string id = ResolveSubsystemId(subsystem);
                    string value = string.IsNullOrEmpty(id)
                        ? subsystem.GetType().FullName
                        : $"{subsystem.GetType().FullName}:{id}";
                    if (!destination.Contains(value)) destination.Add(value);
                }
            }
            catch
            {
                // Some subsystem types differ between Unity package versions.
                // Absence is reported as absence, never promoted to support.
            }
        }

        private static string ResolveSubsystemId(object subsystem)
        {
            try
            {
                object descriptor = subsystem?.GetType()
                    .GetProperty("subsystemDescriptor",
                        BindingFlags.Public | BindingFlags.Instance)?
                    .GetValue(subsystem);
                return descriptor?.GetType()
                    .GetProperty("id", BindingFlags.Public | BindingFlags.Instance)?
                    .GetValue(descriptor) as string;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static object ResolveManager()
        {
            Type settingsType = FindType(
                "UnityEngine.XR.Management.XRGeneralSettings",
                "Unity.XR.Management");
            object settings = settingsType?
                .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?
                .GetValue(null);
            return settingsType?
                .GetProperty("Manager", BindingFlags.Public | BindingFlags.Instance)?
                .GetValue(settings);
        }

        private static string ResolveProviderBoundary(string activeLoader)
        {
            if (string.IsNullOrEmpty(activeLoader) ||
                string.Equals(activeLoader, "none", StringComparison.Ordinal))
                return "no_active_provider";
            if (activeLoader.IndexOf("XREAL", StringComparison.OrdinalIgnoreCase) >= 0)
                return "xreal_provider";
            if (activeLoader.IndexOf("ARCore", StringComparison.OrdinalIgnoreCase) >= 0)
                return "google_arcore_provider";
            return "other_provider";
        }

        private static bool HasDeviceModel(string name)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                string path = System.IO.Path.Combine(
                    Application.persistentDataPath, "models", name);
                return System.IO.File.Exists(path) &&
                    new System.IO.FileInfo(path).Length > 0;
            }
            catch
            {
                return false;
            }
#else
            return false;
#endif
        }

        private static Type FindType(string fullName, string assemblyFragment)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string name = assembly.GetName().Name ?? string.Empty;
                if (!string.IsNullOrEmpty(assemblyFragment) &&
                    name.IndexOf(assemblyFragment, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                Type found = assembly.GetType(fullName, false);
                if (found != null) return found;
            }
            return null;
        }
    }
}
