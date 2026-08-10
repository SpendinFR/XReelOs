using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace TLab.VKeyborad
{
    [RequireComponent(typeof(AudioSource))]
    public class VKeyborad : BaseVKeyborad
    {
        [Header("Audio")]
        [SerializeField] private AudioClip m_keyStroke;

        [Header("Key")]
        [SerializeField] private GameObject m_keyBOX;
        [SerializeField] private GameObject m_layer0;
        [SerializeField] private GameObject m_layer1;
        [SerializeField] private GameObject m_LayerS;

        [Header("Options")]
        [SerializeField] private bool m_hideOnStart = false;

        [Header("Transform Anchor")]
        [SerializeField] private Transform m_anchor;

#if UNITY_EDITOR
        [Header("Keyborad Visual (Editor Only)")]
        public Sprite m_keyImage;
        public Material m_keyMat;
        public Color m_keyImageColor = Color.white;

        public ColorBlock m_keyColorBlock;
#endif

        private bool m_shift = false;

        private float m_inertia = 0.0f;

        private AudioSource m_audioSource;

        private List<string> m_keyBuffer = new List<string>();
        private List<SKeyCode> m_sKeyBuffer = new List<SKeyCode>();

        private const float IMMEDIATELY = 0f;
        private const float INERTIA = 0.1f;

        public bool shift => m_shift;

        public override bool isVisible => m_keyBOX.activeSelf;

        private string THIS_NAME => "[ " + this.GetType() + "] ";

        public void OnKeyPress(string key) => m_keyBuffer.Add(key);

        public void OnSKeyPress(SKeyCode sKey) => m_sKeyBuffer.Add(sKey);

        public void SetTransform(Vector3 position, Vector3 target, Vector3 worldUp)
        {
            if (m_anchor == null)
                m_anchor = this.transform;

            m_anchor.position = position;

            m_anchor.LookAt(target, worldUp);
        }

        public override void SetVisibility(bool active)
        {
            if (active == isVisible)
                return;

            m_keyBOX.SetActive(active);

            m_onVisibilityChanged.Invoke(active);
        }

        public override void SetUp()
        {
            if (m_initialized)
            {
                Debug.LogError(THIS_NAME + "keyborad has already been initialised");
                return;
            }

            m_mobile = Platform.mobile;

            if (m_mobile)
            {
                foreach (var key in KeyBase.Keys(m_keyBOX, true))
                    key.keyborad = this;
            }
            else
            {
                SetVisibility(false);
            }

            if (m_audioSource == null)
                m_audioSource = GetComponent<AudioSource>();

            m_initialized = true;
        }

        protected override void Start()
        {
            base.Start();

            if (m_hideOnStart)
                SetVisibility(false);
        }

        private void Update()
        {
            if (!m_initialized)
            {
                return;
            }

            if (m_mobile)
            {
                foreach (SKeyCode sKey in m_sKeyBuffer)
                {
                    switch (sKey)
                    {
                        case SKeyCode.BackSpace:
                            m_inputField?.OnBackSpaceKey();
                            break;
                        case SKeyCode.Return:
                            m_inputField?.OnEnterKey();
                            break;
                        case SKeyCode.Shift:
                            m_shift = !m_shift;

                            foreach (var key in KeyBase.Keys(m_keyBOX, false))
                                key.OnShift();

                            m_inputField?.OnShiftKey();
                            break;
                        case SKeyCode.Space:
                            m_inputField?.OnSpaceKey();
                            break;
                        case SKeyCode.Tab:
                            m_inputField?.OnTabKey();
                            break;
                    }

                    AudioHandler.ShotAudio(m_audioSource, m_keyStroke, IMMEDIATELY);
                }

                foreach (string key in m_keyBuffer)
                {
                    m_inputField?.OnKey(key);

                    AudioHandler.ShotAudio(m_audioSource, m_keyStroke, IMMEDIATELY);
                }

                m_sKeyBuffer.Clear();
                m_keyBuffer.Clear();
            }
            else
            {
                m_inertia += Time.deltaTime;

                if (Input.anyKey)
                {
                    string inputString = Input.inputString;

                    if (Input.GetKeyDown(KeyCode.Return))
                    {
                        m_inputField?.OnEnterKey();
                    }
                    else if (Input.GetKeyDown(KeyCode.Tab))
                    {
                        m_inputField?.OnTabKey();
                    }
                    else if (Input.GetKeyDown(KeyCode.Space))
                    {
                        m_inputField?.OnSpaceKey();
                    }
                    else if (Input.GetKey(KeyCode.Backspace) && m_inertia > INERTIA)
                    {
                        m_inputField?.OnBackSpaceKey();
                        m_inertia = 0.0f;
                    }
                    else if (inputString != "" && inputString != "")
                    {
                        m_inputField?.OnKey(inputString);
                    }
                    else if (Input.GetMouseButtonDown(1))
                    {
                        m_inputField?.OnKey(GUIUtility.systemCopyBuffer);
                    }
                }
            }
        }

        public void SwitchLayer()
        {
            var active = m_layer0.activeSelf;
            m_layer0.SetActive(!active);
            m_layer1.SetActive(active);

            AudioHandler.ShotAudio(m_audioSource, m_keyStroke, IMMEDIATELY);
        }

#if UNITY_EDITOR
        public void Attach<T, K>(GameObject root) where T : Component where K : Component
        {
            var ks = root.GetComponentsInChildren<K>();
            foreach (K k in ks)
            {
                var t = k.GetComponent<T>();

                if (t == null)
                    k.gameObject.AddComponent<T>();
            }
        }

        public void SetUpKey()
        {
            Attach<Key, Button>(m_layer0);
            Attach<Key, Button>(m_layer1);
            Attach<SKey, Button>(m_LayerS);

            foreach (var key in KeyBase.Keys(m_keyBOX, true))
            {
                key.SetUp();
                UnityEditor.EditorUtility.SetDirty(key);
            }
        }

        public void SetUpKeyVisual()
        {
            foreach (var key in KeyBase.Keys(m_keyBOX, true))
            {
                var button = key.GetComponent<Button>();
                button.colors = m_keyColorBlock;

                var image = key.GetComponent<Image>();
                image.color = m_keyImageColor;
                image.sprite = m_keyImage;
                image.material = m_keyMat;

                UnityEditor.EditorUtility.SetDirty(image);
                UnityEditor.EditorUtility.SetDirty(button);
            }
        }
#endif
    }
}
