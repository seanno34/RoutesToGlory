using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Optional Tripo hull orientation overrides from rtg-ship-tuning.json (gitignored).
    /// Tune live in Play mode (Settings → Hull orientation → Save tuning), then commit
    /// rtg-ship-tuning.json.example with the saved values for team defaults.
    /// </summary>
    public static class RtgShipTuningConfig
    {
        public const string FileName = "rtg-ship-tuning.json";

        [System.Serializable]
        public class ShipTuningFile
        {
            public string _comment;
            public bool autoOrientImportedHull = true;
            public Vector3 hullEulerOffset;
            public float headingOffsetDegrees;
            public bool useCustomEnginePorts;
            public Vector3 mainEngineLocal;
            public Vector3 leftEngineLocal;
            public Vector3 rightEngineLocal;
            public float exhaustLengthScale = 1f;
            public RtgExhaustColorStop[] exhaustColorStops;
            public float exhaustColorMaxMph = 99f;
            public RtgEngineCavityTuning mainCavity;
            public RtgEngineCavityTuning leftCavity;
            public RtgEngineCavityTuning rightCavity;
            public float cavitySizeMeters = 0.42f;
            public float cavityDepthOffsetMeters = 0.06f;
            public float cavityIntensity = 1f;
            public float cavityCoreRatio = 0.62f;
        }

        public static bool TryLoad(out ShipTuningFile tuning)
        {
            tuning = null;
            foreach (string path in LoadCandidatePaths())
            {
                if (!File.Exists(path)) continue;

                try
                {
                    tuning = JsonUtility.FromJson<ShipTuningFile>(File.ReadAllText(path));
                    if (tuning != null)
                    {
                        Debug.Log($"[RTG] Loaded {FileName} from {path}");
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

        public static bool TrySave(ShipTuningFile tuning, out string savedPath)
        {
            savedPath = GetWritablePath();
            if (tuning == null) return false;

            try
            {
                string directory = Path.GetDirectoryName(savedPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                tuning._comment =
                    "Tuned in Play mode (Settings → Hull orientation / Engine ports → Save tuning). " +
                    "Copy values into rtg-ship-tuning.json.example for team defaults.";
                File.WriteAllText(savedPath, JsonUtility.ToJson(tuning, true));
                Debug.Log($"[RTG] Saved {FileName} → {savedPath}");
                return true;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[RTG] Failed to save {FileName}: {ex.Message}");
                savedPath = null;
                return false;
            }
        }

        public static void ApplyTo(RtgPlayerLocation player, ShipTuningFile tuning)
        {
            if (player == null || tuning == null) return;

            player.shipAutoOrientImportedHull = tuning.autoOrientImportedHull;
            player.shipHullEulerOffset = tuning.hullEulerOffset;
            player.shipHeadingOffsetDegrees = tuning.headingOffsetDegrees;
            player.shipUseCustomEnginePorts = tuning.useCustomEnginePorts;
            player.shipMainEngineLocal = tuning.mainEngineLocal;
            player.shipLeftEngineLocal = tuning.leftEngineLocal;
            player.shipRightEngineLocal = tuning.rightEngineLocal;
            player.shipExhaustLengthScale = tuning.exhaustLengthScale > 0f
                ? tuning.exhaustLengthScale
                : 1f;
            player.shipExhaustColorMaxMph = tuning.exhaustColorMaxMph > 0f
                ? tuning.exhaustColorMaxMph
                : 99f;
            player.shipExhaustColorStops = tuning.exhaustColorStops != null && tuning.exhaustColorStops.Length > 0
                ? tuning.exhaustColorStops
                : RtgExhaustColorProfile.CreateDefaultStops();
            player.shipExhaustColorStops = RtgExhaustColorProfile.NormalizeStops(
                player.shipExhaustColorStops,
                player.shipExhaustColorMaxMph);

            RtgEngineCavityTuning legacyCavity = RtgEngineCavityTuning.FromLegacy(
                tuning.cavitySizeMeters,
                tuning.cavityDepthOffsetMeters,
                tuning.cavityIntensity,
                tuning.cavityCoreRatio);

            player.shipMainCavity = tuning.mainCavity.sizeMeters > 0f
                ? tuning.mainCavity.Clamped()
                : legacyCavity;
            player.shipLeftCavity = tuning.leftCavity.sizeMeters > 0f
                ? tuning.leftCavity.Clamped()
                : legacyCavity;
            player.shipRightCavity = tuning.rightCavity.sizeMeters > 0f
                ? tuning.rightCavity.Clamped()
                : legacyCavity;
        }

        public static ShipTuningFile CaptureFrom(RtgPlayerLocation player)
        {
            return new ShipTuningFile
            {
                autoOrientImportedHull = player.shipAutoOrientImportedHull,
                hullEulerOffset = player.shipHullEulerOffset,
                headingOffsetDegrees = player.shipHeadingOffsetDegrees,
                useCustomEnginePorts = player.shipUseCustomEnginePorts,
                mainEngineLocal = player.shipMainEngineLocal,
                leftEngineLocal = player.shipLeftEngineLocal,
                rightEngineLocal = player.shipRightEngineLocal,
                exhaustLengthScale = player.shipExhaustLengthScale,
                exhaustColorStops = player.shipExhaustColorStops,
                exhaustColorMaxMph = player.shipExhaustColorMaxMph,
                mainCavity = player.shipMainCavity.Clamped(),
                leftCavity = player.shipLeftCavity.Clamped(),
                rightCavity = player.shipRightCavity.Clamped(),
            };
        }

        public static string GetWritablePath()
        {
#if UNITY_EDITOR
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", FileName));
#else
            return Path.Combine(Application.persistentDataPath, FileName);
#endif
        }

        private static IEnumerable<string> LoadCandidatePaths()
        {
            yield return Path.Combine(Application.streamingAssetsPath, FileName);
#if UNITY_EDITOR
            yield return Path.GetFullPath(Path.Combine(Application.dataPath, "..", FileName));
#endif
            yield return Path.Combine(Application.persistentDataPath, FileName);
        }
    }
}
