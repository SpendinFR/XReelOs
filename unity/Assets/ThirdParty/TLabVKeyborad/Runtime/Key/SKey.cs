using UnityEngine;

namespace TLab.VKeyborad
{
    public enum SKeyCode
    {
        BackSpace,
        Tab,
        Space,
        Shift,
        Return,
    }

    public class SKey : KeyBase
    {
        [SerializeField] private SKeyCode m_sKey;

        public override void OnPress() => keyborad.OnSKeyPress(m_sKey);

        public override void OnShift() => m_lowerDisp.SetActive(true);

#if UNITY_EDITOR
        public override void SetUp()
        {
            base.SetUp();

            string name = gameObject.name;
            switch (name)
            {
                case "BackSpace":
                    m_sKey = SKeyCode.BackSpace;
                    break;
                case "Return":
                    m_sKey = SKeyCode.Return;
                    break;
                case "Shift":
                    m_sKey = SKeyCode.Shift;
                    break;
                case "Space":
                    m_sKey = SKeyCode.Space;
                    break;
                case "Tab":
                    m_sKey = SKeyCode.Tab;
                    break;
            }

            m_lowerDisp = transform.GetChild(0).gameObject;
            m_upperDisp = transform.GetChild(0).gameObject;
        }
#endif
    }
}
