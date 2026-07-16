using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Tripo Xenite deposit orientation from rtg-xenite-deposit-tuning.json (gitignored).
    /// Tune in Play mode (Settings → Xenite deposit), then copy values to the .example file.
    /// </summary>
    public static class RtgXeniteDepositTuningConfig
    {
        public const string FileName = "rtg-xenite-deposit-tuning.json";

        public static Vector3 RuntimeEulerOffset { get; private set; } = DefaultEulerOffset;

        public static readonly Vector3 DefaultEulerOffset = new Vector3(270f, 0f, 0f);

        [System.Serializable]
        public class XeniteDepositTuningFile
        {
            public string _comment;
            public Vector3 depositEulerOffset = DefaultEulerOffset;
        }

        public static bool TryLoad(out XeniteDepositTuningFile tuning)
        {
            tuning = null;
            foreach (string path in LoadCandidatePaths())
            {
                if (!File.Exists(path)) continue;

                try
                {
                    tuning = JsonUtility.FromJson<XeniteDepositTuningFile>(File.ReadAllText(path));
                    if (tuning != null)
                    {
                        if (tuning.depositEulerOffset == new Vector3(90f, 0f, 0f))
                            tuning.depositEulerOffset = DefaultEulerOffset;
                        RuntimeEulerOffset = tuning.depositEulerOffset;
                        Debug.Log($"[RTG] Loaded {FileName} from {path} — euler={RuntimeEulerOffset}");
                        return true;
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[RTG] Failed to parse {path}: {ex.Message}");
                }
            }

            return false;
        }

        public static bool TrySave(XeniteDepositTuningFile tuning, out string savedPath)
        {
            savedPath = GetWritablePath();
            if (tuning == null) return false;

            try
            {
                string directory = Path.GetDirectoryName(savedPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                tuning._comment =
                    "Tuned in Play mode (Settings → Xenite deposit). Applied when xenite deposits spawn.";
                RuntimeEulerOffset = tuning.depositEulerOffset;
                File.WriteAllText(savedPath, JsonUtility.ToJson(tuning, true));
                Debug.Log($"[RTG] Saved {FileName} → {savedPath} (euler={RuntimeEulerOffset})");
#if UNITY_EDITOR
                TryCopyToStreamingAssets(savedPath);
#endif
                return true;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[RTG] Failed to save {FileName}: {ex.Message}");
                savedPath = null;
                return false;
            }
        }

        public static void ApplyRuntimeEuler(Vector3 euler)
        {
            RuntimeEulerOffset = euler;
        }

        public static XeniteDepositTuningFile CaptureFrom(Vector3 euler)
        {
            return new XeniteDepositTuningFile { depositEulerOffset = euler };
        }

        public static XeniteDepositTuningFile CaptureFrom(RtgPlayerLocation player)
        {
            if (player == null)
                return new XeniteDepositTuningFile();
            return CaptureFrom(player.xeniteDepositEulerOffset);
        }

        public static void ApplyTo(RtgPlayerLocation player, XeniteDepositTuningFile tuning)
        {
            if (player == null || tuning == null) return;
            player.xeniteDepositEulerOffset = tuning.depositEulerOffset;
            ApplyRuntimeEuler(tuning.depositEulerOffset);
        }

        public static string GetWritablePath()
        {
#if UNITY_EDITOR
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", FileName));
#else
            return Path.Combine(Application.persistentDataPath, FileName);
#endif
        }

#if UNITY_EDITOR
        private static void TryCopyToStreamingAssets(string sourcePath)
        {
            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
                return;

            try
            {
                string streamingDir = Path.Combine(Application.dataPath, "StreamingAssets");
                Directory.CreateDirectory(streamingDir);
                string destPath = Path.Combine(streamingDir, FileName);
                File.Copy(sourcePath, destPath, overwrite: true);
                Debug.Log($"[RTG] Copied {FileName} to StreamingAssets for device builds → {destPath}");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[RTG] Failed to copy {FileName} to StreamingAssets: {ex.Message}");
            }
        }
#endif

        private static IEnumerable<string> LoadCandidatePaths()
        {
#if UNITY_EDITOR
            yield return Path.GetFullPath(Path.Combine(Application.dataPath, "..", FileName));
#else
            yield return Path.Combine(Application.persistentDataPath, FileName);
#endif
            yield return Path.Combine(Application.streamingAssetsPath, FileName);
        }
    }
}
