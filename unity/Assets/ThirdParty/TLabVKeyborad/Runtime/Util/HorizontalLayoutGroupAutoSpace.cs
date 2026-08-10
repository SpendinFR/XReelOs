using Unity.Profiling;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace TLab.VKeyborad
{
    /// <summary>
    /// For fixed-width child content, the layout space is not
    /// automatically adjusted, so this script calculates the
    /// content width of the child element and adjusts the layout
    /// space to fit the width of the parent content.
    /// </summary>
    [ExecuteAlways]
    public class HorizontalLayoutGroupAutoSpace : UIBehaviour
    {
        [SerializeField] private HorizontalLayoutGroup m_layoutGroup;

        [SerializeField] private bool m_updateEveryFrame = false;

        private static readonly ProfilerMarker HorizontalLayoutGroupFitPerMarker =
            new ProfilerMarker("[TLAB] HorizontalLayoutGroupFitPerMarker.Fit");

        public void Fit()
        {
            if (m_layoutGroup == null)
            {
                return;
            }

            using (HorizontalLayoutGroupFitPerMarker.Auto())
            {
                var rectTransform = (RectTransform)m_layoutGroup.transform;

                var rectTransformWidth = (rectTransform.parent as RectTransform).sizeDelta.x;

                var childs = new RectTransform[rectTransform.childCount];

                var childsRectTransformWidth = 0f;

                for (int i = 0; i < childs.Length; i++)
                {
                    childs[i] = (RectTransform)m_layoutGroup.transform.GetChild(i);

                    childsRectTransformWidth += childs[i].sizeDelta.x;
                }

                if (childsRectTransformWidth == 0)
                {
                    // Perhaps horizontal layout groups are in process (first frame), so skip the layout space adjustment.
                    return;
                }

#if UNITY_EDITOR
                var prev = m_layoutGroup.spacing;
#endif

                m_layoutGroup.spacing = (rectTransformWidth - childsRectTransformWidth) / (childs.Length - 1);

#if UNITY_EDITOR
                if (!Application.isPlaying && (prev != m_layoutGroup.spacing))
                    UnityEditor.EditorUtility.SetDirty(m_layoutGroup);
#endif
            }
        }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();

            Fit();
        }

        protected override void OnTransformParentChanged()
        {
            base.OnTransformParentChanged();

            Awake();
        }

#if UNITY_EDITOR
        protected override void Reset()
        {
            if (m_layoutGroup == null)
                m_layoutGroup = GetComponent<HorizontalLayoutGroup>();
        }
#endif

        protected override void Awake()
        {
            base.Awake();

            Fit();

            enabled = m_updateEveryFrame;
        }

        private void Update()
        {
            if ((m_layoutGroup != null) && m_layoutGroup.transform.hasChanged)
            {
                Fit();
            }
        }
    }
}
