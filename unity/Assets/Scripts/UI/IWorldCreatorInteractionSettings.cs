namespace MLOmega.XR.UI
{
    public enum HandLowLightMode
    {
        Off = 0,
        Light = 1,
        Strong = 2,
    }

    /// <summary>
    /// Assembly-neutral control surface used by the Atelier settings panel.
    /// The XREAL implementation owns Reflex and pointer details; the UI never
    /// acquires a reverse dependency on those platform assemblies.
    /// </summary>
    public interface IWorldCreatorInteractionSettings
    {
        bool IsGestureStandby { get; }
        bool IsHeadOnlyModeEnabled { get; }
        bool IsHeadOnlyInteractionActive { get; }
        bool IsRayVisible { get; }
        string TrackingStatus { get; }
        string GlassesTemperatureStatus { get; }
        HandLowLightMode CurrentHandLowLightMode { get; }
        void SetGestureStandby(bool standby);
        void ToggleRayVisible();
        void CycleHandLowLightMode();
        void ToggleHeadOnlyMode();
        void EnterHeadOnlyInteractionMode();
        void EnterHeadOnlyPassiveMode();
    }
}
