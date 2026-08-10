using System;
using System.IO;
using UnityEngine;

namespace MLOmega.XR.UI
{
    /// <summary>
    /// User-mediated Android document exchange. No broad storage permission and
    /// no hidden shared database are used between Atelier and production.
    /// </summary>
    public sealed class WorldMapDocumentExchange : MonoBehaviour
    {
        public event Action<string> Imported;
        public event Action<string> Exported;
        public event Action<string> ImageImported;
        public event Action<string> GlbImported;
        public event Action<string> Failed;

        public string LastStatus { get; private set; } = "idle";
        public string LastDetail { get; private set; } = string.Empty;

        public bool BeginExport(
            WorldMapStore store,
            string displayName = null)
        {
            if (store == null) return Fail("store_missing");
            string packagePath = Path.Combine(
                Application.temporaryCachePath,
                "world-map-v1.export.json");
            if (!store.ExportPackage(packagePath, out string error))
                return Fail(error);
            string safeName = SafeName(
                displayName,
                store.WorldMapId + ".world-map-v1.json");
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var bridge = new AndroidJavaClass(
                "com.mlomega.xr.documents.WorldMapDocumentActivity"))
            {
                bridge.CallStatic(
                    "beginExport",
                    packagePath,
                    safeName,
                    gameObject.name);
            }
#else
            string desktop = Path.Combine(
                Application.temporaryCachePath, safeName);
            File.Copy(packagePath, desktop, true);
            OnWorldMapDocumentResult("export|exported|" + desktop);
#endif
            LastStatus = "export_pending";
            return true;
        }

        public bool BeginImport()
        {
            string destination = Path.Combine(
                Application.temporaryCachePath,
                "world-map-v1.import.json");
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var bridge = new AndroidJavaClass(
                "com.mlomega.xr.documents.WorldMapDocumentActivity"))
            {
                bridge.CallStatic(
                    "beginImport",
                    destination,
                    gameObject.name);
            }
#else
            return Fail("document_picker_android_only");
#endif
            LastStatus = "import_pending";
            return true;
        }

        public bool BeginImageImport()
        {
            string destination = Path.Combine(
                Application.temporaryCachePath,
                "world-asset.import");
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var bridge = new AndroidJavaClass(
                "com.mlomega.xr.documents.WorldMapDocumentActivity"))
            {
                bridge.CallStatic(
                    "beginImageImport",
                    destination,
                    gameObject.name);
            }
#else
            return Fail("image_picker_android_only");
#endif
            LastStatus = "image_import_pending";
            return true;
        }

        public bool BeginGlbImport()
        {
            string destination = Path.Combine(
                Application.temporaryCachePath,
                "world-model.import.glb");
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var bridge = new AndroidJavaClass(
                "com.mlomega.xr.documents.WorldMapDocumentActivity"))
            {
                bridge.CallStatic(
                    "beginGlbImport",
                    destination,
                    gameObject.name);
            }
#else
            return Fail("glb_picker_android_only");
#endif
            LastStatus = "glb_import_pending";
            return true;
        }

        public void OnWorldMapDocumentResult(string result)
        {
            string[] parts = (result ?? string.Empty).Split(
                new[] { '|' }, 3);
            if (parts.Length < 2)
            {
                Fail("document_result_invalid");
                return;
            }
            string operation = parts[0];
            string status = parts[1];
            string detail = parts.Length == 3 ? parts[2] : string.Empty;
            LastStatus = status;
            LastDetail = detail;
            if (status == "imported" && operation == "import")
                Imported?.Invoke(detail);
            else if (status == "image_imported" && operation == "image")
                ImageImported?.Invoke(detail);
            else if (status == "glb_imported" && operation == "glb")
                GlbImported?.Invoke(detail);
            else if (status == "exported" && operation == "export")
                Exported?.Invoke(detail);
            else if (status != "cancelled")
                Failed?.Invoke(status + ":" + detail);
        }

        private bool Fail(string error)
        {
            LastStatus = "error";
            LastDetail = error ?? "unknown";
            Failed?.Invoke(LastDetail);
            return false;
        }

        private static string SafeName(string requested, string fallback)
        {
            string value = string.IsNullOrWhiteSpace(requested)
                ? fallback
                : requested;
            foreach (char invalid in Path.GetInvalidFileNameChars())
                value = value.Replace(invalid, '-');
            if (!value.EndsWith(
                    ".world-map-v1.json",
                    StringComparison.OrdinalIgnoreCase))
                value += ".world-map-v1.json";
            return value.Length <= 100 ? value : value.Substring(0, 100);
        }
    }
}
