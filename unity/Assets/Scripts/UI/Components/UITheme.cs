// MLOmega V19 — E25
// Liquid-glass design tokens: palette, glass parameters and short animation
// timings shared by every UI component. A ScriptableObject so the whole look can
// be retuned without recompiling (menu "MLOmega/Config/UI Theme"). Animations are
// capped at <=200 ms per the spec (fade/scale).
using UnityEngine;

namespace MLOmega.XR.UI.Components
{
    [CreateAssetMenu(fileName = "UITheme", menuName = "MLOmega/Config/UI Theme", order = 2)]
    public sealed class UITheme : ScriptableObject
    {
        [Header("Glass palette")]
        [Tooltip("Base tint of the translucent panel body (alpha = base opacity).")]
        [SerializeField] private Color _panelTint = new Color(0.10f, 0.13f, 0.20f, 0.42f);

        [Tooltip("Soft luminous rim colour around each panel.")]
        [SerializeField] private Color _rimColor = new Color(0.55f, 0.80f, 1.00f, 0.85f);

        [Tooltip("Primary text colour on glass.")]
        [SerializeField] private Color _textColor = new Color(0.92f, 0.97f, 1.00f, 1f);

        [Tooltip("Muted / secondary text (age, 'probable', hypothesis label).")]
        [SerializeField] private Color _mutedTextColor = new Color(0.70f, 0.80f, 0.90f, 0.85f);

        [Header("Truth-level accents (§17.2)")]
        [SerializeField] private Color _observedAccent = new Color(0.45f, 0.95f, 0.65f, 1f);
        [SerializeField] private Color _probableAccent = new Color(0.98f, 0.85f, 0.45f, 1f);
        [SerializeField] private Color _rememberedAccent = new Color(0.65f, 0.75f, 0.95f, 1f);
        [SerializeField] private Color _inferredAccent = new Color(0.85f, 0.65f, 0.98f, 1f);

        [Header("Glass parameters (drive LiquidGlass.shader)")]
        [Range(0f, 1f)]
        [Tooltip("Background blur strength (Kawase passes scale). 0 = no blur, translucent-only fallback.")]
        [SerializeField] private float _blurStrength = 0.6f;

        [Range(0f, 1f)]
        [Tooltip("Film grain amount over the glass.")]
        [SerializeField] private float _grain = 0.06f;

        [Range(0f, 4f)]
        [Tooltip("Rim glow width in shader units.")]
        [SerializeField] private float _rimWidth = 1.4f;

        [Range(1f, 24f)]
        [SerializeField] private float _cornerRadius = 12f;

        [Header("Animation (<=200 ms, spec)")]
        [Range(0f, 0.2f)]
        [SerializeField] private float _fadeInSeconds = 0.16f;
        [Range(0f, 0.2f)]
        [SerializeField] private float _fadeOutSeconds = 0.18f;
        [Range(0f, 0.2f)]
        [SerializeField] private float _scaleInSeconds = 0.14f;
        [Range(0.5f, 1f)]
        [SerializeField] private float _scaleFrom = 0.92f;

        public Color PanelTint => _panelTint;
        public Color RimColor => _rimColor;
        public Color TextColor => _textColor;
        public Color MutedTextColor => _mutedTextColor;
        public float BlurStrength => _blurStrength;
        public float Grain => _grain;
        public float RimWidth => _rimWidth;
        public float CornerRadius => _cornerRadius;
        public float FadeInSeconds => _fadeInSeconds;
        public float FadeOutSeconds => _fadeOutSeconds;
        public float ScaleInSeconds => _scaleInSeconds;
        public float ScaleFrom => _scaleFrom;

        /// <summary>Truth-level accent colour per §17.2 truth ladder.</summary>
        public Color AccentFor(string truthLevel)
        {
            switch ((truthLevel ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "observed": return _observedAccent;
                case "probable": return _probableAccent;
                case "remembered": return _rememberedAccent;
                case "inferred": return _inferredAccent;
                case "replay": return _rememberedAccent;
                default: return _mutedTextColor;
            }
        }

        public static UITheme CreateDefault() => CreateInstance<UITheme>();
    }
}
