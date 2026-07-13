using UnityEngine;

namespace RoutesToGlory.Game
{
    /// <summary>Short mobile haptic pulses for gameplay feedback (POC).</summary>
    internal static class RtgDeviceHaptics
    {
        public static void PulseLight(int durationMs = 18, int amplitude = 48)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            PulseAndroid(durationMs, amplitude);
#elif UNITY_IOS && !UNITY_EDITOR
            Handheld.Vibrate();
#else
            _ = durationMs;
            _ = amplitude;
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static void PulseAndroid(int durationMs, int amplitude)
        {
            try
            {
                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                using var vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                if (vibrator == null)
                {
                    Handheld.Vibrate();
                    return;
                }

                using var version = new AndroidJavaClass("android.os.Build$VERSION");
                int sdk = version.GetStatic<int>("SDK_INT");
                long duration = Mathf.Max(1, durationMs);

                if (sdk >= 26)
                {
                    using var effectClass = new AndroidJavaClass("android.os.VibrationEffect");
                    using var effect = effectClass.CallStatic<AndroidJavaObject>(
                        "createOneShot",
                        duration,
                        Mathf.Clamp(amplitude, 1, 255));
                    vibrator.Call("vibrate", effect);
                }
                else
                {
                    vibrator.Call("vibrate", duration);
                }
            }
            catch
            {
                Handheld.Vibrate();
            }
        }
#endif
    }
}
