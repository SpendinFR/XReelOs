using UnityEngine;
using UnityEngine.Events;

namespace TLab.VKeyborad
{
    public class BaseInputField : MonoBehaviour
    {
        [System.Serializable]
        public class FieldEvent
        {
            public UnityEvent<bool> onFocus;
        }

        [System.Serializable]
        public class KeyEvent
        {
            public UnityEvent onEnter;
            public UnityEvent onTab;
            public UnityEvent onShift;
            public UnityEvent onSpace;
            public UnityEvent onBackSpace;
            public UnityEvent<string> onKey;
        }

        [SerializeField] protected BaseVKeyborad m_keyborad;
        [SerializeField] protected KeyEvent m_keyEvent;
        [SerializeField] protected FieldEvent m_fieldEvent;

        [Header("Option")]
        [SerializeField] protected bool m_activeOnAwake = false;

        public bool inputFieldIsActive => m_keyborad.inputFieldBase == this;

        public BaseVKeyborad keyborad
        {
            get => m_keyborad;
            set => m_keyborad = value;
        }

        #region KEY_EVENT

        public virtual void OnEnterKey()
        {
            HandlingOnEnterKey();
            AfterOnEnterKey();
        }
        protected virtual void HandlingOnEnterKey() { }
        protected virtual void AfterOnEnterKey() => m_keyEvent.onEnter.Invoke();

        public virtual void OnTabKey()
        {
            HandlingOnTabKey();
            AfterOnTabKey();
        }
        protected virtual void HandlingOnTabKey() => AddKey("    ");
        protected virtual void AfterOnTabKey() => m_keyEvent.onTab.Invoke();

        public virtual void OnShiftKey()
        {
            HandlingOnShiftKey();
            AfterOnShiftKey();
        }
        protected virtual void HandlingOnShiftKey() { }
        protected virtual void AfterOnShiftKey() => m_keyEvent.onShift.Invoke();

        public virtual void OnSpaceKey()
        {
            HandlingOnSpaceKey();
            AfterOnSpaceKey();
        }
        protected virtual void HandlingOnSpaceKey() => AddKey(" ");
        protected virtual void AfterOnSpaceKey() => m_keyEvent.onSpace.Invoke();

        public virtual void OnBackSpaceKey()
        {
            HandlingOnBackSpaceKey();
            AfterOnBackSpaceKey();
        }
        protected virtual void HandlingOnBackSpaceKey() { }
        protected virtual void AfterOnBackSpaceKey() => m_keyEvent.onBackSpace.Invoke();

        public virtual void OnKey(string input)
        {
            HandlingOnKey(input);
            AfterOnKey(input);
        }
        protected virtual void HandlingOnKey(string input) => AddKey(input);
        protected virtual void AfterOnKey(string input) => m_keyEvent.onKey.Invoke(input);

        public virtual void AddKey(string input) { }

        #endregion KEY_EVENT

        #region FOUCUS_EVENET

        public virtual void SwitchInputField(bool active)
        {
            if (m_keyborad.inputFieldBase != this)
            {
                if (active)
                {
                    var prev = m_keyborad.inputFieldBase;

                    m_keyborad.SwitchInputField(this);

                    if (prev != null)
                        prev.AfterOnFocus(false);
                }
            }
            else
            {
                if (!active)
                    m_keyborad.SwitchInputField(null);
            }
        }

        public virtual void OnFocus(bool active)
        {
            HandlingOnFocus(active);
            AfterOnFocus(active);
        }
        protected virtual void HandlingOnFocus(bool active)
        {
            if (active == inputFieldIsActive)
                return;

            SwitchInputField(active);

            if (m_keyborad.mobile)
                m_keyborad.SetVisibility(active);
        }
        protected virtual void AfterOnFocus(bool active) => m_fieldEvent.onFocus.Invoke(active);

        public virtual void OnFocus() => OnFocus(true);

        public virtual void OnSwitchFocus() => OnFocus(!inputFieldIsActive);

        #endregion FOUCUS_EVENT

        protected virtual void Start()
        {
            if (m_activeOnAwake)
                SwitchInputField(true);
        }

        protected virtual void Update() { }
    }
}