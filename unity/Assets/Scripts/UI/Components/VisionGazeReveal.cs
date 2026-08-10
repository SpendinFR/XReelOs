using UnityEngine;
using UnityEngine.EventSystems;

namespace MLOmega.XR.UI.Components
{
    /// <summary>
    /// Keeps window chrome almost invisible until gaze reaches its real hit
    /// target. It never owns clicks, so the proven Button/pinch path is intact.
    /// </summary>
    public sealed class VisionGazeReveal : MonoBehaviour,
        IPointerEnterHandler,
        IPointerExitHandler
    {
        private CanvasGroup _group;
        private float _restAlpha = .10f;
        private float _hoverAlpha = 1f;

        public void Configure(float restAlpha, float hoverAlpha)
        {
            _restAlpha = Mathf.Clamp01(restAlpha);
            _hoverAlpha = Mathf.Clamp01(hoverAlpha);
            _group = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
            _group.interactable = true;
            _group.blocksRaycasts = true;
            _group.alpha = _restAlpha;
        }

        private void Awake()
        {
            if (_group == null) Configure(_restAlpha, _hoverAlpha);
        }

        private void OnEnable()
        {
            if (_group != null) _group.alpha = _restAlpha;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_group != null) _group.alpha = _hoverAlpha;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_group != null) _group.alpha = _restAlpha;
        }
    }
}
