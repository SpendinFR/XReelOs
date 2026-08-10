using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace TLab.VKeyborad
{
    public class CoroutineHandler : MonoBehaviour
    {
        static protected CoroutineHandler m_instance;

        static public CoroutineHandler instance
        {
            get
            {
                if (m_instance == null)
                {
                    GameObject o = new GameObject("CoroutineHandler");
                    DontDestroyOnLoad(o);
                    m_instance = o.AddComponent<CoroutineHandler>();
                }

                return m_instance;
            }
        }

        public void OnDisable()
        {
            if (m_instance)
            {
                Destroy(m_instance.gameObject);
            }
        }

        public static Coroutine StartStaticCoroutine(IEnumerator coroutine)
        {
            return instance.StartCoroutine(coroutine);
        }

        public static IEnumerator AfterFrame(UnityEvent callback, int delay)
        {
            for (int i = 0; i < delay; i++)
            {
                yield return null;
            }

            callback.Invoke();
        }

        public static IEnumerator AfterFrame(UnityAction callback, int delay)
        {
            for (int i = 0; i < delay; i++)
            {
                yield return null;
            }

            callback.Invoke();
        }

        public static IEnumerator AfterSecound(UnityEvent callback, float delay)
        {
            yield return new WaitForSeconds(delay);
            callback.Invoke();
        }

        public static IEnumerator AfterSecound(UnityAction callback, float delay)
        {
            yield return new WaitForSeconds(delay);
            callback.Invoke();
        }
    }
}
