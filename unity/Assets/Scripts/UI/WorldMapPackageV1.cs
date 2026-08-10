using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace MLOmega.XR.UI
{
    /// <summary>
    /// Explicit exchange envelope between the isolated Atelier APK and the
    /// production FreeGuy reader. It carries presentation only: never Memory,
    /// face data, transcripts or session identifiers.
    /// </summary>
    public static class WorldMapPackageV1
    {
        public const string PackageType = "mlomega.world-map";
        public const int MaxBytes = 768 * 1024 * 1024;
        public const int MaxContents = 2048;
        public const int MaxDynamicBindings = 512;

        [Serializable]
        private sealed class Envelope
        {
            public string packageType = PackageType;
            public int schemaVersion = WorldMapStore.CurrentSchemaVersion;
            public string generator = "MLOmega XREAL Atelier";
            public long exportedAtUnixMs;
            public string mapSha256;
            public WorldMapStore.MapDocument map;
        }

        public static bool TryWrite(
            WorldMapStore.MapDocument map,
            string path,
            out string error)
        {
            error = null;
            if (!ValidateMap(map, out error)) return false;
            try
            {
                string mapJson = JsonUtility.ToJson(map, false);
                var envelope = new Envelope
                {
                    exportedAtUnixMs =
                        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    mapSha256 = Sha256(mapJson),
                    map = map,
                };
                string json = JsonUtility.ToJson(envelope, true);
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                if (bytes.Length > MaxBytes)
                {
                    error = "world_map_package_too_large";
                    return false;
                }
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                File.WriteAllBytes(path, bytes);
                return true;
            }
            catch (Exception ex)
            {
                error = "world_map_export:" + ex.GetType().Name;
                return false;
            }
        }

        public static bool TryRead(
            string path,
            out WorldMapStore.MapDocument map,
            out string error)
        {
            map = null;
            error = null;
            try
            {
                var file = new FileInfo(path);
                if (!file.Exists || file.Length <= 0 || file.Length > MaxBytes)
                {
                    error = "world_map_package_missing_or_unbounded";
                    return false;
                }
                string json = File.ReadAllText(path, Encoding.UTF8);
                Envelope envelope = JsonUtility.FromJson<Envelope>(json);
                if (
                    envelope == null ||
                    envelope.packageType != PackageType ||
                    envelope.schemaVersion != WorldMapStore.CurrentSchemaVersion)
                {
                    error = "world_map_package_contract_invalid";
                    return false;
                }
                if (!ValidateMap(envelope.map, out error)) return false;
                string mapJson = JsonUtility.ToJson(envelope.map, false);
                if (!FixedEquals(envelope.mapSha256, Sha256(mapJson)))
                {
                    error = "world_map_package_digest_mismatch";
                    return false;
                }
                map = envelope.map;
                return true;
            }
            catch (Exception ex)
            {
                error = "world_map_import:" + ex.GetType().Name;
                return false;
            }
        }

        private static bool ValidateMap(
            WorldMapStore.MapDocument map,
            out string error)
        {
            error = null;
            if (map != null && map.dynamicBindings == null)
                map.dynamicBindings =
                    new List<WorldMapStore.WorldDynamicBinding>();
            if (
                map == null ||
                map.schemaVersion != WorldMapStore.CurrentSchemaVersion ||
                string.IsNullOrWhiteSpace(map.worldMapId) ||
                map.worldMapId.Length > 160 ||
                map.contents == null ||
                map.contents.Count > MaxContents ||
                    map.assets == null ||
                    map.assets.Count > WorldMapStore.MaxAssetCount ||
                    map.dynamicBindings == null ||
                    map.dynamicBindings.Count > MaxDynamicBindings ||
                    map.anchorMappings == null ||
                map.anchorMappings.Count > MaxContents)
            {
                error = "world_map_document_invalid";
                return false;
            }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var assetIds = new HashSet<string>(StringComparer.Ordinal);
            var anchorMappingIds =
                new HashSet<string>(StringComparer.Ordinal);
            int totalAssetBytes = 0;
            foreach (WorldMapStore.WorldAsset asset in map.assets)
            {
                byte[] bytes;
                try
                {
                    bytes = Convert.FromBase64String(asset?.base64Data ?? string.Empty);
                }
                catch (FormatException)
                {
                    error = "world_map_asset_base64_invalid";
                    return false;
                }
                if (
                    asset == null ||
                    string.IsNullOrWhiteSpace(asset.assetId) ||
                    asset.assetId.Length > 160 ||
                    !assetIds.Add(asset.assetId) ||
                    (asset.kind != "logo_image" &&
                     asset.kind != "glb_model") ||
                    (
                        asset.kind == "logo_image" &&
                        asset.mimeType != "image/png" &&
                        asset.mimeType != "image/jpeg"
                    ) ||
                    (
                        asset.kind == "glb_model" &&
                        asset.mimeType != "model/gltf-binary"
                    ) ||
                    bytes.Length <= 0 ||
                    bytes.Length > WorldMapStore.MaxAssetBytes ||
                    !string.IsNullOrWhiteSpace(asset.localFilePath) ||
                    !FixedEquals(asset.sha256, Sha256(bytes)))
                {
                    error = "world_map_asset_invalid";
                    return false;
                }
                if (
                    asset.kind == "glb_model" &&
                    !RuntimeGlbModel.TryValidate(bytes, out _))
                {
                    error = "world_map_glb_invalid";
                    return false;
                }
                totalAssetBytes += bytes.Length;
                if (totalAssetBytes > WorldMapStore.MaxTotalAssetBytes)
                {
                    error = "world_map_assets_too_large";
                    return false;
                }
            }
            int totalMappingBytes = 0;
            foreach (WorldMapStore.WorldAnchorMapping mapping in
                map.anchorMappings)
            {
                byte[] bytes;
                try
                {
                    bytes = Convert.FromBase64String(
                        mapping?.base64Data ?? string.Empty);
                }
                catch (FormatException)
                {
                    error = "world_map_anchor_mapping_base64_invalid";
                    return false;
                }
                if (
                    mapping == null ||
                    string.IsNullOrWhiteSpace(mapping.anchorGuid) ||
                    !anchorMappingIds.Add(mapping.anchorGuid) ||
                    string.IsNullOrWhiteSpace(mapping.nativeFileName) ||
                    mapping.nativeFileName.IndexOfAny(
                        Path.GetInvalidFileNameChars()) >= 0 ||
                    mapping.nativeFileName.Contains("..") ||
                    bytes.Length <= 0 ||
                    bytes.Length > WorldMapStore.MaxAnchorMappingBytes ||
                    !FixedEquals(mapping.sha256, Sha256(bytes)))
                {
                    error = "world_map_anchor_mapping_invalid";
                    return false;
                }
                totalMappingBytes += bytes.Length;
                if (totalMappingBytes >
                    WorldMapStore.MaxTotalAnchorMappingBytes)
                {
                    error = "world_map_anchor_mappings_too_large";
                    return false;
                }
            }
            foreach (WorldMapStore.WorldContent item in map.contents)
            {
                if (
                    item == null ||
                    string.IsNullOrWhiteSpace(item.worldContentId) ||
                    item.worldContentId.Length > 160 ||
                    !ids.Add(item.worldContentId) ||
                    string.IsNullOrWhiteSpace(item.anchorGuid) ||
                    item.anchorGuid.Length > 160 ||
                    !anchorMappingIds.Contains(item.anchorGuid) ||
                    string.IsNullOrWhiteSpace(item.templateId) ||
                    item.templateId.Length > 64 ||
                    (item.label ?? string.Empty).Length > 120 ||
                    (item.subtitle ?? string.Empty).Length > 240 ||
                    (!string.IsNullOrWhiteSpace(item.assetId) &&
                     !assetIds.Contains(item.assetId)) ||
                    !ValidMotion(item) ||
                    !Finite(item.localPosition) ||
                    !Finite(item.localEuler) ||
                    !ValidScale(item.localScale))
                {
                    error = "world_map_content_invalid";
                    return false;
                }
            }
            var bindingIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (WorldMapStore.WorldDynamicBinding binding in
                map.dynamicBindings)
            {
                if (
                    binding == null ||
                    string.IsNullOrWhiteSpace(binding.bindingId) ||
                    binding.bindingId.Length > 160 ||
                    !bindingIds.Add(binding.bindingId) ||
                    string.IsNullOrWhiteSpace(binding.templateId) ||
                    binding.templateId.Length > 64 ||
                    (binding.targetLabel ?? string.Empty).Length > 80 ||
                    (binding.targetKind ?? string.Empty).Length > 40 ||
                    (!string.IsNullOrWhiteSpace(binding.assetId) &&
                     !assetIds.Contains(binding.assetId)) ||
                    !Finite(binding.offset) ||
                    !ValidScale(binding.scale) ||
                    binding.minConfidence < 0.5f ||
                    binding.minConfidence > 1f ||
                    binding.maxInstances < 1 ||
                    binding.maxInstances > 12 ||
                    binding.ttlMs < 250 ||
                    binding.ttlMs > 10000)
                {
                    error = "world_map_dynamic_binding_invalid";
                    return false;
                }
            }
            return true;
        }

        private static bool Finite(WorldMapStore.StoredVector3 value) =>
            IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

        private static bool ValidScale(WorldMapStore.StoredVector3 value) =>
            Finite(value) &&
            value.x >= 0.1f && value.x <= WorldMapStore.MaxWorldScale &&
            value.y >= 0.1f && value.y <= WorldMapStore.MaxWorldScale &&
            value.z >= 0.1f && value.z <= WorldMapStore.MaxWorldScale;

        private static bool ValidMotion(WorldMapStore.WorldContent item)
        {
            bool legacy =
                string.IsNullOrWhiteSpace(item.motionPath) &&
                item.motionRadiusM == 0f &&
                item.motionSpeed == 0f &&
                item.motionHeightM == 0f;
            if (legacy) return true;
            string requested = string.IsNullOrWhiteSpace(item.motionPath)
                ? "static"
                : item.motionPath.Trim().ToLowerInvariant();
            return
                WorldMapStore.CleanMotionPath(requested) == requested &&
                IsFinite(item.motionRadiusM) &&
                item.motionRadiusM >= .1f &&
                item.motionRadiusM <= 40f &&
                IsFinite(item.motionSpeed) &&
                item.motionSpeed >= .05f &&
                item.motionSpeed <= 5f &&
                IsFinite(item.motionHeightM) &&
                item.motionHeightM >= -20f &&
                item.motionHeightM <= 20f;
        }

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);

        private static string Sha256(string value)
            => Sha256(Encoding.UTF8.GetBytes(value));

        private static string Sha256(byte[] value)
        {
            using (SHA256 hash = SHA256.Create())
            {
                byte[] digest = hash.ComputeHash(value);
                var result = new StringBuilder(digest.Length * 2);
                foreach (byte item in digest) result.Append(item.ToString("x2"));
                return result.ToString();
            }
        }

        private static bool FixedEquals(string left, string right)
        {
            byte[] a = Encoding.ASCII.GetBytes(left ?? string.Empty);
            byte[] b = Encoding.ASCII.GetBytes(right ?? string.Empty);
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }
}
