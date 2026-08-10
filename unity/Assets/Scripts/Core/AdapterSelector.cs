// MLOmega V19 — E23
// Runtime device-adapter selection. Maps the MLOmegaConfig.Adapter value (which in
// turn mirrors configs/user_profile.yaml display/capture, handoff §3.5) to a
// concrete IXRDeviceAdapter:
//
//   XrAdapterKind.Auto      -> editor: Simulated ; device: Xreal
//   XrAdapterKind.Xreal     -> XrealDeviceAdapter      (display: xreal_one_pro / capture: xreal_eye)
//   XrAdapterKind.PhoneOnly -> PhoneOnlyAdapter        (display: phone_only     / capture: phone_camera)
//   XrAdapterKind.Simulated -> SimulatedDeviceAdapter  (editor / companion_web dev)
using UnityEngine;

namespace MLOmega.XR.Core
{
    public static class AdapterSelector
    {
        /// <summary>
        /// Build the adapter chosen by <paramref name="kind"/>. <c>Auto</c> resolves
        /// to the simulator in the editor and to XREAL in a player build.
        /// </summary>
        public static IXRDeviceAdapter Create(XrAdapterKind kind)
        {
            switch (kind)
            {
                case XrAdapterKind.Xreal:
                    return new XrealDeviceAdapter();
                case XrAdapterKind.PhoneOnly:
                    return new PhoneOnlyAdapter();
                case XrAdapterKind.Simulated:
                    return new SimulatedDeviceAdapter();
                case XrAdapterKind.Auto:
                default:
                    return Application.isEditor
                        ? (IXRDeviceAdapter)new SimulatedDeviceAdapter()
                        : new XrealDeviceAdapter();
            }
        }
    }
}
