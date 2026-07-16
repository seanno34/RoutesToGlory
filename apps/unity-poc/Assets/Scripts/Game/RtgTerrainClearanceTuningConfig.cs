using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Corridor terrain clearance tuning from rtg-terrain-clearance-tuning.json (gitignored).
    /// Affects glider commitment only — Light Road reads committed ground via
    /// <see cref="RtgTerrainHeight.GetTrailReferenceGroundHeight"/>.
    /// See <see cref="RtgTerrainElevationGuards"/> before changing fields or defaults.
    /// </summary>
    public static class RtgTerrainClearanceTuningConfig
    {
        public const string FileName = "rtg-terrain-clearance-tuning.json";

        [System.Serializable]
        public class TerrainClearanceTuningFile
        {
            public string _comment;
            public float corridorSampleSpacingM = 12f;
            public float corridorLookAheadM = 72f;
            public float consistencyBandM = 1.25f;
            public float minConsistentDistanceSlowM = 36f;
            public float minConsistentDistanceFastM = 18f;
            public float consistencyFullSpeedMps = 25f;
            public float minLevelChangeM = 1.25f;
            public float committedBlendUpSeconds = 0.4f;
            public float committedBlendDownSeconds = 0.55f;
            public bool useRaycastFallback = true;

            // Pre-corridor field names (still read from older saved JSON).
            public float heightDeadbandM;
            public float climbSnapThresholdSlowM;
            public float climbSnapThresholdFastM;
            public float climbSnapFullSpeedMps;
            public float heightBlendUpSeconds;
            public float heightBlendDownSeconds;
            public float forwardSampleDistanceM;
        }

        public static bool TryLoad(out TerrainClearanceTuningFile tuning)
        {
            tuning = null;
            foreach (string path in LoadCandidatePaths())
            {
                if (!File.Exists(path)) continue;

                try
                {
                    tuning = JsonUtility.FromJson<TerrainClearanceTuningFile>(File.ReadAllText(path));
                    if (tuning != null)
                    {
                        MigrateLegacyFields(tuning);
                        NormalizeMissingDefaults(tuning);
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

        public static bool TrySave(TerrainClearanceTuningFile tuning, out string savedPath)
        {
            savedPath = GetWritablePath();
            if (tuning == null) return false;

            try
            {
                string directory = Path.GetDirectoryName(savedPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                tuning._comment =
                    "Corridor clearance tuning — Settings → Terrain clearance → Save tuning. " +
                    "Affects glider only; Light Road follows GetTrailReferenceGroundHeight. " +
                    "See RtgTerrainElevationGuards before changing pipeline.";
                File.WriteAllText(savedPath, JsonUtility.ToJson(tuning, true));
                Debug.Log($"[RTG] Saved {FileName} → {savedPath}");
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

        public static void ApplyTo(RtgTerrainHeight terrainHeight, TerrainClearanceTuningFile tuning)
        {
            if (terrainHeight == null || tuning == null) return;

            terrainHeight.corridorSampleSpacingM = Mathf.Clamp(tuning.corridorSampleSpacingM, 4f, 30f);
            terrainHeight.corridorLookAheadM = Mathf.Clamp(tuning.corridorLookAheadM, 24f, 120f);
            terrainHeight.consistencyBandM = Mathf.Clamp(tuning.consistencyBandM, 0.3f, 4f);
            terrainHeight.minConsistentDistanceSlowM =
                Mathf.Clamp(tuning.minConsistentDistanceSlowM, 12f, 80f);
            terrainHeight.minConsistentDistanceFastM =
                Mathf.Clamp(tuning.minConsistentDistanceFastM, 8f, 60f);
            terrainHeight.consistencyFullSpeedMps = Mathf.Clamp(tuning.consistencyFullSpeedMps, 5f, 60f);
            terrainHeight.minLevelChangeM = Mathf.Clamp(tuning.minLevelChangeM, 0.25f, 6f);
            terrainHeight.committedBlendUpSeconds = Mathf.Clamp(tuning.committedBlendUpSeconds, 0.05f, 2f);
            terrainHeight.committedBlendDownSeconds = Mathf.Clamp(tuning.committedBlendDownSeconds, 0.05f, 2f);
            terrainHeight.useRaycastFallback = tuning.useRaycastFallback;
            terrainHeight.ResetHeightSmoothing();
        }

        public static TerrainClearanceTuningFile CaptureFrom(RtgTerrainHeight terrainHeight)
        {
            if (terrainHeight == null)
                return new TerrainClearanceTuningFile();

            return new TerrainClearanceTuningFile
            {
                corridorSampleSpacingM = terrainHeight.corridorSampleSpacingM,
                corridorLookAheadM = terrainHeight.corridorLookAheadM,
                consistencyBandM = terrainHeight.consistencyBandM,
                minConsistentDistanceSlowM = terrainHeight.minConsistentDistanceSlowM,
                minConsistentDistanceFastM = terrainHeight.minConsistentDistanceFastM,
                consistencyFullSpeedMps = terrainHeight.consistencyFullSpeedMps,
                minLevelChangeM = terrainHeight.minLevelChangeM,
                committedBlendUpSeconds = terrainHeight.committedBlendUpSeconds,
                committedBlendDownSeconds = terrainHeight.committedBlendDownSeconds,
                useRaycastFallback = terrainHeight.useRaycastFallback,
            };
        }

        public static TerrainClearanceTuningFile Defaults() => new TerrainClearanceTuningFile();

        private static void MigrateLegacyFields(TerrainClearanceTuningFile tuning)
        {
            if (tuning == null) return;

            if (tuning.consistencyBandM <= 0f && tuning.heightDeadbandM > 0f)
                tuning.consistencyBandM = tuning.heightDeadbandM;
            if (tuning.minConsistentDistanceSlowM <= 0f && tuning.climbSnapThresholdSlowM > 0f)
                tuning.minConsistentDistanceSlowM = tuning.climbSnapThresholdSlowM;
            if (tuning.minConsistentDistanceFastM <= 0f && tuning.climbSnapThresholdFastM > 0f)
                tuning.minConsistentDistanceFastM = tuning.climbSnapThresholdFastM;
            if (tuning.consistencyFullSpeedMps <= 0f && tuning.climbSnapFullSpeedMps > 0f)
                tuning.consistencyFullSpeedMps = tuning.climbSnapFullSpeedMps;
            if (tuning.committedBlendUpSeconds <= 0f && tuning.heightBlendUpSeconds > 0f)
                tuning.committedBlendUpSeconds = tuning.heightBlendUpSeconds;
            if (tuning.committedBlendDownSeconds <= 0f && tuning.heightBlendDownSeconds > 0f)
                tuning.committedBlendDownSeconds = tuning.heightBlendDownSeconds;
            if (tuning.corridorLookAheadM <= 0f && tuning.forwardSampleDistanceM > 0f)
                tuning.corridorLookAheadM = tuning.forwardSampleDistanceM;
        }

        private static void NormalizeMissingDefaults(TerrainClearanceTuningFile tuning)
        {
            if (tuning == null) return;

            TerrainClearanceTuningFile defaults = new TerrainClearanceTuningFile();
            if (tuning.corridorSampleSpacingM <= 0f)
                tuning.corridorSampleSpacingM = defaults.corridorSampleSpacingM;
            if (tuning.corridorLookAheadM <= 0f)
                tuning.corridorLookAheadM = defaults.corridorLookAheadM;
            if (tuning.consistencyBandM <= 0f)
                tuning.consistencyBandM = defaults.consistencyBandM;
            if (tuning.minConsistentDistanceSlowM <= 0f)
                tuning.minConsistentDistanceSlowM = defaults.minConsistentDistanceSlowM;
            if (tuning.minConsistentDistanceFastM <= 0f)
                tuning.minConsistentDistanceFastM = defaults.minConsistentDistanceFastM;
            if (tuning.consistencyFullSpeedMps <= 0f)
                tuning.consistencyFullSpeedMps = defaults.consistencyFullSpeedMps;
            if (tuning.minLevelChangeM <= 0f)
                tuning.minLevelChangeM = defaults.minLevelChangeM;
            if (tuning.committedBlendUpSeconds <= 0f)
                tuning.committedBlendUpSeconds = defaults.committedBlendUpSeconds;
            if (tuning.committedBlendDownSeconds <= 0f)
                tuning.committedBlendDownSeconds = defaults.committedBlendDownSeconds;
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
                Debug.Log(
                    $"[RTG] Copied {FileName} to StreamingAssets for device builds → {destPath}");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[RTG] Failed to copy {FileName} to StreamingAssets: {ex.Message}");
            }
        }
#endif

        private static IEnumerable<string> LoadCandidatePaths()
        {
            const string exampleName = FileName + ".example";
#if UNITY_EDITOR
            yield return Path.GetFullPath(Path.Combine(Application.dataPath, "..", FileName));
#else
            yield return Path.Combine(Application.persistentDataPath, FileName);
#endif
            yield return Path.Combine(Application.streamingAssetsPath, FileName);
            yield return Path.GetFullPath(Path.Combine(Application.dataPath, "..", exampleName));
            yield return Path.Combine(Application.streamingAssetsPath, exampleName);
        }
    }
}
