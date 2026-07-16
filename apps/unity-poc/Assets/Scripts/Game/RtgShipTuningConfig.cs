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
            public bool enginePortsMeshLocal;
            public Vector3 mainEngineLocal;
            public Vector3 leftEngineLocal;
            public Vector3 rightEngineLocal;
            public RtgExhaustAnchor mainExhaustAnchor;
            public RtgExhaustAnchor leftExhaustAnchor;
            public RtgExhaustAnchor rightExhaustAnchor;
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
                    "Tuned in Play mode (Settings → Exhaust position uses Hull/Attachments socket locals). " +
                    "Copy values into rtg-ship-tuning.json.example for team defaults.";
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

        /// <summary>
        /// Applies exhaust, cavity, and engine-port tuning only — does not touch hull
        /// orientation fields. Use <see cref="ApplyTo"/> when loading full ship tuning.
        /// </summary>
        public static void ApplyExhaustTo(RtgPlayerLocation player, ShipTuningFile tuning)
        {
            if (player == null || tuning == null) return;

            ApplyExhaustFields(player, tuning);
        }

        public static void ApplyTo(RtgPlayerLocation player, ShipTuningFile tuning)
        {
            if (player == null || tuning == null) return;

            player.shipAutoOrientImportedHull = tuning.autoOrientImportedHull;
            player.shipHullEulerOffset = tuning.hullEulerOffset;
            player.shipHeadingOffsetDegrees = tuning.headingOffsetDegrees;

            ApplyExhaustFields(player, tuning);
        }

        private static void ApplyExhaustFields(RtgPlayerLocation player, ShipTuningFile tuning)
        {
            player.shipEnginePortsMeshLocal = true;
            player.shipMainExhaustAnchor = ResolveAnchor(
                tuning.mainExhaustAnchor,
                RtgGliderExhaustAnchors.DefaultMain);
            player.shipLeftExhaustAnchor = ResolveAnchor(
                tuning.leftExhaustAnchor,
                RtgGliderExhaustAnchors.DefaultLeft);
            player.shipRightExhaustAnchor = ResolveAnchor(
                tuning.rightExhaustAnchor,
                RtgGliderExhaustAnchors.DefaultRight);
            if (RtgGliderExhaustAnchors.HasSavedData(tuning.mainExhaustAnchor)
                || RtgGliderExhaustAnchors.HasSavedData(tuning.leftExhaustAnchor)
                || RtgGliderExhaustAnchors.HasSavedData(tuning.rightExhaustAnchor))
            {
                player.shipUseCustomEnginePorts = true;
            }
            else
            {
                player.shipUseCustomEnginePorts = tuning.useCustomEnginePorts;
            }

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
            MigrateLegacyPlumeTuning(player, tuning);

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

            RtgGliderExhaustTuningSanitizer.TrySanitizePlayerExhaust(player, null, out _, out _);
        }

        private static RtgExhaustAnchor ResolveAnchor(RtgExhaustAnchor loaded, RtgExhaustAnchor fallback)
        {
            return RtgGliderExhaustAnchors.HasSavedData(loaded)
                ? loaded.Clamped()
                : fallback;
        }

        private static void MigrateLegacyPlumeTuning(RtgPlayerLocation player, ShipTuningFile tuning)
        {
            if (player?.shipExhaustColorStops == null || player.shipExhaustColorStops.Length == 0)
                return;

            float legacyScale = tuning.exhaustLengthScale > 0f ? tuning.exhaustLengthScale : 1f;
            float legacyPlumeMax = tuning.mainCavity.plumeMaxLengthMeters > 0f
                ? tuning.mainCavity.plumeMaxLengthMeters
                : RtgEngineCavityTuning.Default.plumeMaxLengthMeters;
            bool hasLegacyScale = !Mathf.Approximately(legacyScale, 1f);
            bool hasLegacyPlumeMax = tuning.mainCavity.plumeMaxLengthMeters > 0f;

            if (!hasLegacyScale && !hasLegacyPlumeMax)
                return;

            var migrated = new RtgExhaustColorStop[player.shipExhaustColorStops.Length];
            for (int i = 0; i < player.shipExhaustColorStops.Length; i++)
            {
                RtgExhaustColorStop stop = player.shipExhaustColorStops[i];
                if (hasLegacyPlumeMax && stop.plumeMaxLengthMeters <= 0f)
                    stop.plumeMaxLengthMeters = legacyPlumeMax;
                if (hasLegacyScale && stop.plumeLengthScale <= 0f)
                    stop.plumeLengthScale = legacyScale;
                migrated[i] = stop;
            }

            player.shipExhaustColorStops = RtgExhaustColorProfile.NormalizeStops(
                migrated,
                player.shipExhaustColorMaxMph);
        }

        public static ShipTuningFile CaptureFrom(RtgPlayerLocation player)
        {
            bool hasAnchors = RtgGliderExhaustAnchors.HasSavedData(player.shipMainExhaustAnchor)
                || RtgGliderExhaustAnchors.HasSavedData(player.shipLeftExhaustAnchor)
                || RtgGliderExhaustAnchors.HasSavedData(player.shipRightExhaustAnchor);
            return new ShipTuningFile
            {
                autoOrientImportedHull = player.shipAutoOrientImportedHull,
                hullEulerOffset = player.shipHullEulerOffset,
                headingOffsetDegrees = player.shipHeadingOffsetDegrees,
                useCustomEnginePorts = hasAnchors || player.shipUseCustomEnginePorts,
                enginePortsMeshLocal = player.shipEnginePortsMeshLocal,
                mainExhaustAnchor = player.shipMainExhaustAnchor.Clamped(),
                leftExhaustAnchor = player.shipLeftExhaustAnchor.Clamped(),
                rightExhaustAnchor = player.shipRightExhaustAnchor.Clamped(),
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
#if UNITY_EDITOR
            yield return Path.GetFullPath(Path.Combine(Application.dataPath, "..", FileName));
#else
            // Device: user-saved tuning overrides bundled defaults.
            yield return Path.Combine(Application.persistentDataPath, FileName);
#endif
            yield return Path.Combine(Application.streamingAssetsPath, FileName);
        }
    }
}
