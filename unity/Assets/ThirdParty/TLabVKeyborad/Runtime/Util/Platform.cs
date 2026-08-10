#define DEBUG
#undef DEBUG

namespace TLab.VKeyborad
{
    public static class Platform
    {
#if !UNITY_EDITOR && UNITY_WEBGL || DEBUG
        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern bool IsMobile();
#endif

        public static bool mobile => _IsMobile();

        public static bool _IsMobile()
        {
#if !UNITY_EDITOR && UNITY_WEBGL || DEBUG
            return IsMobile();
#elif UNITY_ANDROID || DEBUG
            return true;
#else
            return false;
#endif
        }
    }
}
