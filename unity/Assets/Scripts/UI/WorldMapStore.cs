using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;

namespace MLOmega.XR.UI
{
    /// <summary>
    /// Durable, device-local catalogue for externally-authored FreeGuy content.
    ///
    /// The production APK is a load-only consumer; the isolated Atelier APK exports
    /// the same versioned document. This store contains only presentation
    /// and spatial provenance. It never opens memory.db and its identifiers are
    /// deliberately independent from WebRTC, BrainLive and CloseDay identifiers.
    /// </summary>
    public sealed class WorldMapStore
    {
        public const int CurrentSchemaVersion = 1;
        public const int MaxAssetCount = 512;
        public const int MaxAssetBytes = 32 * 1024 * 1024;
        public const int MaxAssetDimension = 2048;
        public const int MaxTotalAssetBytes = 512 * 1024 * 1024;
        public const float MaxWorldScale = 50f;
        public const int MaxAnchorMappingBytes = 2 * 1024 * 1024;
        public const int MaxTotalAnchorMappingBytes = 24 * 1024 * 1024;
        private const double EarthRadiusM = 6378137.0;

        [Serializable]
        public sealed class MapDocument
        {
            public int schemaVersion = CurrentSchemaVersion;
            public string worldMapId;
            public string displayName;
            public string calibrationId;
            public long createdAtUnixMs;
            public long updatedAtUnixMs;
            public bool geoOriginValid;
            public double originLatitude;
            public double originLongitude;
            public double originAltitudeM;
            public float originAccuracyM;
            public float worldNorthYawDeg;
            public StoredVector3 localOrigin;
            public List<WorldContent> contents = new List<WorldContent>();
            public List<WorldAsset> assets = new List<WorldAsset>();
            public List<WorldDynamicBinding> dynamicBindings =
                new List<WorldDynamicBinding>();
            public List<WorldAnchorMapping> anchorMappings =
                new List<WorldAnchorMapping>();
        }

        [Serializable]
        public sealed class WorldContent
        {
            public string worldContentId;
            public string sourceMapId;
            public string anchorGuid;
            public string templateId;
            public string presetId;
            public string categoryId;
            public string archetypeId;
            public string styleId;
            public string animationId;
            public string motionPath = "static";
            public float motionRadiusM = 1.5f;
            public float motionSpeed = 0.8f;
            public float motionHeightM;
            public string accentHex;
            public string secondaryHex;
            public string assetId;
            public string label;
            public string subtitle;
            public string targetTrackId;
            public string author;
            public string provenance;
            public string state;
            public float quality;
            public bool geoPoseValid;
            public double latitude;
            public double longitude;
            public double altitudeM;
            public long createdAtUnixMs;
            public long updatedAtUnixMs;
            public StoredVector3 localPosition;
            public StoredVector3 localEuler;
            public StoredVector3 localScale;
        }

        [Serializable]
        public sealed class WorldAsset
        {
            public string assetId;
            public string kind;
            public string mimeType;
            public string sha256;
            public string base64Data;
            // Product-library reference set only after a signed package has been
            // validated and extracted inside the APK private storage.
            public string localFilePath;
            public string author;
        }

        /// <summary>
        /// Presentation-only rule authored in Atelier and evaluated against the
        /// live VisionRT tracks. It never creates a Memory write.
        /// </summary>
        [Serializable]
        public sealed class WorldDynamicBinding
        {
            public string bindingId;
            public string sourceMapId;
            public string targetLabel;
            public string targetKind;
            public string templateId;
            public string presetId;
            public string archetypeId;
            public string styleId;
            public string animationId;
            public string accentHex;
            public string secondaryHex;
            public string assetId;
            public string label;
            public string subtitle;
            public string attachment = "above";
            public StoredVector3 offset;
            public StoredVector3 scale;
            public float minConfidence = 0.70f;
            public int maxInstances = 3;
            public int ttlMs = 950;
            public bool enabled = true;
        }

        [Serializable]
        public sealed class WorldAnchorMapping
        {
            public string anchorGuid;
            public string nativeFileName;
            public string sha256;
            public string base64Data;
        }

        [Serializable]
        public struct StoredVector3
        {
            public float x;
            public float y;
            public float z;

            public StoredVector3(Vector3 value)
            {
                x = value.x;
                y = value.y;
                z = value.z;
            }

            public Vector3 Value => new Vector3(x, y, z);
        }

        private readonly string _path;
        private MapDocument _document;

        public WorldMapStore(
            string directory,
            string calibrationId,
            string fileName = "world-map-v1.json")
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("world map directory is required", nameof(directory));
            Directory.CreateDirectory(directory);
            string safeFile = Path.GetFileName(fileName ?? string.Empty);
            if (string.IsNullOrWhiteSpace(safeFile) ||
                !safeFile.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                safeFile = "world-map-v1.json";
            _path = Path.Combine(directory, safeFile);
            _document = LoadDocument(calibrationId);
        }

        public string FilePath => _path;
        public MapDocument Document => _document;
        public string WorldMapId => _document.worldMapId;
        public IReadOnlyList<WorldContent> Contents => _document.contents;
        public IReadOnlyList<WorldAsset> Assets => _document.assets;
        public IReadOnlyList<WorldDynamicBinding> DynamicBindings =>
            _document.dynamicBindings;
        public IReadOnlyList<WorldAnchorMapping> AnchorMappings =>
            _document.anchorMappings;
        public bool HasGeoOrigin => _document.geoOriginValid;

        public bool SetDisplayName(string displayName)
        {
            string clean = CleanText(displayName, 80);
            if (string.IsNullOrWhiteSpace(clean)) return false;
            _document.displayName = clean;
            Touch();
            Save();
            return true;
        }

        /// <summary>
        /// Fix the Earth-to-XREAL transform. A noisy fix never replaces a materially
        /// better one unless the caller explicitly starts a new calibration.
        /// </summary>
        public bool SetGeoOrigin(
            double latitude,
            double longitude,
            double altitudeM,
            float accuracyM,
            float worldNorthYawDeg,
            Vector3 localOrigin,
            bool force = false)
        {
            if (!Finite(latitude) || !Finite(longitude) || !Finite(altitudeM) ||
                !Finite(accuracyM) || accuracyM <= 0f ||
                latitude < -90d || latitude > 90d ||
                longitude < -180d || longitude > 180d)
                return false;
            if (
                !force &&
                _document.geoOriginValid &&
                accuracyM >= _document.originAccuracyM * 0.8f)
                return false;

            _document.geoOriginValid = true;
            _document.originLatitude = latitude;
            _document.originLongitude = longitude;
            _document.originAltitudeM = altitudeM;
            _document.originAccuracyM = accuracyM;
            _document.worldNorthYawDeg = NormaliseYaw(worldNorthYawDeg);
            _document.localOrigin = new StoredVector3(localOrigin);
            Touch();
            Save();
            return true;
        }

        /// <summary>Convert WGS84 coordinates to XREAL tracking-local metres.</summary>
        public bool TryGeoToLocal(
            double latitude,
            double longitude,
            double altitudeM,
            out Vector3 local)
        {
            local = default;
            if (!_document.geoOriginValid ||
                !Finite(latitude) || !Finite(longitude) || !Finite(altitudeM))
                return false;

            double lat0 = _document.originLatitude * Math.PI / 180d;
            double northM =
                (latitude - _document.originLatitude) * Math.PI / 180d * EarthRadiusM;
            double eastM =
                (longitude - _document.originLongitude) * Math.PI / 180d *
                EarthRadiusM * Math.Cos(lat0);
            double upM = altitudeM - _document.originAltitudeM;
            if (
                Math.Abs(eastM) > 20000d ||
                Math.Abs(northM) > 20000d ||
                Math.Abs(upM) > 2000d)
                return false;

            Quaternion northRotation = Quaternion.Euler(
                0f, _document.worldNorthYawDeg, 0f);
            Vector3 offset =
                northRotation * Vector3.right * (float)eastM +
                northRotation * Vector3.forward * (float)northM +
                Vector3.up * (float)upM;
            local = _document.localOrigin.Value + offset;
            return Finite(local.x) && Finite(local.y) && Finite(local.z);
        }

        public WorldContent Upsert(
            string worldContentId,
            string anchorGuid,
            string templateId,
            string label,
            string subtitle,
            string targetTrackId,
            string author,
            string provenance,
            Vector3 position,
            Quaternion rotation,
            Vector3 scale,
            float quality,
            string state = "tracking",
            bool geoPoseValid = false,
            double latitude = 0d,
            double longitude = 0d,
            double altitudeM = 0d,
            string motionPath = "static",
            float motionRadiusM = 1.5f,
            float motionSpeed = 0.8f,
            float motionHeightM = 0f)
        {
            string id = CleanId(worldContentId);
            if (string.IsNullOrEmpty(id))
                id = "world-" + Guid.NewGuid().ToString("N");
            WorldContent record = FindById(id);
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (record == null)
            {
                record = new WorldContent
                {
                    worldContentId = id,
                    createdAtUnixMs = now,
                };
                _document.contents.Add(record);
            }
            record.anchorGuid = CleanId(anchorGuid);
            record.sourceMapId = _document.worldMapId;
            record.templateId = CleanTemplate(templateId);
            record.label = CleanText(label, 120);
            record.subtitle = CleanText(subtitle, 240);
            record.targetTrackId = CleanId(targetTrackId);
            record.author = author == "automatic" ? "automatic" : "manual";
            record.provenance = CleanText(provenance, 240);
            record.state = state == "tracking" ? "tracking" : "unresolved";
            record.quality = Mathf.Clamp01(quality);
            record.geoPoseValid =
                geoPoseValid &&
                Finite(latitude) &&
                Finite(longitude) &&
                Finite(altitudeM) &&
                latitude >= -90d && latitude <= 90d &&
                longitude >= -180d && longitude <= 180d;
            record.latitude = record.geoPoseValid ? latitude : 0d;
            record.longitude = record.geoPoseValid ? longitude : 0d;
            record.altitudeM = record.geoPoseValid ? altitudeM : 0d;
            record.updatedAtUnixMs = now;
            record.localPosition = new StoredVector3(position);
            record.localEuler = new StoredVector3(rotation.eulerAngles);
            record.localScale = new StoredVector3(new Vector3(
                Mathf.Clamp(scale.x, 0.1f, MaxWorldScale),
                Mathf.Clamp(scale.y, 0.1f, MaxWorldScale),
                Mathf.Clamp(scale.z, 0.1f, MaxWorldScale)));
            record.motionPath = CleanMotionPath(motionPath);
            record.motionRadiusM = Mathf.Clamp(motionRadiusM, .1f, 40f);
            record.motionSpeed = Mathf.Clamp(motionSpeed, .05f, 5f);
            record.motionHeightM = Mathf.Clamp(motionHeightM, -20f, 20f);
            Touch();
            Save();
            return record;
        }

        public WorldContent ApplyVisualPreset(
            string worldContentId,
            WorldCreatorCatalog.Entry preset)
        {
            WorldContent record = FindById(worldContentId);
            if (record == null || preset == null) return null;
            record.templateId = CleanTemplate(preset.templateId);
            record.presetId = CleanId(preset.presetId);
            record.categoryId = CleanId(preset.categoryId);
            record.archetypeId = CleanId(preset.archetypeId);
            record.styleId = CleanId(preset.styleId);
            record.animationId = CleanId(preset.animationId);
            record.accentHex = CleanHex(preset.accentHex);
            record.secondaryHex = CleanHex(preset.secondaryHex);
            if (string.IsNullOrWhiteSpace(record.label))
                record.label = CleanText(preset.label, 120);
            if (string.IsNullOrWhiteSpace(record.subtitle))
                record.subtitle = CleanText(preset.subtitle, 240);
            Touch();
            Save();
            return record;
        }

        public bool TryAddImageAsset(
            string sourcePath,
            out string assetId,
            out string error)
        {
            assetId = string.Empty;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                error = "asset_file_missing";
                return false;
            }
            byte[] bytes;
            try
            {
                var info = new FileInfo(sourcePath);
                if (info.Length <= 0 || info.Length > MaxAssetBytes)
                {
                    error = "asset_size_invalid";
                    return false;
                }
                bytes = File.ReadAllBytes(sourcePath);
            }
            catch (Exception ex)
            {
                error = "asset_read:" + ex.GetType().Name;
                return false;
            }
            string mime = DetectImageMime(bytes);
            if (string.IsNullOrEmpty(mime))
            {
                error = "asset_format_invalid";
                return false;
            }
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!texture.LoadImage(bytes, true) ||
                    texture.width < 8 ||
                    texture.height < 8 ||
                    texture.width > MaxAssetDimension ||
                    texture.height > MaxAssetDimension)
                {
                    error = "asset_dimensions_invalid";
                    return false;
                }
            }
            finally
            {
                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(texture);
                else
                    UnityEngine.Object.DestroyImmediate(texture);
            }
            string sha = Sha256(bytes);
            WorldAsset existing = _document.assets.Find(item =>
                item != null &&
                string.Equals(item.sha256, sha, StringComparison.Ordinal));
            if (existing != null)
            {
                assetId = existing.assetId;
                return true;
            }
            if (_document.assets.Count >= MaxAssetCount)
            {
                error = "asset_count_exceeded";
                return false;
            }
            int totalBytes = bytes.Length;
            foreach (WorldAsset item in _document.assets)
            {
                if (item == null || string.IsNullOrEmpty(item.base64Data)) continue;
                totalBytes += EstimatedDecodedBytes(item.base64Data);
            }
            if (totalBytes > MaxTotalAssetBytes)
            {
                error = "asset_total_size_exceeded";
                return false;
            }
            assetId = "asset-" + sha.Substring(0, 20);
            _document.assets.Add(new WorldAsset
            {
                assetId = assetId,
                kind = "logo_image",
                mimeType = mime,
                sha256 = sha,
                base64Data = Convert.ToBase64String(bytes),
                author = "manual",
            });
            Touch();
            Save();
            return true;
        }

        public bool TryAddGlbAsset(
            string sourcePath,
            out string assetId,
            out string error)
        {
            assetId = string.Empty;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                error = "asset_file_missing";
                return false;
            }
            byte[] bytes;
            try
            {
                var info = new FileInfo(sourcePath);
                if (info.Length <= 0 || info.Length > MaxAssetBytes)
                {
                    error = "asset_size_invalid";
                    return false;
                }
                bytes = File.ReadAllBytes(sourcePath);
            }
            catch (Exception ex)
            {
                error = "asset_read:" + ex.GetType().Name;
                return false;
            }
            if (!RuntimeGlbModel.TryValidate(bytes, out error))
                return false;
            string sha = Sha256(bytes);
            WorldAsset existing = _document.assets.Find(item =>
                item != null &&
                string.Equals(item.sha256, sha, StringComparison.Ordinal));
            if (existing != null)
            {
                assetId = existing.assetId;
                return true;
            }
            if (_document.assets.Count >= MaxAssetCount)
            {
                error = "asset_count_exceeded";
                return false;
            }
            int totalBytes = bytes.Length;
            foreach (WorldAsset item in _document.assets)
            {
                if (item == null || string.IsNullOrEmpty(item.base64Data)) continue;
                totalBytes += EstimatedDecodedBytes(item.base64Data);
            }
            if (totalBytes > MaxTotalAssetBytes)
            {
                error = "asset_total_size_exceeded";
                return false;
            }
            assetId = "asset-" + sha.Substring(0, 20);
            _document.assets.Add(new WorldAsset
            {
                assetId = assetId,
                kind = "glb_model",
                mimeType = "model/gltf-binary",
                sha256 = sha,
                base64Data = Convert.ToBase64String(bytes),
                author = "manual",
            });
            Touch();
            Save();
            return true;
        }

        public WorldDynamicBinding UpsertDynamicBinding(
            string bindingId,
            WorldCreatorCatalog.Entry preset,
            string targetLabel,
            string targetKind,
            string attachment,
            string label,
            string subtitle,
            string assetId,
            Vector3 offset,
            Vector3 scale)
        {
            if (preset == null) return null;
            string id = CleanId(bindingId);
            if (string.IsNullOrEmpty(id))
                id = "dynamic-" + Guid.NewGuid().ToString("N");
            WorldDynamicBinding record = _document.dynamicBindings.Find(item =>
                item != null &&
                string.Equals(item.bindingId, id, StringComparison.Ordinal));
            if (record == null)
            {
                record = new WorldDynamicBinding { bindingId = id };
                _document.dynamicBindings.Add(record);
            }
            record.sourceMapId = _document.worldMapId;
            record.targetLabel = CleanText(targetLabel, 80).ToLowerInvariant();
            record.targetKind = CleanText(targetKind, 40).ToLowerInvariant();
            record.templateId = CleanTemplate(preset.templateId);
            record.presetId = CleanId(preset.presetId);
            record.archetypeId = CleanId(preset.archetypeId);
            record.styleId = CleanId(preset.styleId);
            record.animationId = CleanId(preset.animationId);
            record.accentHex = CleanHex(preset.accentHex);
            record.secondaryHex = CleanHex(preset.secondaryHex);
            record.assetId = FindAsset(assetId)?.assetId ?? string.Empty;
            record.label = CleanText(
                string.IsNullOrWhiteSpace(label) ? preset.label : label, 120);
            record.subtitle = CleanText(
                string.IsNullOrWhiteSpace(subtitle) ? preset.subtitle : subtitle,
                240);
            record.attachment = CleanAttachment(attachment);
            record.offset = new StoredVector3(new Vector3(
                Mathf.Clamp(offset.x, -4f, 4f),
                Mathf.Clamp(offset.y, -4f, 4f),
                Mathf.Clamp(offset.z, -4f, 4f)));
            record.scale = new StoredVector3(new Vector3(
                Mathf.Clamp(scale.x, .1f, MaxWorldScale),
                Mathf.Clamp(scale.y, .1f, MaxWorldScale),
                Mathf.Clamp(scale.z, .1f, MaxWorldScale)));
            record.enabled = true;
            Touch();
            Save();
            return record;
        }

        public bool RemoveDynamicBinding(string bindingId)
        {
            string clean = CleanId(bindingId);
            WorldDynamicBinding found = _document.dynamicBindings.Find(item =>
                item != null &&
                string.Equals(item.bindingId, clean, StringComparison.Ordinal));
            if (found == null) return false;
            _document.dynamicBindings.Remove(found);
            Touch();
            Save();
            return true;
        }

        public WorldAsset FindAsset(string assetId)
        {
            string clean = CleanId(assetId);
            if (string.IsNullOrEmpty(clean)) return null;
            return _document.assets.Find(item =>
                item != null &&
                string.Equals(item.assetId, clean, StringComparison.Ordinal));
        }

        public static string CleanMotionPath(string value)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "orbit":
                case "patrol":
                case "figure8":
                case "vertical":
                    return value.Trim().ToLowerInvariant();
                default:
                    return "static";
            }
        }

        public bool AssignAsset(string worldContentId, string assetId)
        {
            WorldContent content = FindById(worldContentId);
            WorldAsset asset = FindAsset(assetId);
            if (content == null || asset == null) return false;
            content.assetId = asset.assetId;
            content.updatedAtUnixMs =
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            Touch();
            Save();
            return true;
        }

        public bool ReplaceFromPackage(
            string packagePath,
            out string error)
        {
            if (!WorldMapPackageV1.TryRead(
                    packagePath,
                    out MapDocument imported,
                    out error))
                return false;
            _document = imported;
            _document.calibrationId = CleanId(_document.calibrationId);
            NormaliseDocument(_document);
            Touch();
            Save();
            return true;
        }

        public bool ReplaceDocument(MapDocument document)
        {
            if (document == null) return false;
            try
            {
                _document = JsonUtility.FromJson<MapDocument>(
                    JsonUtility.ToJson(document, false));
                NormaliseDocument(_document);
                Touch();
                Save();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool ExportPackage(string packagePath, out string error) =>
            WorldMapPackageV1.TryWrite(_document, packagePath, out error);

        public bool CaptureAnchorMappings(
            string mappingDirectory,
            out string error)
        {
            error = string.Empty;
            if (_document.contents == null || _document.contents.Count == 0)
            {
                _document.anchorMappings = new List<WorldAnchorMapping>();
                Touch();
                Save();
                return true;
            }
            if (
                string.IsNullOrWhiteSpace(mappingDirectory) ||
                !Directory.Exists(mappingDirectory))
            {
                error = "anchor_mapping_directory_missing";
                return false;
            }
            var unique = new HashSet<string>(StringComparer.Ordinal);
            var captured = new List<WorldAnchorMapping>();
            int totalBytes = 0;
            foreach (WorldContent content in _document.contents)
            {
                if (
                    content == null ||
                    string.IsNullOrWhiteSpace(content.anchorGuid) ||
                    !unique.Add(content.anchorGuid))
                    continue;
                if (!TryAnchorNativeFileName(
                        content.anchorGuid,
                        out string fileName))
                {
                    error = "anchor_guid_invalid:" + content.anchorGuid;
                    return false;
                }
                string path = Path.Combine(mappingDirectory, fileName);
                if (!File.Exists(path))
                {
                    error = "anchor_mapping_missing:" + fileName;
                    return false;
                }
                byte[] bytes = File.ReadAllBytes(path);
                if (
                    bytes.Length <= 0 ||
                    bytes.Length > MaxAnchorMappingBytes)
                {
                    error = "anchor_mapping_size_invalid:" + fileName;
                    return false;
                }
                totalBytes += bytes.Length;
                if (totalBytes > MaxTotalAnchorMappingBytes)
                {
                    error = "anchor_mappings_total_size_exceeded";
                    return false;
                }
                captured.Add(new WorldAnchorMapping
                {
                    anchorGuid = content.anchorGuid,
                    nativeFileName = fileName,
                    sha256 = Sha256(bytes),
                    base64Data = Convert.ToBase64String(bytes),
                });
            }
            if (captured.Count != unique.Count)
            {
                error = "anchor_mapping_count_mismatch";
                return false;
            }
            _document.anchorMappings = captured;
            Touch();
            Save();
            return true;
        }

        public bool InstallAnchorMappings(
            string mappingDirectory,
            out string error)
        {
            error = string.Empty;
            if (_document.anchorMappings == null)
            {
                error = "anchor_mappings_missing";
                return false;
            }
            Directory.CreateDirectory(mappingDirectory);
            try
            {
                foreach (WorldAnchorMapping mapping in _document.anchorMappings)
                {
                    if (
                        mapping == null ||
                        !TryAnchorNativeFileName(
                            mapping.anchorGuid,
                            out string expected) ||
                        !string.Equals(
                            expected,
                            mapping.nativeFileName,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        error = "anchor_mapping_name_invalid";
                        return false;
                    }
                    byte[] bytes =
                        Convert.FromBase64String(mapping.base64Data);
                    if (
                        bytes.Length <= 0 ||
                        bytes.Length > MaxAnchorMappingBytes ||
                        !string.Equals(
                            Sha256(bytes),
                            mapping.sha256,
                            StringComparison.Ordinal))
                    {
                        error = "anchor_mapping_digest_invalid";
                        return false;
                    }
                    string destination =
                        Path.Combine(mappingDirectory, expected);
                    string temp = destination + ".import";
                    File.WriteAllBytes(temp, bytes);
                    if (File.Exists(destination))
                        File.Delete(destination);
                    File.Move(temp, destination);
                }
                return true;
            }
            catch (Exception ex)
            {
                error = "anchor_mapping_install:" +
                    ex.GetType().Name;
                return false;
            }
        }

        public void ReleaseEmbeddedAnchorMappings()
        {
            if (_document.anchorMappings == null) return;
            bool changed = false;
            foreach (WorldAnchorMapping mapping in _document.anchorMappings)
            {
                if (mapping == null || string.IsNullOrEmpty(mapping.base64Data))
                    continue;
                mapping.base64Data = string.Empty;
                changed = true;
            }
            if (!changed) return;
            Touch();
            Save();
        }

        public WorldContent FindById(string id)
        {
            string clean = CleanId(id);
            if (string.IsNullOrEmpty(clean)) return null;
            return _document.contents.Find(item =>
                item != null &&
                string.Equals(item.worldContentId, clean, StringComparison.Ordinal));
        }

        public WorldContent FindByAnchor(string anchorGuid)
        {
            string clean = CleanId(anchorGuid);
            if (string.IsNullOrEmpty(clean)) return null;
            return _document.contents.Find(item =>
                item != null &&
                string.Equals(item.anchorGuid, clean, StringComparison.Ordinal));
        }

        public bool Remove(string worldContentId)
        {
            WorldContent found = FindById(worldContentId);
            if (found == null) return false;
            _document.contents.Remove(found);
            Touch();
            Save();
            return true;
        }

        public void MarkUnresolved(string anchorGuid)
        {
            WorldContent found = FindByAnchor(anchorGuid);
            if (found == null || found.state == "unresolved") return;
            found.state = "unresolved";
            found.updatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            Touch();
            Save();
        }

        public void Save()
        {
            string json = JsonUtility.ToJson(_document, true);
            string temp = _path + ".tmp";
            File.WriteAllText(temp, json);
            try
            {
                if (File.Exists(_path))
                    File.Replace(temp, _path, null);
                else
                    File.Move(temp, _path);
            }
            catch (PlatformNotSupportedException)
            {
                File.Copy(temp, _path, true);
                File.Delete(temp);
            }
            catch (IOException)
            {
                File.Copy(temp, _path, true);
                File.Delete(temp);
            }
        }

        private MapDocument LoadDocument(string calibrationId)
        {
            MapDocument loaded = null;
            if (File.Exists(_path))
            {
                try
                {
                    loaded = JsonUtility.FromJson<MapDocument>(
                        File.ReadAllText(_path));
                }
                catch (Exception ex)
                {
                    Debug.LogWarning(
                        "[WorldMapStore] corrupt map ignored: " +
                        ex.GetType().Name);
                }
            }
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (
                loaded == null ||
                loaded.schemaVersion != CurrentSchemaVersion ||
                string.IsNullOrWhiteSpace(loaded.worldMapId))
            {
                loaded = new MapDocument
                {
                    schemaVersion = CurrentSchemaVersion,
                    worldMapId = "worldmap-" + Guid.NewGuid().ToString("N"),
                    calibrationId = CleanId(calibrationId),
                    createdAtUnixMs = now,
                    updatedAtUnixMs = now,
                    contents = new List<WorldContent>(),
                    localOrigin = new StoredVector3(Vector3.zero),
                };
            }
            if (loaded.contents == null)
                loaded.contents = new List<WorldContent>();
            if (loaded.assets == null)
                loaded.assets = new List<WorldAsset>();
            if (loaded.dynamicBindings == null)
                loaded.dynamicBindings = new List<WorldDynamicBinding>();
            if (loaded.anchorMappings == null)
                loaded.anchorMappings = new List<WorldAnchorMapping>();
            loaded.contents.RemoveAll(item =>
                item == null || string.IsNullOrWhiteSpace(item.worldContentId));
            NormaliseDocument(loaded);
            return loaded;
        }

        private static void NormaliseDocument(MapDocument document)
        {
            if (document == null) return;
            if (document.contents == null)
                document.contents = new List<WorldContent>();
            if (document.assets == null)
                document.assets = new List<WorldAsset>();
            if (document.dynamicBindings == null)
                document.dynamicBindings = new List<WorldDynamicBinding>();
            if (document.anchorMappings == null)
                document.anchorMappings = new List<WorldAnchorMapping>();
            if (string.IsNullOrWhiteSpace(document.displayName))
                document.displayName = "Monde " +
                    (document.worldMapId ?? "local").Substring(
                        0, Mathf.Min(8, (document.worldMapId ?? "local").Length));
            foreach (WorldContent content in document.contents)
                if (content != null && string.IsNullOrWhiteSpace(content.sourceMapId))
                    content.sourceMapId = document.worldMapId;
            foreach (WorldDynamicBinding binding in document.dynamicBindings)
                if (binding != null && string.IsNullOrWhiteSpace(binding.sourceMapId))
                    binding.sourceMapId = document.worldMapId;
        }

        private void Touch() =>
            _document.updatedAtUnixMs =
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        private static string CleanTemplate(string value)
        {
            string clean = CleanId(value);
            switch (clean)
            {
                case "neon_sign":
                case "holo_billboard":
                case "vehicle_fx":
                case "poi_beacon":
                case "memory_echo":
                case "annotation":
                case "portal_arch":
                case "sky_drone":
                case "giant_hologram":
                case "direction_arrow":
                case "building_crown":
                case "window_display":
                case "particle_column":
                case "street_totem":
                case "home_widget":
                case "room_boundary":
                case "logo_orbit":
                case "warning_barrier":
                    return clean;
                default:
                    return "neon_sign";
            }
        }

        private static string DetectImageMime(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 12) return string.Empty;
            if (
                bytes[0] == 0x89 &&
                bytes[1] == 0x50 &&
                bytes[2] == 0x4E &&
                bytes[3] == 0x47)
                return "image/png";
            if (bytes[0] == 0xFF && bytes[1] == 0xD8)
                return "image/jpeg";
            return string.Empty;
        }

        private static string CleanAttachment(string value)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "center":
                case "above":
                case "below":
                case "front":
                case "rear":
                case "left":
                case "right":
                    return value.Trim().ToLowerInvariant();
                default:
                    return "above";
            }
        }

        private static string Sha256(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(bytes);
                var chars = new char[digest.Length * 2];
                const string hex = "0123456789abcdef";
                for (int i = 0; i < digest.Length; i++)
                {
                    chars[i * 2] = hex[digest[i] >> 4];
                    chars[i * 2 + 1] = hex[digest[i] & 0x0f];
                }
                return new string(chars);
            }
        }

        private static int EstimatedDecodedBytes(string base64) =>
            string.IsNullOrEmpty(base64) ? 0 : base64.Length * 3 / 4;

        private static bool TryAnchorNativeFileName(
            string serializableGuid,
            out string fileName)
        {
            fileName = string.Empty;
            string[] parts =
                (serializableGuid ?? string.Empty).Split('-');
            if (
                parts.Length != 2 ||
                !ulong.TryParse(
                    parts[0],
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out ulong low) ||
                !ulong.TryParse(
                    parts[1],
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture,
                    out ulong high))
                return false;
            var guid = new Guid(
                (uint)(low & 0xffffffff),
                (ushort)((low >> 32) & 0xffff),
                (ushort)((low >> 48) & 0xffff),
                (byte)(high & 0xff),
                (byte)((high >> 8) & 0xff),
                (byte)((high >> 16) & 0xff),
                (byte)((high >> 24) & 0xff),
                (byte)((high >> 32) & 0xff),
                (byte)((high >> 40) & 0xff),
                (byte)((high >> 48) & 0xff),
                (byte)((high >> 56) & 0xff));
            fileName = guid.ToString();
            return guid != Guid.Empty;
        }

        private static string CleanId(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var chars = new char[Math.Min(value.Length, 160)];
            int count = 0;
            foreach (char c in value.Trim())
            {
                if (count >= chars.Length) break;
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == ':' || c == '.')
                    chars[count++] = c;
            }
            return new string(chars, 0, count);
        }

        private static string CleanText(string value, int limit)
        {
            string clean = string.Join(
                " ",
                (value ?? string.Empty).Split(
                    (char[])null,
                    StringSplitOptions.RemoveEmptyEntries));
            return clean.Length <= limit ? clean : clean.Substring(0, limit);
        }

        private static string CleanHex(string value)
        {
            string clean = (value ?? string.Empty).Trim().TrimStart('#');
            if (clean.Length != 6 && clean.Length != 8) return "18E8FF";
            foreach (char c in clean)
            {
                bool hex =
                    (c >= '0' && c <= '9') ||
                    (c >= 'a' && c <= 'f') ||
                    (c >= 'A' && c <= 'F');
                if (!hex) return "18E8FF";
            }
            return clean.ToUpperInvariant();
        }

        private static float NormaliseYaw(float yaw)
        {
            if (!Finite(yaw)) return 0f;
            return Mathf.Repeat(yaw + 180f, 360f) - 180f;
        }

        private static bool Finite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);

        private static bool Finite(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
