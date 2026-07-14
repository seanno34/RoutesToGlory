using System.Runtime.InteropServices;
using UnityEngine;

namespace RoutesToGlory.Game
{
    /// <summary>Ensures Unity can hear gameplay SFX on device (listener + iOS session).</summary>
    public static class RtgAudioSession
    {
        private static bool _prepared;

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void RTG_EnablePlaybackAudioSession();
#endif

        public static void Prepare()
        {
            if (_prepared) return;
            _prepared = true;

            AudioListener.pause = false;
            AudioListener.volume = 1f;

#if UNITY_IOS && !UNITY_EDITOR
            try
            {
                RTG_EnablePlaybackAudioSession();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[RTG] iOS audio session setup failed: {e.Message}");
            }
#endif
        }

        public static void EnsureListener(Camera camera)
        {
            if (camera == null) return;

            Prepare();

            if (camera.GetComponent<AudioListener>() == null)
            {
                camera.gameObject.AddComponent<AudioListener>();
                Debug.Log("[RTG] Added AudioListener to main camera.");
            }
        }
    }
}
