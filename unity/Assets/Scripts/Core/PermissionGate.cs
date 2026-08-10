// MLOmega V19 — E22 / Gate G1
// Runtime Android permission requests for the Eye capture path. Per the XREAL
// "Access RGB Camera" docs, RECORD_AUDIO and FOREGROUND_SERVICE_MEDIA_PROJECTION
// are required; CAMERA is required by Android convention. In the editor there is
// no Android permission model, so all are reported Granted so dev flows proceed.
using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace MLOmega.XR.Core
{
    public enum PermissionStatus
    {
        Unknown = 0,
        Requested = 1,
        Granted = 2,
        Denied = 3
    }

    /// <summary>
    /// Requests the permissions the Eye capture needs and tracks each one's
    /// state for the overlay. Non-blocking: requests are fired once, results are
    /// polled/observed via callbacks.
    /// </summary>
    public sealed class PermissionGate : MonoBehaviour
    {
        // Exact Android permission strings.
        private const string PermCamera = "android.permission.CAMERA";
        private const string PermRecordAudio = "android.permission.RECORD_AUDIO";
        private const string PermForegroundMediaProjection =
            "android.permission.FOREGROUND_SERVICE_MEDIA_PROJECTION";

        private static readonly string[] RequiredPermissions =
        {
            PermCamera,
            PermRecordAudio,
            PermForegroundMediaProjection
        };

        private readonly Dictionary<string, PermissionStatus> _status =
            new Dictionary<string, PermissionStatus>();

        public event Action AllGranted;

        public bool AllPermissionsGranted { get; private set; }

        private void Awake()
        {
            foreach (string p in RequiredPermissions)
            {
                _status[p] = PermissionStatus.Unknown;
            }
        }

        private void Start()
        {
            RequestAll();
        }

        public void RequestAll()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            foreach (string perm in RequiredPermissions)
            {
                if (Permission.HasUserAuthorizedPermission(perm))
                {
                    _status[perm] = PermissionStatus.Granted;
                    continue;
                }

                _status[perm] = PermissionStatus.Requested;
                var callbacks = new PermissionCallbacks();
                string captured = perm;
                callbacks.PermissionGranted += _ => OnResult(captured, true);
                callbacks.PermissionDenied += _ => OnResult(captured, false);
                callbacks.PermissionDeniedAndDontAskAgain += _ => OnResult(captured, false);
                Permission.RequestUserPermission(perm, callbacks);
            }
#else
            // Editor / non-Android: no permission model. Treat as granted so the
            // simulated dev flow proceeds.
            foreach (string perm in RequiredPermissions)
            {
                _status[perm] = PermissionStatus.Granted;
            }
#endif
            Reevaluate();
        }

        private void OnResult(string perm, bool granted)
        {
            _status[perm] = granted ? PermissionStatus.Granted : PermissionStatus.Denied;
            if (!granted)
            {
                Debug.LogWarning($"[PermissionGate] Permission denied: {perm}. " +
                                 "Eye capture may be unavailable (see README plan B).");
            }
            Reevaluate();
        }

        private void Reevaluate()
        {
            bool all = true;
            foreach (string perm in RequiredPermissions)
            {
                if (_status[perm] != PermissionStatus.Granted)
                {
                    all = false;
                    break;
                }
            }
            bool wasAll = AllPermissionsGranted;
            AllPermissionsGranted = all;
            if (all && !wasAll)
            {
                AllGranted?.Invoke();
            }
        }

        public PermissionStatus StatusOf(string permission) =>
            _status.TryGetValue(permission, out PermissionStatus s) ? s : PermissionStatus.Unknown;

        /// <summary>Compact per-permission readout for the overlay.</summary>
        public string Format()
        {
            return $"cam:{Short(StatusOf(PermCamera))}  " +
                   $"mic:{Short(StatusOf(PermRecordAudio))}  " +
                   $"proj:{Short(StatusOf(PermForegroundMediaProjection))}";
        }

        private static string Short(PermissionStatus s) => s switch
        {
            PermissionStatus.Granted => "OK",
            PermissionStatus.Denied => "KO",
            PermissionStatus.Requested => "...",
            _ => "?"
        };
    }
}
