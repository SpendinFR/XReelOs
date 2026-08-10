// MLOmega V19 — E23
// Shared default values for the V19 contracts as produced by the Unity client.
// contracts_version tracks packages/contracts (schemas default "v19.0").
namespace MLOmega.XR.Core
{
    public static class ContractDefaults
    {
        /// <summary>Matches the "default": "v19.0" in packages/contracts/schemas/*.</summary>
        public const string Version = "v19.0";

        /// <summary>FrameEnvelope.source values (handoff §3.4 / §3.5 capture kinds).</summary>
        public static class FrameSource
        {
            public const string XrealEye = "xreal_eye";
            public const string Simulated = "simulated";
            public const string PhoneCamera = "phone_camera";
        }
    }
}
