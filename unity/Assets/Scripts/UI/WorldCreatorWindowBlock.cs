using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MLOmega.XR.UI
{
    /// <summary>
    /// Optional Lab window block and tracking-loss guard. Both features are
    /// dormant until a window is explicitly locked or the XR tracking state is
    /// continuously bad for several seconds. Product/PhoneOnly never register
    /// these Lab windows and therefore keep their established behaviour.
    /// </summary>
    public sealed partial class WorldCreatorController
    {
        private sealed class WindowBlockMember
        {
            public RectTransform Rect;
            public string Prefix;
            public DeckWindowKind Kind;
            public ExternalSpatialWindowState External;
        }

        private sealed class WindowPose
        {
            public WindowBlockMember Member;
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 TargetPosition;
            public Quaternion TargetRotation;
        }

        private readonly List<WindowPose> _blockManipulationPoses =
            new List<WindowPose>();
        private Vector3 _blockManipulationLeaderPosition;
        private Quaternion _blockManipulationLeaderRotation;
        private RectTransform _blockManipulationLeader;
        private bool _blockManipulationActive;
        private bool _blockSavePending;

        private readonly List<WindowPose> _trackingFallbackPoses =
            new List<WindowPose>();
        private readonly List<WindowPose> _cinemaTransitionPoses =
            new List<WindowPose>();
        private bool _cinemaTransitionSnapshotValid;
        private bool _rawSpatialTrackingReliable = true;
        private string _rawSpatialTrackingReason = "OK";
        private float _spatialLossSince = -1f;
        private float _spatialGoodSince = -1f;
        private bool _spatialFallbackActive;
        private bool _spatialRecoveryActive;
        private float _spatialRecoveryStartedAt = -1f;
        private Vector3 _fallbackLastCameraPosition;
        private Quaternion _fallbackLastCameraRotation;

        private const float SpatialFallbackGraceSeconds = 4f;
        private const float SpatialRecoveryStableSeconds = 1.1f;
        private const float SpatialRecoveryBlendSeconds = .85f;
        private const float BlockPortraitLongEdgeMetres = .72f;
        private const float BlockLandscapeLongEdgeMetres = .78f;
        private const float BlockUltraWideLongEdgeMetres = .96f;
        private const string AutoJoinWindowBlockPreference =
            "mlomega.xr.lab.auto_join_block.v1";
        private bool _autoJoinWindowBlock;

        private void InitializeWindowBlockState(
            DeckWindowKind kind,
            ExternalSpatialWindowState external)
        {
            string prefix = BlockPrefix(kind, external);
            bool locked = PlayerPrefs.GetInt(prefix + "block_locked", 0) == 1;
            SetWindowBlockSlot(
                kind,
                external,
                PlayerPrefs.GetInt(prefix + "block_slot", 0));
            SetWindowBlockLocked(kind, external, locked, false);
        }

        private void ToggleWindowBlock(
            DeckWindowKind kind,
            ExternalSpatialWindowState external)
        {
            bool next = !IsWindowBlockLocked(kind, external);
            if (next)
            {
                JoinWindowBlock(kind, external, true);
                return;
            }
            SetWindowBlockLocked(kind, external, next, true);
            if (!next)
            {
                ShowGestureToast(
                    "FENETRE LIBRE",
                    new Color(.72f, .78f, .88f));
                return;
            }

        }

        private void ToggleAutoJoinWindowBlock()
        {
            _autoJoinWindowBlock = !_autoJoinWindowBlock;
            PlayerPrefs.SetInt(AutoJoinWindowBlockPreference,
                _autoJoinWindowBlock ? 1 : 0);
            PlayerPrefs.Save();
            if (_autoJoinWindowBlock)
            {
                if (_activeExternalWindow != null &&
                    IsExternalWindowVisible(_activeExternalWindow))
                    JoinWindowBlock(
                        DeckWindowKind.External, _activeExternalWindow, false);
                else if (_settingsDeck != null &&
                         _settingsDeck.gameObject.activeSelf)
                    JoinWindowBlock(DeckWindowKind.Settings, null, false);
                else if (!_deckMinimized)
                    JoinWindowBlock(DeckWindowKind.Workspace, null, false);
            }
            ShowGestureToast(
                _autoJoinWindowBlock ? "Nouvelles fenêtres liées" :
                    "Nouvelles fenêtres libres",
                _autoJoinWindowBlock ? new Color(.35f, 1f, .94f) :
                    new Color(.72f, .78f, .88f));
            RefreshQuickMenuTelemetry();
        }

        private void JoinWindowBlock(
            DeckWindowKind kind,
            ExternalSpatialWindowState external,
            bool notify)
        {
            WindowBlockMember joined = GetWindowBlockMember(kind, external);
            if (joined?.Rect == null) return;
            if (!IsWindowBlockLocked(kind, external))
            {
                List<WindowBlockMember> existing = VisibleWindowBlockMembers(true);
                int slot = 0;
                for (int i = 0; i < existing.Count; i++)
                    slot = Mathf.Max(slot,
                        GetWindowBlockSlot(existing[i].Kind,
                            existing[i].External) + 1);
                SetWindowBlockSlot(kind, external, slot);
                SetWindowBlockLocked(kind, external, true, true);
            }

            NormalizeWindowBlockFootprint(joined);
            WindowBlockMember leader = FirstVisibleLockedMember(joined.Rect);
            if (leader?.Rect != null)
                ArrangeWindowBlockAutomatically();
            else
            {
                SaveWindowBlockMember(joined);
                PlayerPrefs.Save();
            }
            if (notify)
                ShowGestureToast(
                    leader == null ? "Bloc prêt" : "Fenêtre liée au bloc",
                    new Color(.35f, 1f, .94f));
        }

        private void SetWindowBlockLocked(
            DeckWindowKind kind,
            ExternalSpatialWindowState external,
            bool locked,
            bool persist)
        {
            Button button = null;
            switch (kind)
            {
                case DeckWindowKind.Workspace:
                    _workspaceBlockLocked = locked;
                    button = _deckBlockButton;
                    break;
                case DeckWindowKind.Settings:
                    _settingsBlockLocked = locked;
                    button = _settingsBlockButton;
                    break;
                case DeckWindowKind.External:
                    if (external != null)
                    {
                        external.BlockLocked = locked;
                        button = external.Block;
                    }
                    break;
            }
            SetControlCenterState(
                button,
                locked,
                new Color(.35f, .94f, 1f, .98f));
            if (!persist) return;
            PlayerPrefs.SetInt(
                BlockPrefix(kind, external) + "block_locked",
                locked ? 1 : 0);
            PlayerPrefs.Save();
        }

        private bool _workspaceBlockLocked;
        private bool _settingsBlockLocked;
        private int _workspaceBlockSlot;
        private int _settingsBlockSlot;

        private void SetWindowBlockSlot(
            DeckWindowKind kind,
            ExternalSpatialWindowState external,
            int slot)
        {
            slot = Mathf.Max(0, slot);
            switch (kind)
            {
                case DeckWindowKind.Workspace:
                    _workspaceBlockSlot = slot;
                    break;
                case DeckWindowKind.Settings:
                    _settingsBlockSlot = slot;
                    break;
                case DeckWindowKind.External when external != null:
                    external.BlockSlot = slot;
                    break;
            }
            PlayerPrefs.SetInt(BlockPrefix(kind, external) + "block_slot", slot);
        }

        private int GetWindowBlockSlot(
            DeckWindowKind kind,
            ExternalSpatialWindowState external)
        {
            return kind switch
            {
                DeckWindowKind.Workspace => _workspaceBlockSlot,
                DeckWindowKind.Settings => _settingsBlockSlot,
                DeckWindowKind.External when external != null => external.BlockSlot,
                _ => 0,
            };
        }

        private void ArrangeWindowBlockAutomatically()
        {
            List<WindowBlockMember> members = VisibleWindowBlockMembers(true);
            if (members.Count < 2) return;
            // A Meta-style block is a deterministic surface, not a snapshot of
            // whatever arbitrary scale/angle each free window had beforehand.
            // Preserve each window's selected aspect (portrait/landscape), but
            // compact the live members and restore a standard physical footprint.
            members.Sort((left, right) =>
                GetWindowBlockSlot(left.Kind, left.External).CompareTo(
                    GetWindowBlockSlot(right.Kind, right.External)));
            for (int i = 0; i < members.Count; i++)
            {
                SetWindowBlockSlot(members[i].Kind, members[i].External, i);
                NormalizeWindowBlockFootprint(members[i]);
            }
            WindowBlockMember leader = members[0];

            float maxWidth = .48f;
            float maxHeight = .32f;
            for (int i = 0; i < members.Count; i++)
            {
                RectTransform rect = members[i].Rect;
                maxWidth = Mathf.Max(
                    maxWidth,
                    rect.rect.width * Mathf.Abs(rect.lossyScale.x));
                maxHeight = Mathf.Max(
                    maxHeight,
                    rect.rect.height * Mathf.Abs(rect.lossyScale.y));
            }
            float stepX = Mathf.Clamp(maxWidth + .075f, .55f, 1.25f);
            float stepY = Mathf.Clamp(maxHeight + .07f, .40f, 1.05f);
            // Rebuild a clean upright surface around the first window. This
            // deliberately removes any accidental individual tilt accumulated
            // before locking, while subsequent group manipulations stay rigid.
            Vector3 centreDirection = leader.Rect.position - _camera.transform.position;
            Quaternion baseRotation = BuildWindowRotation(
                centreDirection.normalized,
                0f,
                0f);
            Vector3 basePosition = leader.Rect.position;

            for (int i = 0; i < members.Count; i++)
            {
                WindowBlockMember member = members[i];
                int slot = GetWindowBlockSlot(member.Kind, member.External);
                BlockSlotPose(slot, stepX, stepY,
                    out Vector3 local, out float yaw);
                member.Rect.position = basePosition + baseRotation * local;
                member.Rect.rotation = baseRotation * Quaternion.Euler(0f, yaw, 0f);
                SaveWindowBlockMember(member);
            }
            PlayerPrefs.Save();
        }

        private static void NormalizeWindowBlockFootprint(WindowBlockMember member)
        {
            RectTransform rect = member?.Rect;
            if (rect == null) return;
            float width = Mathf.Max(1f, Mathf.Abs(rect.rect.width));
            float height = Mathf.Max(1f, Mathf.Abs(rect.rect.height));
            float ratio = width / height;
            float targetLongEdge = ratio < 1f
                ? BlockPortraitLongEdgeMetres
                : (ratio > 2f
                    ? BlockUltraWideLongEdgeMetres
                    : BlockLandscapeLongEdgeMetres);
            float authoredLongEdge = Mathf.Max(width, height);
            float scale = Mathf.Clamp(
                targetLongEdge / authoredLongEdge,
                .00038f,
                .00108f);
            rect.localScale = Vector3.one * scale;
        }

        private static void BlockSlotPose(
            int slot,
            float stepX,
            float stepY,
            out Vector3 local,
            out float yaw)
        {
            // Every tier is a complete curved Meta-style triplet: centre,
            // left, right. A fourth window starts a second centred tier instead
            // of being refused or pushed to an uncontrolled fourth column.
            int row = Mathf.Max(0, slot) / 3;
            int inRow = Mathf.Max(0, slot) % 3;
            int column = inRow == 0 ? 0 : (inRow == 1 ? -1 : 1);
            // The panels' outward normals follow the camera-to-window direction.
            // Therefore the left member needs a negative yaw and the right a
            // positive one to converge on the wearer (the previous signs opened
            // the block away from them).
            yaw = column < 0 ? -43f : (column > 0 ? 43f : 0f);
            // A planar offset makes lateral panels perceptually farther away
            // because of their large X travel. Pull them forward proportionally
            // so their optical distance matches the centre panel.
            float sideForward = Mathf.Clamp(stepX * .43f, .24f, .42f);
            local = new Vector3(
                column * stepX,
                row * stepY,
                -Mathf.Abs(column) * sideForward);
        }

        private bool IsWindowBlockLocked(
            DeckWindowKind kind,
            ExternalSpatialWindowState external)
        {
            return kind switch
            {
                DeckWindowKind.Workspace => _workspaceBlockLocked,
                DeckWindowKind.Settings => _settingsBlockLocked,
                DeckWindowKind.External => external != null && external.BlockLocked,
                _ => false,
            };
        }

        private string BlockPrefix(
            DeckWindowKind kind,
            ExternalSpatialWindowState external)
        {
            return kind switch
            {
                DeckWindowKind.Workspace => DeckLayoutPrefix,
                DeckWindowKind.Settings => SettingsLayoutPrefix,
                DeckWindowKind.External when external != null => external.LayoutPrefix,
                _ => "mlomega.atelier.window_block.unknown.",
            };
        }

        private WindowBlockMember GetWindowBlockMember(
            DeckWindowKind kind,
            ExternalSpatialWindowState external)
        {
            RectTransform rect = kind switch
            {
                DeckWindowKind.Workspace => _spatialDeckRect,
                DeckWindowKind.Settings => _settingsDeckRect,
                DeckWindowKind.External => external?.Rect,
                _ => null,
            };
            if (rect == null) return null;
            return new WindowBlockMember
            {
                Rect = rect,
                Prefix = BlockPrefix(kind, external),
                Kind = kind,
                External = external,
            };
        }

        private List<WindowBlockMember> VisibleWindowBlockMembers(bool lockedOnly)
        {
            var members = new List<WindowBlockMember>();
            if (!_deckMinimized && _spatialDeckRect != null &&
                (!lockedOnly || _workspaceBlockLocked))
                members.Add(GetWindowBlockMember(DeckWindowKind.Workspace, null));
            if (_settingsDeckRect != null && _settingsDeck != null &&
                _settingsDeck.gameObject.activeSelf &&
                (!lockedOnly || _settingsBlockLocked))
                members.Add(GetWindowBlockMember(DeckWindowKind.Settings, null));
            for (int i = 0; i < _externalSpatialWindows.Count; i++)
            {
                ExternalSpatialWindowState external = _externalSpatialWindows[i];
                if (!IsExternalWindowVisible(external) ||
                    (lockedOnly && !external.BlockLocked)) continue;
                members.Add(GetWindowBlockMember(DeckWindowKind.External, external));
            }
            return members;
        }

        private WindowBlockMember FirstVisibleLockedMember(RectTransform exclude)
        {
            List<WindowBlockMember> members = VisibleWindowBlockMembers(true);
            for (int i = 0; i < members.Count; i++)
                if (members[i]?.Rect != null && members[i].Rect != exclude)
                    return members[i];
            return null;
        }

        private void BeginWindowBlockManipulation(
            DeckWindowKind kind,
            ExternalSpatialWindowState external,
            DeckManipulationMode mode)
        {
            _blockManipulationActive = false;
            _blockSavePending = false;
            _blockManipulationPoses.Clear();
            if ((mode != DeckManipulationMode.Move &&
                 mode != DeckManipulationMode.Depth &&
                 mode != DeckManipulationMode.Tilt) ||
                !IsWindowBlockLocked(kind, external)) return;

            WindowBlockMember leader = GetWindowBlockMember(kind, external);
            if (leader?.Rect == null) return;
            List<WindowBlockMember> members = VisibleWindowBlockMembers(true);
            if (members.Count < 2) return;
            _blockManipulationLeader = leader.Rect;
            _blockManipulationLeaderPosition = leader.Rect.position;
            _blockManipulationLeaderRotation = leader.Rect.rotation;
            for (int i = 0; i < members.Count; i++)
            {
                WindowBlockMember member = members[i];
                _blockManipulationPoses.Add(new WindowPose
                {
                    Member = member,
                    Position = member.Rect.position,
                    Rotation = member.Rect.rotation,
                });
            }
            _blockManipulationActive = true;
        }

        private void ApplyWindowBlockManipulation()
        {
            if (!_blockManipulationActive || _blockManipulationLeader == null) return;
            Quaternion deltaRotation =
                _blockManipulationLeader.rotation *
                Quaternion.Inverse(_blockManipulationLeaderRotation);
            Vector3 leaderPosition = _blockManipulationLeader.position;
            for (int i = 0; i < _blockManipulationPoses.Count; i++)
            {
                WindowPose pose = _blockManipulationPoses[i];
                RectTransform rect = pose.Member?.Rect;
                if (rect == null || rect == _blockManipulationLeader) continue;
                rect.position = leaderPosition + deltaRotation *
                    (pose.Position - _blockManipulationLeaderPosition);
                rect.rotation = deltaRotation * pose.Rotation;
            }
            if (_deckManipulationMode == DeckManipulationMode.None)
                _blockSavePending = true;
        }

        private void CompleteWindowBlockManipulation()
        {
            if (!_blockManipulationActive) return;
            if (_blockSavePending)
            {
                for (int i = 0; i < _blockManipulationPoses.Count; i++)
                    SaveWindowBlockMember(_blockManipulationPoses[i].Member);
                PlayerPrefs.Save();
            }
            _blockManipulationPoses.Clear();
            _blockManipulationLeader = null;
            _blockManipulationActive = false;
            _blockSavePending = false;
        }

        private void CommitManualPlacementToTrackingFallback()
        {
            if (!_spatialFallbackActive) return;
            CaptureTrackingFallbackPoses();
            RememberFallbackCameraPose();
            _spatialRecoveryActive = false;
        }

        /// <summary>
        /// The 2D cinema transition restarts the XREAL tracking manager. Capture
        /// every visible window in head-relative coordinates before that switch
        /// so a new XR origin cannot scatter the old world-space poses.
        /// </summary>
        public void CaptureCinemaTransitionLayout()
        {
            _cinemaTransitionPoses.Clear();
            _cinemaTransitionSnapshotValid = false;
            if (_camera == null) return;
            List<WindowBlockMember> members = VisibleWindowBlockMembers(false);
            for (int i = 0; i < members.Count; i++)
            {
                WindowBlockMember member = members[i];
                if (member?.Rect == null) continue;
                _cinemaTransitionPoses.Add(new WindowPose
                {
                    Member = member,
                    Position = _camera.transform.InverseTransformPoint(
                        member.Rect.position),
                    Rotation = Quaternion.Inverse(_camera.transform.rotation) *
                               member.Rect.rotation,
                });
            }
            _cinemaTransitionSnapshotValid = _cinemaTransitionPoses.Count > 0;
            Debug.Log("[XR-CINEMA-LAYOUT] captured=" +
                      _cinemaTransitionPoses.Count);
        }

        /// <summary>
        /// Rebase the complete group once against the restarted camera origin.
        /// Relative spacing, scale, aspect and orientation remain untouched.
        /// </summary>
        public void RestoreCinemaTransitionLayout()
        {
            if (!_cinemaTransitionSnapshotValid || _camera == null) return;
            int restored = 0;
            for (int i = 0; i < _cinemaTransitionPoses.Count; i++)
            {
                WindowPose pose = _cinemaTransitionPoses[i];
                RectTransform rect = pose.Member?.Rect;
                if (rect == null || !rect.gameObject.activeInHierarchy) continue;
                rect.SetPositionAndRotation(
                    _camera.transform.TransformPoint(pose.Position),
                    _camera.transform.rotation * pose.Rotation);
                SaveWindowBlockMember(pose.Member);
                restored++;
            }
            PlayerPrefs.Save();
            CaptureTrackingFallbackPoses();
            RememberFallbackCameraPose();
            _cinemaTransitionPoses.Clear();
            _cinemaTransitionSnapshotValid = false;
            Debug.Log("[XR-CINEMA-LAYOUT] restored=" + restored);
        }

        private void SaveWindowBlockMember(WindowBlockMember member)
        {
            if (member?.Rect == null || _camera == null) return;
            Vector3 local = _camera.transform.InverseTransformPoint(member.Rect.position);
            PlayerPrefs.SetFloat(member.Prefix + "x", local.x);
            PlayerPrefs.SetFloat(member.Prefix + "y", local.y);
            PlayerPrefs.SetFloat(member.Prefix + "z", Mathf.Clamp(local.z, .45f, 2.8f));
            PlayerPrefs.SetFloat(member.Prefix + "scale",
                member.Kind == DeckWindowKind.External
                    ? Mathf.Clamp(member.Rect.localScale.x, .00015f, .01000f)
                    : Mathf.Clamp(member.Rect.localScale.x, .00038f, .00108f));

            Vector3 direction = member.Rect.position - _camera.transform.position;
            Quaternion upright = BuildWindowRotation(direction.normalized, 0f, 0f);
            Vector3 angles = (Quaternion.Inverse(upright) * member.Rect.rotation).eulerAngles;
            PlayerPrefs.SetFloat(member.Prefix + "tilt", SignedAngle(angles.x));
            PlayerPrefs.SetFloat(member.Prefix + "turn", SignedAngle(angles.y));
            if (member.Kind == DeckWindowKind.Settings)
                SaveSettingsSize(member.Rect.sizeDelta);
            else if (member.Kind == DeckWindowKind.External)
                SaveExternalWindowSize(member.External);
        }

        private static float SignedAngle(float degrees) =>
            degrees > 180f ? degrees - 360f : degrees;

        /// <summary>
        /// Raw XR reliability is reported each rendered frame by the pointer.
        /// It is intentionally separate from the visible warning debounce.
        /// </summary>
        public void ReportSpatialTrackingState(bool reliable, string reason)
        {
            float now = Time.unscaledTime;
            _rawSpatialTrackingReliable = reliable;
            _rawSpatialTrackingReason = string.IsNullOrWhiteSpace(reason) ?
                "INCONNU" : reason;
            if (reliable)
            {
                _spatialLossSince = -1f;
                if (_spatialGoodSince < 0f) _spatialGoodSince = now;
            }
            else
            {
                _spatialGoodSince = -1f;
                if (_spatialLossSince < 0f) _spatialLossSince = now;
            }
        }

        private void UpdateSpatialTrackingFallback()
        {
            if (_camera == null) return;
            // Frozen anchoring is an explicit third settings mode. Tracking
            // warnings stay diagnostic and never force a mode change.
            if (!_manualFrozenWindows)
            {
                CancelSpatialTrackingFallback();
                return;
            }
            if (!_spatialFallbackActive)
            {
                BeginSpatialTrackingFallback();
                if (!_spatialFallbackActive) return;
            }

            if (_deckManipulationMode != DeckManipulationMode.None)
            {
                // Manual placement always wins, even while the SLAM is degraded.
                CaptureTrackingFallbackPoses();
                RememberFallbackCameraPose();
                return;
            }
            _spatialRecoveryActive = false;
            // Apply after regular window/follow updates. Rewriting the same
            // world pose here used to be indistinguishable from normal 6DoF.
            // LateUpdate instead cancels positional drift while preserving the
            // live IMU rotation, which is the actual degraded-tracking model.
        }

        private void LateUpdate()
        {
            if (!_manualFrozenWindows || !_spatialFallbackActive ||
                _deckManipulationMode != DeckManipulationMode.None)
                return;
            HoldTrackingFallbackPoses(0f);
        }

        private void CancelSpatialTrackingFallback()
        {
            if (!_spatialFallbackActive && !_spatialRecoveryActive &&
                _trackingFallbackPoses.Count == 0) return;
            _trackingFallbackPoses.Clear();
            _spatialFallbackActive = false;
            _spatialRecoveryActive = false;
            _spatialRecoveryStartedAt = -1f;
            _spatialLossSince = -1f;
            _spatialGoodSince = -1f;
            Debug.Log("[XR-WINDOW-FALLBACK] manual freeze disabled");
        }

        private void BeginSpatialTrackingFallback()
        {
            CaptureTrackingFallbackPoses();
            if (_trackingFallbackPoses.Count == 0) return;
            RememberFallbackCameraPose();
            _spatialFallbackActive = true;
            _spatialRecoveryActive = false;
            Debug.Log("[XR-WINDOW-FALLBACK] frozen reason=" +
                      (_manualFrozenWindows ? "MANUAL" : _rawSpatialTrackingReason) +
                      " windows=" +
                      _trackingFallbackPoses.Count);
            ShowGestureToast(
                "ANCRAGE FIGE // " +
                    (_manualFrozenWindows ? "MANUEL" : _rawSpatialTrackingReason),
                new Color(1f, .68f, .32f));
        }

        private void CaptureTrackingFallbackPoses()
        {
            _trackingFallbackPoses.Clear();
            List<WindowBlockMember> members = VisibleWindowBlockMembers(false);
            for (int i = 0; i < members.Count; i++)
            {
                WindowBlockMember member = members[i];
                _trackingFallbackPoses.Add(new WindowPose
                {
                    Member = member,
                    Position = member.Rect.position,
                    Rotation = member.Rect.rotation,
                    TargetPosition = member.Rect.position,
                    TargetRotation = member.Rect.rotation,
                });
            }
        }

        private void RememberFallbackCameraPose()
        {
            _fallbackLastCameraPosition = _camera.transform.position;
            _fallbackLastCameraRotation = _camera.transform.rotation;
        }

        private void BeginSpatialRecovery()
        {
            Quaternion rawRotationDelta = _camera.transform.rotation *
                Quaternion.Inverse(_fallbackLastCameraRotation);
            Quaternion correctionRotation = Quaternion.RotateTowards(
                Quaternion.identity, rawRotationDelta, 20f);
            Vector3 correctionTranslation = Vector3.ClampMagnitude(
                _camera.transform.position - _fallbackLastCameraPosition,
                .45f);
            for (int i = 0; i < _trackingFallbackPoses.Count; i++)
            {
                WindowPose pose = _trackingFallbackPoses[i];
                pose.TargetPosition =
                    _fallbackLastCameraPosition + correctionTranslation +
                    correctionRotation *
                    (pose.Position - _fallbackLastCameraPosition);
                pose.TargetRotation = correctionRotation * pose.Rotation;
            }
            _spatialRecoveryStartedAt = Time.unscaledTime;
            _spatialRecoveryActive = true;
        }

        private void HoldTrackingFallbackPoses(float recoveryProgress)
        {
            float eased = recoveryProgress * recoveryProgress *
                (3f - 2f * recoveryProgress);
            // A manual freeze means exactly that: keep the captured world pose.
            // The former camera-translation compensation made the windows follow
            // the head and could therefore feel less anchored than normal 6DoF.
            for (int i = 0; i < _trackingFallbackPoses.Count; i++)
            {
                WindowPose pose = _trackingFallbackPoses[i];
                if (pose.Member?.Rect == null) continue;
                pose.Member.Rect.position = Vector3.Lerp(
                    pose.Position,
                    pose.TargetPosition,
                    eased);
                pose.Member.Rect.rotation = Quaternion.Slerp(
                    pose.Rotation, pose.TargetRotation, eased);
            }
        }
    }
}
