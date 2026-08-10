using UnityEngine;

namespace TLab.VKeyborad
{
    public static class AudioHandler
    {
        public static void ShotAudio(AudioSource audioSource, AudioClip audioClip, float delay)
        {
            if (audioSource != null && audioClip != null)
            {
                CoroutineHandler.StartStaticCoroutine(CoroutineHandler.AfterSecound(() => { audioSource.PlayOneShot(audioClip, 1.0f); }, delay));
            }
        }
    }
}
