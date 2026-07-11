using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Optional dev overrides from rtg-dev-world.json (gitignored). Checked in
    /// StreamingAssets for device builds and next to Assets/ in the Editor.
    /// </summary>
    public static class RtgDevWorldConfig
    {
        private const string FileName = "rtg-dev-world.json";

        [System.Serializable]
        private class DevWorldFile
        {
            public string apiBaseUrl;
            public string worldId;
            public string empireId;
            public string accessCode;
            public string slug;
        }

        public static bool TryApplyTo(RtgEchoSiteLoader loader)
        {
            if (loader == null || !TryRead(out DevWorldFile cfg)) return false;

            if (!string.IsNullOrWhiteSpace(cfg.apiBaseUrl))
                loader.apiBaseUrl = cfg.apiBaseUrl.Trim();
            if (!string.IsNullOrWhiteSpace(cfg.worldId))
                loader.worldId = cfg.worldId.Trim();
            if (!string.IsNullOrWhiteSpace(cfg.empireId))
                loader.empireId = cfg.empireId.Trim();

            loader.dataSource = RtgEchoSiteLoader.DataSource.LiveApi;

            Debug.Log($"[RTG] Applied {FileName} — API {loader.apiBaseUrl}");
            return true;
        }

        private static bool TryRead(out DevWorldFile cfg)
        {
            cfg = null;
            foreach (string path in CandidatePaths())
            {
                if (!File.Exists(path)) continue;

                try
                {
                    cfg = JsonUtility.FromJson<DevWorldFile>(File.ReadAllText(path));
                    if (cfg != null) return true;
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[RTG] Failed to parse {path}: {ex.Message}");
                }
            }

            return false;
        }

        private static IEnumerable<string> CandidatePaths()
        {
            yield return Path.Combine(Application.streamingAssetsPath, FileName);
#if UNITY_EDITOR
            yield return Path.GetFullPath(Path.Combine(Application.dataPath, "..", FileName));
#endif
        }
    }
}
