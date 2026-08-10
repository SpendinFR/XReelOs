using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using MLOmega.XR.UI.Components;

namespace MLOmega.XR.UI
{
    /// <summary>
    /// Lab-only actions hosted by the proven Atelier control centre. Product
    /// and Atelier do not register these delegates, so this remains a no-op in
    /// their builds.
    /// </summary>
    public sealed partial class WorldCreatorController
    {
        private const string LabVrPreference = "mlomega.xr.lab.vr_mode.v1";
        private Action _labQuitAction;
        private Action _labKeyboardAction;
        private Func<bool> _labKeyboardVisible;
        private Action<bool> _labVrChanged;
        private Action _labRecordAction;
        private Func<bool> _labRecording;
        private Func<bool> _labRecordBusy;
        private Func<float> _labRecordElapsed;
        private Func<string> _labRecordStatus;
        private Action _labDockReorderAction;
        private Func<bool> _labDockReorderActive;
        private Button _labQuitButton;
        private Button _labVrButton;
        private Button _labKeyboardButton;
        private Button _labRecordButton;
        private Button _labLowLightButton;
        private TextMeshProUGUI _labVrLabel;
        private TextMeshProUGUI _labKeyboardLabel;
        private TextMeshProUGUI _labRecordLabel;
        private TextMeshProUGUI _labLowLightLabel;
        private Image _labQuitConfirmPanel;
        private Coroutine _labRecordUiLoop;

        public void RegisterLabSettingsActions(
            Action quit,
            Action toggleKeyboard,
            Func<bool> keyboardVisible,
            Action<bool> vrChanged,
            Action toggleRecording,
            Func<bool> recording,
            Func<bool> recordBusy,
            Func<float> recordElapsed,
            Func<string> recordStatus,
            Action toggleDockReorder,
            Func<bool> dockReorderActive)
        {
            _labQuitAction = quit;
            _labKeyboardAction = toggleKeyboard;
            _labKeyboardVisible = keyboardVisible;
            _labVrChanged = vrChanged;
            _labRecordAction = toggleRecording;
            _labRecording = recording;
            _labRecordBusy = recordBusy;
            _labRecordElapsed = recordElapsed;
            _labRecordStatus = recordStatus;
            _labDockReorderAction = toggleDockReorder;
            _labDockReorderActive = dockReorderActive;
            if (_settingsDeck == null) BuildSettingsDeck();
            if (_settingsDeckRect == null) return;
            BuildOptionalLabSettingsActions();
            Vector2 size = _settingsDeckRect.sizeDelta;
            if (size.x >= size.y)
                size = new Vector2(
                    Mathf.Max(size.x, 1040f),
                    Mathf.Max(size.y, 760f));
            else
                size = new Vector2(
                    Mathf.Max(size.x, 620f),
                    Mathf.Max(size.y, 1060f));
            _settingsDeckRect.sizeDelta = size;
            LayoutSettingsDeck();
            RefreshOptionalLabSettingsActions();
            ResolveInteractionSettings();
            if (_interactionSettings != null)
                SetHeadOnlyModeVisualState(
                    _interactionSettings.IsHeadOnlyModeEnabled,
                    _interactionSettings.IsHeadOnlyInteractionActive,
                    false);
            if (_labRecordUiLoop == null)
                _labRecordUiLoop = StartCoroutine(RefreshLabRecordUi());
            _settingsHitGraphics.Clear();
            _settingsDeckRect.GetComponentsInChildren(true, _settingsHitGraphics);
        }

        public void SaveLabWindowLayoutsForExit()
        {
            SaveVisibleWindowLayouts();
            PlayerPrefs.Save();
        }

        private bool HasOptionalLabSettingsActions() => _labVrButton != null;

        private void BuildOptionalLabSettingsActions()
        {
            if (_labQuitButton != null || _settingsDeckRect == null) return;

            _labQuitButton = MakeVisionControlButton(
                _settingsDeckRect,
                "LAB POWER",
                VisionIconKind.Power,
                string.Empty,
                Vector2.zero,
                ToggleOptionalLabQuitConfirmation,
                56f);
            TextMeshProUGUI quitCaption = CaptionFor(_labQuitButton);
            if (quitCaption != null) quitCaption.gameObject.SetActive(false);

            _labVrButton = MakeVisionControlButton(
                _settingsDeckRect,
                "LAB VR MODE",
                VisionIconKind.Vr,
                "Mode VR",
                Vector2.zero,
                ToggleOptionalLabVr,
                64f);
            _labVrLabel = CaptionFor(_labVrButton);

            _labKeyboardButton = MakeVisionControlButton(
                _settingsDeckRect,
                "LAB KEYBOARD",
                VisionIconKind.Keyboard,
                "Clavier",
                Vector2.zero,
                () =>
                {
                    _labKeyboardAction?.Invoke();
                    RefreshOptionalLabSettingsActions();
                },
                64f);
            _labKeyboardLabel = CaptionFor(_labKeyboardButton);

            _labRecordButton = MakeVisionControlButton(
                _settingsDeckRect,
                "LAB RECORD",
                VisionIconKind.Record,
                "Enregistrer",
                Vector2.zero,
                () =>
                {
                    _labRecordAction?.Invoke();
                    RefreshOptionalLabSettingsActions();
                },
                64f);
            _labRecordLabel = CaptionFor(_labRecordButton);

            _labLowLightButton = MakeVisionControlButton(
                _settingsDeckRect,
                "LAB HAND LOW LIGHT",
                VisionIconKind.Hand,
                "Main nuit",
                Vector2.zero,
                CycleOptionalLabLowLight,
                64f);
            _labLowLightLabel = CaptionFor(_labLowLightButton);

            _labQuitConfirmPanel = MakeImage(
                _settingsDeckRect,
                "Lab quit confirmation glass",
                Vector2.zero,
                new Vector2(250f, 116f),
                new Color(.055f, .060f, .075f, .96f));
            _labQuitConfirmPanel.raycastTarget = false;
            MakeText(
                _labQuitConfirmPanel.transform,
                "Quitter l'application ?",
                new Vector2(0f, 29f),
                new Vector2(220f, 28f),
                15f,
                VisionText,
                FontStyles.Normal);
            Button yes = MakeButton(
                _labQuitConfirmPanel.transform,
                "Oui",
                new Vector2(-58f, -24f),
                new Vector2(98f, 40f),
                ConfirmOptionalLabQuit);
            Button no = MakeButton(
                _labQuitConfirmPanel.transform,
                "Non",
                new Vector2(58f, -24f),
                new Vector2(98f, 40f),
                () => SetOptionalLabQuitConfirmation(false));
            StyleConfirmationButton(yes);
            StyleConfirmationButton(no);
            _labQuitConfirmPanel.gameObject.SetActive(false);
        }

        private static void StyleConfirmationButton(Button button)
        {
            if (button == null) return;
            Image image = button.GetComponent<Image>();
            if (image != null)
            {
                image.sprite = GetVisionRoundedSprite();
                image.type = Image.Type.Sliced;
                image.color = new Color(.20f, .21f, .25f, .92f);
            }
            TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
            {
                label.fontSize = 14f;
                label.fontStyle = FontStyles.Normal;
            }
        }

        private void ToggleOptionalLabQuitConfirmation()
        {
            SetOptionalLabQuitConfirmation(
                _labQuitConfirmPanel == null ||
                !_labQuitConfirmPanel.gameObject.activeSelf);
        }

        private void SetOptionalLabQuitConfirmation(bool visible)
        {
            if (_labQuitConfirmPanel == null) return;
            _labQuitConfirmPanel.gameObject.SetActive(visible);
            if (visible) _labQuitConfirmPanel.transform.SetAsLastSibling();
        }

        private void ConfirmOptionalLabQuit()
        {
            SetOptionalLabQuitConfirmation(false);
            _labQuitAction?.Invoke();
        }

        private void ToggleOptionalLabVr()
        {
            bool enabled = PlayerPrefs.GetInt(LabVrPreference, 0) != 1;
            PlayerPrefs.SetInt(LabVrPreference, enabled ? 1 : 0);
            PlayerPrefs.Save();
            _labVrChanged?.Invoke(enabled);
            RefreshOptionalLabSettingsActions();
            ShowGestureToast(
                enabled ? "MODE VR PRET" : "MODE VR COUPE",
                enabled ? new Color(.55f, .78f, 1f) : VisionSecondary);
        }

        public void ToggleLabKeyboardFromGesture()
        {
            if (_labKeyboardAction == null) return;
            _labKeyboardAction.Invoke();
            RefreshOptionalLabSettingsActions();
            ShowGestureToast(
                _labKeyboardVisible?.Invoke() == true
                    ? "CLAVIER // DEUX DOIGTS"
                    : "CLAVIER FERME",
                VisionPressed);
        }

        private void ToggleLabDockReorder()
        {
            _labDockReorderAction?.Invoke();
            RefreshQuickMenuTelemetry();
        }

        private bool IsLabDockReorderActive() =>
            _labDockReorderActive?.Invoke() == true;

        private void CycleOptionalLabLowLight()
        {
            ResolveInteractionSettings();
            _interactionSettings?.CycleHandLowLightMode();
            RefreshOptionalLabSettingsActions();
            string mode = _interactionSettings?.CurrentHandLowLightMode switch
            {
                HandLowLightMode.Light => "LEGER",
                HandLowLightMode.Strong => "RENFORCE",
                _ => "DESACTIVE",
            };
            ShowGestureToast("MAIN BASSE LUMIERE // " + mode, VisionPressed);
        }

#if false // Bluetooth pairing is intentionally owned by Android Settings on the S24.
        private void ToggleOptionalLabBluetoothPanel()
        {
            if (_labBluetoothPanel == null) return;
            bool visible = !_labBluetoothPanel.gameObject.activeSelf;
            _labBluetoothPanel.gameObject.SetActive(visible);
            if (visible)
            {
                _labBluetoothPanel.transform.SetAsLastSibling();
                RefreshOptionalLabBluetoothStatus();
            }
        }

        private void RefreshOptionalLabBluetoothStatus()
        {
            _nextLabBluetoothRefreshAt = Time.unscaledTime + 2f;
            string value = _labBluetoothStatus?.Invoke() ??
                "UNAVAILABLE|Bluetooth non configure";
            string[] parts = value.Split('|');
            string state = parts.Length > 0 ? parts[0] : "UNAVAILABLE";
            string device = parts.Length > 1 ? parts[1] : "Aucun appareil";
            int battery = -1;
            string inputs = "Aucune entree externe";
            foreach (string part in parts)
            {
                if (part.StartsWith("battery=", StringComparison.Ordinal) &&
                    int.TryParse(part.Substring(8), out int parsed))
                    battery = parsed;
                else if (part.StartsWith("inputs=", StringComparison.Ordinal))
                    inputs = part.Substring(7);
            }
            bool enabled = state == "ON";
            bool connected = enabled &&
                !device.StartsWith("Aucun", StringComparison.OrdinalIgnoreCase);
            Color color = connected
                ? new Color(.35f, 1f, .72f, .98f)
                : (enabled
                    ? new Color(.42f, .74f, 1f, .98f)
                    : new Color(.58f, .61f, .68f, .96f));
            if (_labBluetoothGaugeLabel != null)
                _labBluetoothGaugeLabel.text = connected
                    ? (battery >= 0 ? battery + "%" : "OK")
                    : (enabled ? "ON" : "OFF");
            if (_labBluetoothRing != null)
            {
                _labBluetoothRing.fillAmount = battery >= 0
                    ? Mathf.Clamp01(battery / 100f)
                    : (enabled ? 1f : 0f);
                _labBluetoothRing.color = color;
            }
            if (_labBluetoothLabel != null)
                _labBluetoothLabel.text = connected ? "Ecouteurs" : "Bluetooth";
            SetControlCenterState(_labBluetoothButton, connected, color);
            if (_labBluetoothDetail != null)
                _labBluetoothDetail.text = connected
                    ? device + (battery >= 0 ? " · " + battery + "%" : "") +
                      "\n" + inputs
                    : device + "\n" + inputs + "\nConnexion geree par le S24";
        }

#endif
        private void RefreshOptionalLabSettingsActions()
        {
            if (_labVrButton == null) return;
            bool vr = PlayerPrefs.GetInt(LabVrPreference, 0) == 1;
            bool keyboard = _labKeyboardVisible?.Invoke() == true;
            if (_labVrLabel != null) _labVrLabel.text = vr ? "VR actif" : "Mode VR";
            if (_labKeyboardLabel != null)
                _labKeyboardLabel.text = keyboard ? "Fermer clavier" : "Clavier";
            SetControlCenterState(_labVrButton, vr, VisionPressed);
            SetControlCenterState(_labKeyboardButton, keyboard, VisionPressed);

            HandLowLightMode lowLight =
                _interactionSettings?.CurrentHandLowLightMode ?? HandLowLightMode.Off;
            if (_labLowLightLabel != null)
                _labLowLightLabel.text = lowLight switch
                {
                    HandLowLightMode.Light => "Main nuit légère",
                    HandLowLightMode.Strong => "Main nuit renforcée",
                    _ => "Main nuit désactivée",
                };
            SetControlCenterState(
                _labLowLightButton,
                lowLight != HandLowLightMode.Off,
                lowLight == HandLowLightMode.Strong
                    ? new Color(.72f, .48f, 1f, .98f)
                    : VisionPressed);

            bool recording = _labRecording?.Invoke() == true;
            bool busy = _labRecordBusy?.Invoke() == true;
            VisionSpatialControlFeedback recordFeedback =
                _labRecordButton?.GetComponent<VisionSpatialControlFeedback>();
            if (recording)
            {
                float pulse = .5f + .5f * Mathf.Sin(Time.unscaledTime * 7.5f);
                Color recordSurface = Color.Lerp(
                    new Color(.32f, .018f, .028f, .90f),
                    new Color(.98f, .055f, .075f, 1f),
                    .28f + pulse * .72f);
                recordFeedback?.SetSelected(true, recordSurface, Color.white);
                if (_labRecordLabel != null)
                {
                    int totalSeconds = Mathf.Max(
                        0,
                        Mathf.FloorToInt(_labRecordElapsed?.Invoke() ?? 0f));
                    _labRecordLabel.text = string.Format(
                        "REC {0:00}:{1:00}",
                        totalSeconds / 60,
                        totalSeconds % 60);
                    _labRecordLabel.color = Color.Lerp(
                        new Color(1f, .32f, .34f, .92f),
                        Color.white,
                        pulse * .35f);
                }
            }
            else if (busy)
            {
                recordFeedback?.SetSelected(
                    true,
                    new Color(.78f, .34f, .06f, .96f),
                    Color.white);
                if (_labRecordLabel != null)
                {
                    _labRecordLabel.text = _labRecordStatus?.Invoke() ?? "Préparation…";
                    _labRecordLabel.color = new Color(1f, .76f, .40f, .96f);
                }
            }
            else
            {
                recordFeedback?.SetSelected(false, VisionPressed, VisionInk);
                if (_labRecordLabel != null)
                {
                    string status = _labRecordStatus?.Invoke();
                    _labRecordLabel.text = string.IsNullOrWhiteSpace(status)
                        ? "Enregistrer"
                        : status;
                    _labRecordLabel.color = VisionSecondary;
                }
            }
        }

        public void RefreshLabSettingsActions() =>
            RefreshOptionalLabSettingsActions();

        private IEnumerator RefreshLabRecordUi()
        {
            var interval = new WaitForSecondsRealtime(.16f);
            while (_labRecordButton != null)
            {
                RefreshOptionalLabSettingsActions();
                yield return interval;
            }
            _labRecordUiLoop = null;
        }

        private Vector2 AdjustOptionalLabSettingsOrientation(Vector2 target)
        {
            if (!HasOptionalLabSettingsActions()) return target;
            return target.x >= target.y
                ? new Vector2(
                    Mathf.Max(target.x, 1040f),
                    Mathf.Max(target.y, 760f))
                : new Vector2(
                    Mathf.Max(target.x, 620f),
                    Mathf.Max(target.y, 1060f));
        }

        private void LayoutOptionalLabSettingsActions(
            float surfaceWidth,
            float surfaceBottom,
            bool compact)
        {
            if (!HasOptionalLabSettingsActions()) return;
            float surfaceTop = _settingsDeckRect.sizeDelta.y * .5f - 48f;
            float surfaceRight = surfaceWidth * .5f;
            LayoutScaledButton(
                _labQuitButton,
                new Vector2(surfaceRight - 27f, surfaceTop - 27f),
                56f,
                .76f);

            float rowScale = compact ? .76f : .88f;
            float step = Mathf.Min(104f, surfaceWidth * .22f);
            float y = surfaceBottom + 55f;
            LayoutScaledButton(_labVrButton, new Vector2(-1.5f * step, y), 64f, rowScale);
            LayoutScaledButton(_labKeyboardButton, new Vector2(-.5f * step, y), 64f, rowScale);
            LayoutScaledButton(_labRecordButton, new Vector2(.5f * step, y), 64f, rowScale);
            LayoutScaledButton(_labLowLightButton, new Vector2(1.5f * step, y), 64f, rowScale);

            // Four compact live diagnostics share the dark header on Lab builds.
            float diagStep = Mathf.Min(156f, surfaceWidth * .22f);
            if (_settingsDevicePill != null)
                _settingsDevicePill.rectTransform.anchoredPosition =
                    new Vector2(-1.5f * diagStep, surfaceTop - 98f);
            if (_settingsLensPill != null)
                _settingsLensPill.rectTransform.anchoredPosition =
                    new Vector2(-.5f * diagStep, surfaceTop - 98f);
            if (_settingsTrackingPill != null)
                _settingsTrackingPill.rectTransform.anchoredPosition =
                    new Vector2(.5f * diagStep, surfaceTop - 98f);
            if (_settingsAudioPill != null)
                _settingsAudioPill.rectTransform.anchoredPosition =
                    new Vector2(1.5f * diagStep, surfaceTop - 98f);

            if (_labQuitConfirmPanel != null)
                LayoutSurface(
                    _labQuitConfirmPanel,
                    new Vector2(surfaceRight - 137f, surfaceTop - 102f),
                    new Vector2(250f, 116f));
        }
    }
}
