using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MLOmega.XR.UI
{
    /// <summary>
    /// Device-local indoor trail graph learned while the wearer walks.
    ///
    /// XREAL tracking supplies geometry. Salted Wi-Fi/BLE identifiers and the
    /// magnetic vector only relocalise a previously visited node; they never claim
    /// coordinates by themselves. A first visit can therefore guide the wearer
    /// back along the traversed path, while a later visit reuses the saved graph.
    /// </summary>
    public sealed class IndoorLiveMapStore
    {
        public const int CurrentSchemaVersion = 1;
        private const float NodeSpacingM = 1.20f;
        private const int MaxNodes = 4096;

        [Serializable]
        public sealed class MapDocument
        {
            public int schemaVersion = CurrentSchemaVersion;
            public string mapId;
            public long createdAtUnixMs;
            public long updatedAtUnixMs;
            public List<MapNode> nodes = new List<MapNode>();
            public List<MapEdge> edges = new List<MapEdge>();
        }

        [Serializable]
        public sealed class MapNode
        {
            public string nodeId;
            public string label;
            public WorldMapStore.StoredVector3 mapPosition;
            public float northYawDeg;
            public RadioFingerprint fingerprint;
            public long firstSeenUnixMs;
            public long lastSeenUnixMs;
            public int observationCount;
        }

        [Serializable]
        public sealed class MapEdge
        {
            public string fromNodeId;
            public string toNodeId;
            public float distanceM;
        }

        [Serializable]
        public sealed class RadioFingerprint
        {
            public int schema_version;
            public long captured_at_unix_ms;
            public List<RadioReading> wifi = new List<RadioReading>();
            public List<RadioReading> ble = new List<RadioReading>();
            public MagneticReading magnetic = new MagneticReading();
            public bool radio_permission;
        }

        [Serializable]
        public sealed class RadioReading
        {
            public string id;
            public int rssi;
            public int frequency_mhz;
        }

        [Serializable]
        public sealed class MagneticReading
        {
            public float x_ut;
            public float y_ut;
            public float z_ut;
            public float magnitude_ut;
        }

        public sealed class RouteResult
        {
            public string MapId;
            public string Destination;
            public float Quality;
            public float DistanceM;
            public List<Vector3> TrackingLocalPoints = new List<Vector3>();
            public List<string> NodeIds = new List<string>();
        }

        private readonly string _path;
        private MapDocument _document;
        private bool _sessionOriginSet;
        private Vector3 _sessionOriginLocal;
        private Vector3 _sessionOriginMap;
        private float _sessionNorthYaw;
        private string _currentNodeId;
        private Vector3 _lastObservedLocal;
        private bool _hasLastObservedLocal;
        private float _lastRelocalisationQuality;

        public IndoorLiveMapStore(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("indoor map directory is required", nameof(directory));
            Directory.CreateDirectory(directory);
            _path = Path.Combine(directory, "indoor-live-map-v1.json");
            _document = Load();
        }

        public string FilePath => _path;
        public string MapId => _document.mapId;
        public int NodeCount => _document.nodes.Count;
        public int EdgeCount => _document.edges.Count;
        public bool IsRelocalised => _sessionOriginSet;
        public string CurrentNodeId => _currentNodeId;
        public float LastRelocalisationQuality => _lastRelocalisationQuality;

        public bool Observe(
            Vector3 trackingLocalPosition,
            float worldNorthYawDeg,
            string fingerprintJson,
            out string state)
        {
            state = "ignored";
            if (!Finite(trackingLocalPosition) || !Finite(worldNorthYawDeg))
                return false;
            RadioFingerprint fingerprint = ParseFingerprint(fingerprintJson);
            // Radio is intentionally consulted only at session bootstrap. Wi-Fi
            // fingerprints often remain nearly identical across several metres;
            // snapping on every sample would collapse a walked path onto one node.
            float matchQuality = 0f;
            MapNode matched = null;
            if (!_sessionOriginSet)
                matched = BestFingerprintMatch(fingerprint, out matchQuality);

            if (!_sessionOriginSet)
            {
                _sessionOriginSet = true;
                _sessionOriginLocal = trackingLocalPosition;
                _sessionNorthYaw = NormaliseYaw(worldNorthYawDeg);
                if (matched != null)
                {
                    _sessionOriginMap = matched.mapPosition.Value;
                    _currentNodeId = matched.nodeId;
                    _lastRelocalisationQuality = matchQuality;
                    TouchNode(matched, fingerprint);
                    state = "relocalised";
                }
                else
                {
                    _sessionOriginMap = Vector3.zero;
                    MapNode first = AddNode(
                        Vector3.zero,
                        _document.nodes.Count == 0 ? "départ" : string.Empty,
                        fingerprint,
                        worldNorthYawDeg);
                    _currentNodeId = first.nodeId;
                    _lastRelocalisationQuality = 0f;
                    state = "mapping_started";
                }
                _lastObservedLocal = trackingLocalPosition;
                _hasLastObservedLocal = true;
                Save();
                return true;
            }

            Vector3 mapPosition = LocalToMap(trackingLocalPosition);
            if (_hasLastObservedLocal &&
                Vector3.Distance(_lastObservedLocal, trackingLocalPosition) < NodeSpacingM)
            {
                state = "tracking";
                return true;
            }
            _lastObservedLocal = trackingLocalPosition;
            _hasLastObservedLocal = true;
            if (_document.nodes.Count >= MaxNodes)
            {
                state = "map_full";
                return false;
            }

            string previous = _currentNodeId;
            MapNode node = AddNode(
                mapPosition, string.Empty, fingerprint, worldNorthYawDeg);
            _currentNodeId = node.nodeId;
            AddEdge(previous, node.nodeId);
            state = "node_added";
            Save();
            return true;
        }

        public bool NameCurrent(string label)
        {
            string clean = CleanLabel(label);
            MapNode node = FindNode(_currentNodeId);
            if (node == null || string.IsNullOrEmpty(clean)) return false;
            node.label = clean;
            node.lastSeenUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            Touch();
            Save();
            return true;
        }

        public bool TryRoute(string destination, out RouteResult result)
        {
            result = null;
            if (!_sessionOriginSet || string.IsNullOrWhiteSpace(_currentNodeId))
                return false;
            MapNode target = FindDestination(destination);
            MapNode start = FindNode(_currentNodeId);
            if (start == null || target == null) return false;

            var distances = new Dictionary<string, float>(StringComparer.Ordinal);
            var previous = new Dictionary<string, string>(StringComparer.Ordinal);
            var unvisited = new HashSet<string>(StringComparer.Ordinal);
            foreach (MapNode node in _document.nodes)
            {
                if (node == null || string.IsNullOrWhiteSpace(node.nodeId)) continue;
                distances[node.nodeId] = node.nodeId == start.nodeId
                    ? 0f
                    : float.PositiveInfinity;
                unvisited.Add(node.nodeId);
            }
            while (unvisited.Count > 0)
            {
                string current = null;
                float best = float.PositiveInfinity;
                foreach (string id in unvisited)
                {
                    float distance = distances[id];
                    if (distance < best)
                    {
                        current = id;
                        best = distance;
                    }
                }
                if (current == null || float.IsPositiveInfinity(best)) break;
                unvisited.Remove(current);
                if (current == target.nodeId) break;
                foreach (MapEdge edge in _document.edges)
                {
                    string neighbour = null;
                    if (edge.fromNodeId == current) neighbour = edge.toNodeId;
                    else if (edge.toNodeId == current) neighbour = edge.fromNodeId;
                    if (neighbour == null || !unvisited.Contains(neighbour)) continue;
                    float candidate = best + Mathf.Max(0.05f, edge.distanceM);
                    if (candidate < distances[neighbour])
                    {
                        distances[neighbour] = candidate;
                        previous[neighbour] = current;
                    }
                }
            }
            if (!distances.TryGetValue(target.nodeId, out float total) ||
                float.IsPositiveInfinity(total))
                return false;

            var ids = new List<string> { target.nodeId };
            string cursor = target.nodeId;
            while (cursor != start.nodeId)
            {
                if (!previous.TryGetValue(cursor, out cursor)) return false;
                ids.Add(cursor);
                if (ids.Count > MaxNodes) return false;
            }
            ids.Reverse();
            var route = new RouteResult
            {
                MapId = _document.mapId,
                Destination = string.IsNullOrEmpty(target.label)
                    ? destination.Trim()
                    : target.label,
                DistanceM = total,
                Quality = Mathf.Clamp(
                    0.72f + _lastRelocalisationQuality * 0.22f,
                    0.72f,
                    0.94f),
                NodeIds = ids,
            };
            foreach (string id in ids)
            {
                MapNode node = FindNode(id);
                if (node != null)
                    route.TrackingLocalPoints.Add(MapToLocal(node.mapPosition.Value));
            }
            if (route.TrackingLocalPoints.Count < 2) return false;
            result = route;
            return true;
        }

        private MapNode AddNode(
            Vector3 mapPosition,
            string label,
            RadioFingerprint fingerprint,
            float northYawDeg)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var node = new MapNode
            {
                nodeId = "indoor-" + Guid.NewGuid().ToString("N"),
                label = CleanLabel(label),
                mapPosition = new WorldMapStore.StoredVector3(mapPosition),
                northYawDeg = NormaliseYaw(northYawDeg),
                fingerprint = fingerprint,
                firstSeenUnixMs = now,
                lastSeenUnixMs = now,
                observationCount = 1,
            };
            _document.nodes.Add(node);
            Touch();
            return node;
        }

        private void AddEdge(string from, string to)
        {
            if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to) ||
                from == to)
                return;
            foreach (MapEdge edge in _document.edges)
                if ((edge.fromNodeId == from && edge.toNodeId == to) ||
                    (edge.fromNodeId == to && edge.toNodeId == from))
                    return;
            MapNode a = FindNode(from);
            MapNode b = FindNode(to);
            if (a == null || b == null) return;
            _document.edges.Add(new MapEdge
            {
                fromNodeId = from,
                toNodeId = to,
                distanceM = Vector3.Distance(
                    a.mapPosition.Value, b.mapPosition.Value),
            });
            Touch();
        }

        private MapNode BestFingerprintMatch(
            RadioFingerprint fingerprint,
            out float quality)
        {
            quality = 0f;
            if (fingerprint == null) return null;
            MapNode selected = null;
            foreach (MapNode node in _document.nodes)
            {
                float candidate = FingerprintSimilarity(
                    fingerprint, node?.fingerprint);
                if (candidate > quality)
                {
                    quality = candidate;
                    selected = node;
                }
            }
            return quality >= 0.62f ? selected : null;
        }

        public static float FingerprintSimilarity(
            RadioFingerprint a,
            RadioFingerprint b)
        {
            if (a == null || b == null) return 0f;
            var left = RadioDictionary(a);
            var right = RadioDictionary(b);
            int common = 0;
            float rssiScore = 0f;
            foreach (KeyValuePair<string, int> item in left)
            {
                if (!right.TryGetValue(item.Key, out int other)) continue;
                common++;
                rssiScore += 1f - Mathf.Clamp01(
                    Mathf.Abs(item.Value - other) / 45f);
            }
            if (common < 3) return 0f;
            int union = left.Count + right.Count - common;
            float overlap = union > 0 ? common / (float)union : 0f;
            float radio = rssiScore / common;
            float magnetic = 0.5f;
            if (a.magnetic != null && b.magnetic != null &&
                a.magnetic.magnitude_ut > 0f && b.magnetic.magnitude_ut > 0f)
                magnetic = 1f - Mathf.Clamp01(
                    Mathf.Abs(
                        a.magnetic.magnitude_ut - b.magnetic.magnitude_ut) / 30f);
            return Mathf.Clamp01(overlap * 0.48f + radio * 0.42f + magnetic * 0.10f);
        }

        private static Dictionary<string, int> RadioDictionary(
            RadioFingerprint fingerprint)
        {
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            if (fingerprint?.wifi != null)
                foreach (RadioReading row in fingerprint.wifi)
                    if (row != null && !string.IsNullOrWhiteSpace(row.id))
                        result["w:" + row.id] = row.rssi;
            if (fingerprint?.ble != null)
                foreach (RadioReading row in fingerprint.ble)
                    if (row != null && !string.IsNullOrWhiteSpace(row.id))
                        result["b:" + row.id] = row.rssi;
            return result;
        }

        private void TouchNode(MapNode node, RadioFingerprint fingerprint)
        {
            node.lastSeenUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            node.observationCount = Mathf.Max(1, node.observationCount + 1);
            if (fingerprint != null) node.fingerprint = fingerprint;
            Touch();
        }

        private Vector3 LocalToMap(Vector3 local) =>
            _sessionOriginMap +
            Quaternion.Euler(0f, -_sessionNorthYaw, 0f) *
            (local - _sessionOriginLocal);

        private Vector3 MapToLocal(Vector3 map) =>
            _sessionOriginLocal +
            Quaternion.Euler(0f, _sessionNorthYaw, 0f) *
            (map - _sessionOriginMap);

        private MapNode FindDestination(string destination)
        {
            string query = CleanLabel(destination);
            if (string.IsNullOrEmpty(query)) return null;
            MapNode exact = _document.nodes.Find(node =>
                node != null &&
                string.Equals(node.label, query, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;
            return _document.nodes.Find(node =>
                node != null &&
                !string.IsNullOrWhiteSpace(node.label) &&
                (node.label.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                 query.IndexOf(node.label, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        private MapNode FindNode(string id) =>
            _document.nodes.Find(node =>
                node != null &&
                string.Equals(node.nodeId, id, StringComparison.Ordinal));

        private RadioFingerprint ParseFingerprint(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                RadioFingerprint parsed = JsonUtility.FromJson<RadioFingerprint>(json);
                if (parsed == null || parsed.schema_version != 1) return null;
                parsed.wifi = parsed.wifi ?? new List<RadioReading>();
                parsed.ble = parsed.ble ?? new List<RadioReading>();
                parsed.magnetic = parsed.magnetic ?? new MagneticReading();
                parsed.wifi.RemoveAll(InvalidReading);
                parsed.ble.RemoveAll(InvalidReading);
                if (parsed.wifi.Count > 16) parsed.wifi.RemoveRange(16, parsed.wifi.Count - 16);
                if (parsed.ble.Count > 16) parsed.ble.RemoveRange(16, parsed.ble.Count - 16);
                return parsed;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool InvalidReading(RadioReading row) =>
            row == null || string.IsNullOrWhiteSpace(row.id) ||
            row.rssi < -127 || row.rssi > 0;

        private MapDocument Load()
        {
            try
            {
                if (File.Exists(_path))
                {
                    MapDocument loaded = JsonUtility.FromJson<MapDocument>(
                        File.ReadAllText(_path));
                    if (loaded != null &&
                        loaded.schemaVersion == CurrentSchemaVersion &&
                        !string.IsNullOrWhiteSpace(loaded.mapId))
                    {
                        loaded.nodes = loaded.nodes ?? new List<MapNode>();
                        loaded.edges = loaded.edges ?? new List<MapEdge>();
                        loaded.nodes.RemoveAll(node =>
                            node == null || string.IsNullOrWhiteSpace(node.nodeId));
                        loaded.edges.RemoveAll(edge =>
                            edge == null ||
                            string.IsNullOrWhiteSpace(edge.fromNodeId) ||
                            string.IsNullOrWhiteSpace(edge.toNodeId));
                        return loaded;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    "[IndoorLiveMapStore] corrupt map ignored: " +
                    ex.GetType().Name);
            }
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return new MapDocument
            {
                schemaVersion = CurrentSchemaVersion,
                mapId = "indoormap-" + Guid.NewGuid().ToString("N"),
                createdAtUnixMs = now,
                updatedAtUnixMs = now,
            };
        }

        private void Save()
        {
            string temp = _path + ".tmp";
            File.WriteAllText(temp, JsonUtility.ToJson(_document, true));
            try
            {
                if (File.Exists(_path)) File.Replace(temp, _path, null);
                else File.Move(temp, _path);
            }
            catch (Exception)
            {
                File.Copy(temp, _path, true);
                File.Delete(temp);
            }
        }

        private void Touch() =>
            _document.updatedAtUnixMs =
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        private static string CleanLabel(string value)
        {
            string clean = string.Join(
                " ",
                (value ?? string.Empty).Split(
                    (char[])null,
                    StringSplitOptions.RemoveEmptyEntries));
            return clean.Length <= 80 ? clean : clean.Substring(0, 80);
        }

        private static float NormaliseYaw(float yaw) =>
            Mathf.Repeat(yaw + 180f, 360f) - 180f;

        private static bool Finite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);

        private static bool Finite(Vector3 value) =>
            Finite(value.x) && Finite(value.y) && Finite(value.z);
    }
}
