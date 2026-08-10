// MLOmega V19 — E33
// Wires the already-emitted GestureBridge events to the UI (§5, and the known gap
// "swipe->hide handler missing on the Unity side"):
//   * OpenPalmMenu  -> MenuPanel.Toggle()            (open the action menu);
//   * SwipeHide     -> device_command set_ui_mode hide_all via DeviceCommandHandler
//                      (the SAME command the voice "cache tout" emits — one path).
//   * Pinch*        -> while the menu is open, a pinch commits the hovered item.
//
// Lives in the Reflex assembly, which already references UI, so it can see both the
// GestureBridge (Reflex) and MenuPanel/DeviceCommandHandler (UI). The palm/swipe
// gestures themselves are emitted by the native Kotlin pipeline (GestureCallbacks)
// or the editor simulator — this only connects them.
using MLOmega.XR.UI;
using MLOmega.XR.UI.Components;
using UnityEngine;

namespace MLOmega.XR.Reflex
{
    public sealed class MenuGestureController : MonoBehaviour
    {
        [SerializeField] private GestureBridge _gestures;
        [SerializeField] private MenuPanel _menu;
        [SerializeField] private DeviceCommandHandler _commandHandler;

        private void Awake()
        {
            if (_gestures == null) _gestures = FindAnyObjectByType<GestureBridge>();
            if (_menu == null) _menu = FindAnyObjectByType<MenuPanel>();
            if (_commandHandler == null) _commandHandler = FindAnyObjectByType<DeviceCommandHandler>();
        }

        private void OnEnable()
        {
            if (_gestures != null) _gestures.GestureRecognized += OnGesture;
            // The PC or a "menu" voice command can also open the panel.
            if (_commandHandler != null) _commandHandler.MenuRequested += OnMenuRequested;
        }

        private void OnDisable()
        {
            if (_gestures != null) _gestures.GestureRecognized -= OnGesture;
            if (_commandHandler != null) _commandHandler.MenuRequested -= OnMenuRequested;
        }

        private void OnMenuRequested()
        {
            if (_menu != null) _menu.Open();
        }

        /// <summary>Handle one recognised gesture. Public so EditMode tests can drive it.</summary>
        public void OnGesture(GestureEvent ev)
        {
            switch (ev.Kind)
            {
                case GestureKind.OpenPalmMenu:
                    if (_menu != null) _menu.Toggle();
                    break;

                case GestureKind.SwipeHide:
                    // Câble le balayage -> cacher l'UI (le handler Unity manquait).
                    // Routed as the shared device command so voice and gesture agree.
                    if (_commandHandler != null)
                    {
                        _commandHandler.Execute(new DeviceCommand
                        {
                            Type = "device_command",
                            Action = "set_ui_mode",
                            UiMode = "hide_all",
                        });
                    }
                    break;

                case GestureKind.PinchEnd:
                    // A pinch commits the hovered menu item while the menu is open.
                    if (_menu != null && _menu.IsOpen)
                    {
                        _menu.HoverAtViewport(ev.ScreenPoint);
                        _menu.PinchCommit();
                    }
                    break;

                case GestureKind.PinchBegin:
                case GestureKind.PinchUpdate:
                    if (_menu != null && _menu.IsOpen) _menu.HoverAtViewport(ev.ScreenPoint);
                    break;
            }
        }
    }
}
