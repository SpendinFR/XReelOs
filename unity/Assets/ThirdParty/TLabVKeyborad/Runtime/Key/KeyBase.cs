using UnityEngine;
using UnityEngine.UI;

namespace TLab.VKeyborad
{
    public class KeyBase : MonoBehaviour
    {
        [SerializeField] protected GameObject m_lowerDisp;
        [SerializeField] protected GameObject m_upperDisp;

        [HideInInspector] public VKeyborad keyborad;

        public virtual void OnPress() { }

        public virtual void OnShift() { }

        protected virtual void OnEnable()
        {
            var button = GetComponent<Button>();
            button.onClick.AddListener(OnPress);
        }

        protected virtual void OnDisable()
        {
            var button = GetComponent<Button>();
            button.onClick.RemoveAllListeners();
        }

#if UNITY_EDITOR
        public virtual void SetUp()
        {
            UnityEditor.GameObjectUtility.RemoveMonoBehavioursWithMissingScript(this.gameObject);
        }
#endif

        public static KeyBase[] Keys(GameObject target, bool includeInactive) => target.GetComponentsInChildren<KeyBase>(includeInactive);
    }
}
