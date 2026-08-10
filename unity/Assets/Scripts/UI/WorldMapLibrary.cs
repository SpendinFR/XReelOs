using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace MLOmega.XR.UI
{
    [Serializable]
    public sealed class WorldMapSelection
    {
        public string mapId;
        public string displayName;
        public bool active;
        public int anchoredCount;
        public int dynamicCount;
    }

    /// <summary>
    /// Device-local package library. Imported maps remain individually signed by
    /// their mlomega.world-map envelope; the active composition is rebuilt from
    /// those packages and never touches Memory/session storage.
    /// </summary>
    public sealed class WorldMapLibrary
    {
        [Serializable]
        private sealed class Entry
        {
            public string mapId;
            public string displayName;
            public string fileName;
            public bool active;
            public int anchoredCount;
            public int dynamicCount;
        }

        [Serializable]
        private sealed class Index
        {
            public int schemaVersion = 1;
            public List<Entry> entries = new List<Entry>();
        }

        private readonly string _directory;
        private readonly string _packagesDirectory;
        private readonly string _assetsDirectory;
        private readonly string _indexPath;
        private Index _index;

        public WorldMapLibrary(string directory)
        {
            _directory = directory ?? throw new ArgumentNullException(nameof(directory));
            _packagesDirectory = Path.Combine(_directory, "packages");
            _assetsDirectory = Path.Combine(_directory, "assets");
            _indexPath = Path.Combine(_directory, "world-map-library-v1.json");
            Directory.CreateDirectory(_packagesDirectory);
            Directory.CreateDirectory(_assetsDirectory);
            _index = LoadIndex();
            PruneMissing();
        }

        public IReadOnlyList<WorldMapSelection> Selections =>
            _index.entries
                .OrderBy(entry => entry.displayName, StringComparer.OrdinalIgnoreCase)
                .Select(entry => new WorldMapSelection
                {
                    mapId = entry.mapId,
                    displayName = entry.displayName,
                    active = entry.active,
                    anchoredCount = entry.anchoredCount,
                    dynamicCount = entry.dynamicCount,
                })
                .ToList();

        public bool InstallPackage(
            string sourcePath,
            bool activate,
            out string mapId,
            out string error)
        {
            mapId = string.Empty;
            if (!WorldMapPackageV1.TryRead(
                    sourcePath,
                    out WorldMapStore.MapDocument map,
                    out error))
                return false;
            mapId = map.worldMapId;
            string installedMapId = mapId;
            string fileName = SafeMapFile(mapId);
            string destination = Path.Combine(_packagesDirectory, fileName);
            try
            {
                foreach (WorldMapStore.WorldAsset asset in map.assets)
                {
                    byte[] bytes = Convert.FromBase64String(asset.base64Data);
                    string extension = asset.kind == "glb_model"
                        ? ".glb"
                        : asset.mimeType == "image/png" ? ".png" : ".jpg";
                    string assetFile = asset.sha256 + extension;
                    string assetPath = Path.Combine(_assetsDirectory, assetFile);
                    if (!File.Exists(assetPath))
                    {
                        File.WriteAllBytes(assetPath + ".tmp", bytes);
                        File.Move(assetPath + ".tmp", assetPath);
                    }
                    else if (!string.Equals(
                                 Sha256File(assetPath),
                                 asset.sha256,
                                 StringComparison.Ordinal))
                    {
                        throw new InvalidDataException("asset_digest_collision");
                    }
                    asset.localFilePath = assetPath;
                    asset.base64Data = string.Empty;
                }
                File.WriteAllText(
                    destination + ".tmp",
                    JsonUtility.ToJson(map, false),
                    Encoding.UTF8);
                if (File.Exists(destination)) File.Delete(destination);
                File.Move(destination + ".tmp", destination);
            }
            catch (Exception ex)
            {
                error = "world_map_library_install:" + ex.GetType().Name;
                return false;
            }
            Entry entry = _index.entries.Find(item =>
                string.Equals(item.mapId, installedMapId, StringComparison.Ordinal));
            if (entry == null)
            {
                entry = new Entry { mapId = mapId };
                _index.entries.Add(entry);
            }
            entry.displayName = string.IsNullOrWhiteSpace(map.displayName)
                ? mapId
                : map.displayName;
            entry.fileName = fileName;
            entry.active = activate || entry.active;
            entry.anchoredCount = map.contents?.Count ?? 0;
            entry.dynamicCount = map.dynamicBindings?.Count ?? 0;
            SaveIndex();
            GarbageCollectAssets();
            error = string.Empty;
            return true;
        }

        public bool SetActive(string mapId, bool active)
        {
            Entry entry = _index.entries.Find(item =>
                string.Equals(item.mapId, mapId, StringComparison.Ordinal));
            if (entry == null) return false;
            entry.active = active;
            SaveIndex();
            GarbageCollectAssets();
            return true;
        }

        public bool Remove(string mapId)
        {
            Entry entry = _index.entries.Find(item =>
                string.Equals(item.mapId, mapId, StringComparison.Ordinal));
            if (entry == null) return false;
            _index.entries.Remove(entry);
            string path = Path.Combine(_packagesDirectory, entry.fileName);
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException)
            {
                return false;
            }
            SaveIndex();
            return true;
        }

        public bool TryComposeActive(
            out WorldMapStore.MapDocument composition,
            out string error)
        {
            composition = NewComposition();
            error = string.Empty;
            var assetBySha = new Dictionary<string, WorldMapStore.WorldAsset>(
                StringComparer.Ordinal);
            var mappingByAnchor =
                new Dictionary<string, WorldMapStore.WorldAnchorMapping>(
                    StringComparer.Ordinal);
            int activeCount = 0;
            foreach (Entry entry in _index.entries.Where(item => item.active))
            {
                string path = Path.Combine(_packagesDirectory, entry.fileName);
                if (!TryReadInstalledMap(
                        path,
                        out WorldMapStore.MapDocument map,
                        out error))
                    return false;
                activeCount++;
                var remap = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (WorldMapStore.WorldAsset asset in map.assets)
                {
                    if (assetBySha.TryGetValue(asset.sha256, out var existing))
                    {
                        remap[asset.assetId] = existing.assetId;
                        continue;
                    }
                    WorldMapStore.WorldAsset clone = Clone(asset);
                    string newId = UniqueAssetId(
                        composition.assets, clone.assetId, clone.sha256);
                    remap[asset.assetId] = newId;
                    clone.assetId = newId;
                    composition.assets.Add(clone);
                    assetBySha[clone.sha256] = clone;
                }
                foreach (WorldMapStore.WorldAnchorMapping mapping in
                    map.anchorMappings)
                {
                    if (mappingByAnchor.TryGetValue(
                            mapping.anchorGuid, out var existing))
                    {
                        if (!string.Equals(
                                existing.sha256,
                                mapping.sha256,
                                StringComparison.Ordinal))
                        {
                            error = "world_map_anchor_collision:" +
                                mapping.anchorGuid;
                            return false;
                        }
                        continue;
                    }
                    WorldMapStore.WorldAnchorMapping clone = Clone(mapping);
                    composition.anchorMappings.Add(clone);
                    mappingByAnchor[clone.anchorGuid] = clone;
                }
                foreach (WorldMapStore.WorldContent content in map.contents)
                {
                    WorldMapStore.WorldContent clone = Clone(content);
                    clone.sourceMapId = map.worldMapId;
                    clone.worldContentId =
                        Prefix(map.worldMapId) + "-" + clone.worldContentId;
                    if (!string.IsNullOrWhiteSpace(clone.assetId) &&
                        remap.TryGetValue(clone.assetId, out string mappedAsset))
                        clone.assetId = mappedAsset;
                    composition.contents.Add(clone);
                }
                foreach (WorldMapStore.WorldDynamicBinding binding in
                    map.dynamicBindings ?? new List<WorldMapStore.WorldDynamicBinding>())
                {
                    WorldMapStore.WorldDynamicBinding clone = Clone(binding);
                    clone.sourceMapId = map.worldMapId;
                    clone.bindingId =
                        Prefix(map.worldMapId) + "-" + clone.bindingId;
                    if (!string.IsNullOrWhiteSpace(clone.assetId) &&
                        remap.TryGetValue(clone.assetId, out string mappedAsset))
                        clone.assetId = mappedAsset;
                    composition.dynamicBindings.Add(clone);
                }
            }
            composition.displayName = activeCount == 0
                ? "Aucun monde actif"
                : activeCount + " monde(s) actif(s)";
            composition.updatedAtUnixMs =
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return true;
        }

        private Index LoadIndex()
        {
            try
            {
                if (File.Exists(_indexPath))
                {
                    Index loaded = JsonUtility.FromJson<Index>(
                        File.ReadAllText(_indexPath));
                    if (loaded?.entries != null) return loaded;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "[WorldMapLibrary] index ignored: " + ex.GetType().Name);
            }
            return new Index();
        }

        private void PruneMissing()
        {
            int before = _index.entries.Count;
            _index.entries.RemoveAll(entry =>
                entry == null ||
                string.IsNullOrWhiteSpace(entry.mapId) ||
                string.IsNullOrWhiteSpace(entry.fileName) ||
                !File.Exists(Path.Combine(_packagesDirectory, entry.fileName)));
            if (_index.entries.Count != before) SaveIndex();
        }

        private void SaveIndex()
        {
            Directory.CreateDirectory(_directory);
            string temp = _indexPath + ".tmp";
            File.WriteAllText(temp, JsonUtility.ToJson(_index, true));
            if (File.Exists(_indexPath)) File.Delete(_indexPath);
            File.Move(temp, _indexPath);
        }

        private bool TryReadInstalledMap(
            string path,
            out WorldMapStore.MapDocument map,
            out string error)
        {
            map = null;
            error = string.Empty;
            try
            {
                map = JsonUtility.FromJson<WorldMapStore.MapDocument>(
                    File.ReadAllText(path, Encoding.UTF8));
                if (
                    map == null ||
                    map.schemaVersion != WorldMapStore.CurrentSchemaVersion ||
                    map.assets == null ||
                    map.contents == null ||
                    map.dynamicBindings == null ||
                    map.anchorMappings == null)
                {
                    error = "world_map_library_document_invalid";
                    return false;
                }
                foreach (WorldMapStore.WorldAsset asset in map.assets)
                {
                    if (
                        asset == null ||
                        string.IsNullOrWhiteSpace(asset.sha256) ||
                        string.IsNullOrWhiteSpace(asset.localFilePath) ||
                        !InsideAssetsDirectory(asset.localFilePath) ||
                        !File.Exists(asset.localFilePath) ||
                        new FileInfo(asset.localFilePath).Length <= 0 ||
                        new FileInfo(asset.localFilePath).Length >
                            WorldMapStore.MaxAssetBytes ||
                        !string.Equals(
                            Sha256File(asset.localFilePath),
                            asset.sha256,
                            StringComparison.Ordinal))
                    {
                        error = "world_map_library_asset_invalid";
                        return false;
                    }
                    asset.base64Data = string.Empty;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = "world_map_library_read:" + ex.GetType().Name;
                return false;
            }
        }

        private bool InsideAssetsDirectory(string path)
        {
            string root = Path.GetFullPath(_assetsDirectory)
                .TrimEnd(Path.DirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            string candidate = Path.GetFullPath(path);
            return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }

        private void GarbageCollectAssets()
        {
            var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Entry entry in _index.entries)
            {
                string path = Path.Combine(_packagesDirectory, entry.fileName);
                try
                {
                    WorldMapStore.MapDocument map =
                        JsonUtility.FromJson<WorldMapStore.MapDocument>(
                            File.ReadAllText(path, Encoding.UTF8));
                    foreach (WorldMapStore.WorldAsset asset in
                        map?.assets ?? new List<WorldMapStore.WorldAsset>())
                    {
                        if (InsideAssetsDirectory(asset.localFilePath))
                            referenced.Add(Path.GetFullPath(asset.localFilePath));
                    }
                }
                catch
                {
                    // Corrupt maps are handled by composition; never delete on
                    // the basis of an unreadable entry.
                    return;
                }
            }
            foreach (string path in Directory.GetFiles(_assetsDirectory))
            {
                if (!referenced.Contains(Path.GetFullPath(path)))
                    File.Delete(path);
            }
        }

        private static string Sha256File(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 hash = SHA256.Create())
            {
                byte[] digest = hash.ComputeHash(stream);
                var result = new StringBuilder(digest.Length * 2);
                foreach (byte item in digest) result.Append(item.ToString("x2"));
                return result.ToString();
            }
        }

        private static WorldMapStore.MapDocument NewComposition()
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return new WorldMapStore.MapDocument
            {
                schemaVersion = WorldMapStore.CurrentSchemaVersion,
                worldMapId = "worldmap-composite",
                displayName = "Composition FreeGuy",
                calibrationId = "xreal-composed",
                createdAtUnixMs = now,
                updatedAtUnixMs = now,
                contents = new List<WorldMapStore.WorldContent>(),
                assets = new List<WorldMapStore.WorldAsset>(),
                dynamicBindings =
                    new List<WorldMapStore.WorldDynamicBinding>(),
                anchorMappings =
                    new List<WorldMapStore.WorldAnchorMapping>(),
            };
        }

        private static string UniqueAssetId(
            IEnumerable<WorldMapStore.WorldAsset> existing,
            string requested,
            string sha)
        {
            string candidate = requested;
            if (existing.All(item => item.assetId != candidate)) return candidate;
            candidate = "asset-" + (sha ?? Guid.NewGuid().ToString("N"))
                .Substring(0, 20);
            return candidate;
        }

        private static string Prefix(string mapId)
        {
            string clean = (mapId ?? "map").Replace("-", string.Empty);
            return clean.Substring(0, Math.Min(12, clean.Length));
        }

        private static string SafeMapFile(string mapId)
        {
            string clean = string.Concat(
                (mapId ?? "map").Where(
                    character => char.IsLetterOrDigit(character) ||
                        character == '-' || character == '_'));
            if (clean.Length > 80) clean = clean.Substring(0, 80);
            return clean + ".world-map-v1.json";
        }

        private static T Clone<T>(T value) =>
            JsonUtility.FromJson<T>(JsonUtility.ToJson(value, false));
    }

    /// <summary>Named draft workspaces used only inside the Atelier APK.</summary>
    public sealed class WorldMapDraftLibrary
    {
        private readonly string _directory;
        private readonly string _calibrationId;

        public WorldMapDraftLibrary(string directory, string calibrationId)
        {
            _directory = directory;
            _calibrationId = calibrationId;
            Directory.CreateDirectory(_directory);
        }

        public IReadOnlyList<WorldMapSelection> List()
        {
            var result = new List<WorldMapSelection>();
            foreach (string path in Directory.GetFiles(
                _directory, "world-map*.json", SearchOption.TopDirectoryOnly))
            {
                if (path.EndsWith(
                        "world-map-library-v1.json",
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                try
                {
                    var map = JsonUtility.FromJson<WorldMapStore.MapDocument>(
                        File.ReadAllText(path));
                    if (map == null || string.IsNullOrWhiteSpace(map.worldMapId))
                        continue;
                    result.Add(new WorldMapSelection
                    {
                        mapId = map.worldMapId,
                        displayName = string.IsNullOrWhiteSpace(map.displayName)
                            ? map.worldMapId
                            : map.displayName,
                        active = false,
                        anchoredCount = map.contents?.Count ?? 0,
                        dynamicCount = map.dynamicBindings?.Count ?? 0,
                    });
                }
                catch
                {
                    // A draft must never make another map unusable.
                }
            }
            return result
                .OrderBy(item => item.displayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public WorldMapStore Create(string displayName)
        {
            string file = "world-map-draft-" +
                Guid.NewGuid().ToString("N") + ".json";
            var store = new WorldMapStore(_directory, _calibrationId, file);
            store.SetDisplayName(
                string.IsNullOrWhiteSpace(displayName)
                    ? "Nouveau monde"
                    : displayName);
            return store;
        }

        public WorldMapStore Open(string mapId)
        {
            foreach (string path in Directory.GetFiles(
                _directory, "world-map*.json", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var map = JsonUtility.FromJson<WorldMapStore.MapDocument>(
                        File.ReadAllText(path));
                    if (map != null && string.Equals(
                            map.worldMapId, mapId, StringComparison.Ordinal))
                        return new WorldMapStore(
                            _directory,
                            _calibrationId,
                            Path.GetFileName(path));
                }
                catch
                {
                }
            }
            return null;
        }

        public bool Delete(string mapId)
        {
            WorldMapStore store = Open(mapId);
            if (store == null || store.Contents.Count > 0) return false;
            try
            {
                File.Delete(store.FilePath);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
        }
    }
}
