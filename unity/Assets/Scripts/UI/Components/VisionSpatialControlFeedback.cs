using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

namespace MLOmega.XR.UI.Components
{
    /// <summary>
    /// Small visionOS-like spatial response for custom UGUI controls. The
    /// existing Button keeps ownership of click semantics; this component only
    /// gives gaze and pinch a smooth, unambiguous scale response.
    /// </summary>
    public sealed class VisionSpatialControlFeedback : MonoBehaviour,
        IPointerEnterHandler,
        IPointerExitHandler,
        IPointerDownHandler,
        IPointerUpHandler
    {
        [SerializeField] private float _hoverScale = 1.035f;
        [SerializeField] private float _pressedScale = .955f;
        [SerializeField] private float _response = 24f;

        private Vector3 _restScale;
        private Image _surface;
        private TMP_Text[] _labels;
        private Graphic[] _iconGraphics;
        private bool _hovered;
        private bool _pressed;
        private bool _selected;
        private Color _normalSurface = new Color(.15f, .16f, .18f, .72f);
        private Color _hoverSurface = new Color(.36f, .38f, .42f, .92f);
        private Color _pressedSurface = new Color(.94f, .96f, .99f, .98f);
        private Color _normalText = new Color(.96f, .97f, 1f, .98f);
        private Color _selectedSurface = new Color(.94f, .96f, .99f, .98f);
        private Color _selectedText = new Color(.05f, .06f, .08f, 1f);

        private void Awake()
        {
            _restScale = transform.localScale;
            ResolveVisuals();
        }

        private void OnEnable()
        {
            if (_restScale.sqrMagnitude < .01f)
                _restScale = transform.localScale;
            _hovered = false;
            _pressed = false;
        }

        private void Update()
        {
            float factor = _pressed
                ? _pressedScale
                : (_hovered ? _hoverScale : 1f);
            float blend = 1f - Mathf.Exp(-_response * Time.unscaledDeltaTime);
            transform.localScale = Vector3.Lerp(
                transform.localScale,
                _restScale * factor,
                blend);
            Color surface = _pressed
                ? _pressedSurface
                : (_selected
                    ? _selectedSurface
                    : (_hovered ? _hoverSurface : _normalSurface));
            Color text = (_pressed || _selected)
                ? _selectedText
                : _normalText;
            if (_surface != null)
                _surface.color = Color.Lerp(
                    _surface.color,
                    surface,
                    blend);
            if (_labels != null)
            {
                for (int i = 0; i < _labels.Length; i++)
                    if (
                        _labels[i] != null &&
                        _labels[i].gameObject.name != "Vision caption")
                        _labels[i].color = Color.Lerp(
                            _labels[i].color,
                            text,
                            blend);
            }
            if (_iconGraphics != null)
            {
                for (int i = 0; i < _iconGraphics.Length; i++)
                    if (_iconGraphics[i] != null)
                        _iconGraphics[i].color = Color.Lerp(
                            _iconGraphics[i].color,
                            text,
                            blend);
            }
        }

        public void Configure(
            Image surface,
            Color normalSurface,
            Color hoverSurface,
            Color pressedSurface,
            Color normalText)
        {
            _surface = surface;
            _normalSurface = normalSurface;
            _hoverSurface = hoverSurface;
            _pressedSurface = pressedSurface;
            _normalText = normalText;
            ResolveVisuals();
            if (_surface != null) _surface.color = _normalSurface;
        }

        public void SetSelected(
            bool selected,
            Color selectedSurface,
            Color selectedText)
        {
            _selected = selected;
            _selectedSurface = selectedSurface;
            _selectedText = selectedText;
        }

        public void SetLayoutScale(float scale)
        {
            _restScale = Vector3.one * Mathf.Max(.01f, scale);
            transform.localScale = _restScale;
        }

        private void ResolveVisuals()
        {
            if (_surface == null) _surface = GetComponent<Image>();
            _labels = GetComponentsInChildren<TMP_Text>(true);
            Graphic[] graphics = GetComponentsInChildren<Graphic>(true);
            var icons = new System.Collections.Generic.List<Graphic>();
            for (int i = 0; i < graphics.Length; i++)
            {
                Graphic graphic = graphics[i];
                if (
                    graphic != null &&
                    graphic != _surface &&
                    graphic.gameObject.name.StartsWith(
                        "Vision icon",
                        System.StringComparison.Ordinal))
                    icons.Add(graphic);
            }
            _iconGraphics = icons.ToArray();
        }

        public void OnPointerEnter(PointerEventData eventData) => _hovered = true;

        public void OnPointerExit(PointerEventData eventData)
        {
            _hovered = false;
            _pressed = false;
        }

        public void OnPointerDown(PointerEventData eventData) => _pressed = true;

        public void OnPointerUp(PointerEventData eventData) => _pressed = false;
    }
}
