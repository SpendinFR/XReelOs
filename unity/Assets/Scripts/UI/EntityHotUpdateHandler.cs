// MLOmega V19 â€” E34 Â§5
// EntityHotUpdateHandler: claims `entity_hot_update` DataChannel messages (the PC
// scene adapter's relation-pack prefetch at identification) and folds them into
// SceneCache.entities_hot, so the ContextCard renders from the local cache without
// a round-trip. Mirrors DeviceCommandHandler: subscribes to the transport's raw
// MessageReceived, claims by type, never throws. Lives in the UI assembly (which
// references Transport, Scene and Contracts).
using System;
using MLOmega.XR.Scene;
using MLOmega.XR.Transport;
using MLOmega.Contracts.V19;
using Newtonsoft.Json;
using UnityEngine;

namespace MLOmega.XR.UI
{
    public sealed class EntityHotUpdateHandler : MonoBehaviour
    {
        [SerializeField] private SceneCache _sceneCache;
        [SerializeField] private LiveTransportBridge _transport;

        /// <summary>Raised for every applied prefetch (entity_id, name).</summary>
        public event Action<string, string> HotUpdateApplied;

        private void Awake()
        {
            if (_sceneCache == null) _sceneCache = FindAnyObjectByType<SceneCache>();
            if (_transport == null) _transport = FindAnyObjectByType<LiveTransportBridge>();
        }

        private void OnEnable()
        {
            if (_transport != null) _transport.MessageReceived += OnTransportMessage;
        }

        private void OnDisable()
        {
            if (_transport != null) _transport.MessageReceived -= OnTransportMessage;
        }

        private void OnTransportMessage(string json) => TryHandleRaw(json);

        /// <summary>Parse and apply a raw hot-update message. Claims any of the four
        /// generalised hot messages (E35 Â§4): entity (person or object), spatial
        /// (zone), task. Returns true when it was one (claimed). Never throws.</summary>
        public bool TryHandleRaw(string json)
        {
            if (EntityHotUpdate.IsEntityHotUpdate(json))
            {
                EntityHotUpdate update;
                try { update = ContractJson.Deserialize<EntityHotUpdate>(json); }
                catch (Exception ex) { Debug.LogWarning($"[EntityHotUpdate] bad json: {ex.Message}"); return true; }
                if (update != null) Apply(update);
                return true;
            }
            if (SpatialHotUpdate.IsSpatialHotUpdate(json))
            {
                SpatialHotUpdate update;
                try { update = ContractJson.Deserialize<SpatialHotUpdate>(json); }
                catch (Exception ex) { Debug.LogWarning($"[SpatialHotUpdate] bad json: {ex.Message}"); return true; }
                if (update != null && _sceneCache != null) _sceneCache.SubmitSpatialHotUpdate(update);
                if (update != null) HotUpdateApplied?.Invoke(update.Zone, "spatial");
                return true;
            }
            if (TaskHotUpdate.IsTaskHotUpdate(json))
            {
                TaskHotUpdate update;
                try { update = ContractJson.Deserialize<TaskHotUpdate>(json); }
                catch (Exception ex) { Debug.LogWarning($"[TaskHotUpdate] bad json: {ex.Message}"); return true; }
                if (update != null && _sceneCache != null) _sceneCache.SubmitTaskHotUpdate(update);
                if (update != null) HotUpdateApplied?.Invoke(update.TaskKey, "task");
                return true;
            }
            return false;
        }

        /// <summary>Fold a parsed prefetch into the scene cache. Null-safe.</summary>
        public bool Apply(EntityHotUpdate update)
        {
            if (update == null || string.IsNullOrEmpty(update.EntityId)) return false;
            if (_sceneCache != null) _sceneCache.SubmitEntityHotUpdate(update);
            HotUpdateApplied?.Invoke(update.EntityId, update.Name);
            return true;
        }
    }
}
