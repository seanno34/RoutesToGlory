using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace RoutesToGlory.EditorTools
{
    /// <summary>
    /// Before iOS/Android builds: auto-sync ship art + Tripo hull into Resources
    /// and fail fast if device assets are still missing.
    /// REGRESSION: removing this preprocessor or weakening ValidateDeviceAssets lets device builds ship
    /// without textured Tripo hull (editor Play still looks fine via TripoModels path).
    /// </summary>
    public sealed class RtgPlayerShipBuildPreprocessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (!IsMobileTarget(report.summary.platform))
                return;

            Debug.Log(
                $"[RTG] Preparing player ship assets for {report.summary.platform} build…");

            if (!RtgPlayerShipAssetSync.PrepareForDeviceBuild(out string error))
            {
                throw new BuildFailedException(
                    "[RTG] Player ship is not ready for a device build.\n" + error);
            }
        }

        private static bool IsMobileTarget(BuildTarget target)
        {
            return target == BuildTarget.iOS || target == BuildTarget.Android;
        }
    }
}
