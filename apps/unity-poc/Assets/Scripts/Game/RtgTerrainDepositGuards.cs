using System.Diagnostics;
using UnityEngine;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Regression guardrails for tile-embedded resource deposits (Phase 2).
    /// Xenite v1 spec: <c>docs/XENITE_DEPOSIT_DESIGN_BRIEF.md</c>
    ///
    /// XENITE TRIPO GUARDRAILS (Jul 2026 — do not regress):
    /// Prefab + albedo must live under Resources (see <see cref="XeniteResourcesLocalPrefabGuard"/>).
    /// Full Sync/runtime rules: <c>apps/unity-poc/docs/XENITE_SPAWN_HANDOFF.md</c>
    /// and summaries on <see cref="RtgTerrainDeposit"/> / <c>RtgMapBuilder.SyncXeniteDeposit</c>.
    /// </summary>
    public static class RtgTerrainDepositGuards
    {
        public const string XeniteDesignBriefPath = "docs/XENITE_DEPOSIT_DESIGN_BRIEF.md";

        public const string XeniteAssetBriefPath = "apps/unity-poc/docs/XENITE_DEPOSIT_ASSET_BRIEF.md";

        /// <summary>Prefab authored diameter (m) at scale 1 — see XENITE_DEPOSIT_ASSET_BRIEF.md §3.</summary>
        public const float XeniteAuthoringFootprintM = 10f;

        public const string XeniteRiftPrefabResourcesPath = "RTG_Deposits/xenite_rift";
        public const string XeniteHighlandPrefabResourcesPath = "RTG_Deposits/xenite_highland";
        public const string XeniteWastelandPrefabResourcesPath = "RTG_Deposits/xenite_wasteland";

        /// <summary>Tripo Smart Mesh default import folder at Assets root (editor fallback).</summary>
        public const string XeniteTripoImportAssetPath =
            "Assets/glowing_lava_crystal_3d_model/glowing_lava_crystal_3d_model.fbx";

        public const string XeniteTripoImportAssetPathFlat =
            "Assets/glowing_lava_crystal_3d_model.fbx";

        public static readonly string[] XeniteTripoImportCandidatePaths =
        {
            XeniteTripoImportAssetPath,
            XeniteTripoImportAssetPathFlat,
            "Assets/glowing_lava_crystal_3d_model/glowing_lava_crystal_3d_model.glb",
            "Assets/glowing_lava_crystal_3d_model.glb",
            "Assets/TripoModels/glowing_lava_crystal_3d_model/glowing_lava_crystal_3d_model.fbx",
            "Assets/TripoModels/glowing_lava_crystal_3d_model/glowing_lava_crystal_3d_model.glb",
            "Assets/Resources/RTG_Deposits/glowing_lava_crystal_3d_model/glowing_lava_crystal_3d_model",
            "Assets/Resources/RTG_Deposits/glowing_lava_crystal_3d_model",
        };

        /// <summary>Optional Resources copy before renaming to xenite_rift.</summary>
        public const string XeniteTripoResourcesFallbackPath = "RTG_Deposits/glowing_lava_crystal_3d_model";

        public static string ResolveXenitePrefabResourcesPath(string biome)
        {
            if (biome == RtgBiomePalette.Highland)
                return XeniteHighlandPrefabResourcesPath;
            if (biome == RtgBiomePalette.Wasteland)
                return XeniteWastelandPrefabResourcesPath;
            return XeniteRiftPrefabResourcesPath;
        }

        /// <summary>Canonical Xenite emissive — matches RESOURCE_MAP_ICONS glow #f97316.</summary>
        public static readonly Color XeniteCanonicalColor = new Color(0.976f, 0.451f, 0.086f);

        /// <summary>
        /// Legacy deep-orange tint kept for any remaining callers; vent cue is
        /// <see cref="RtgXeniteClaimedVentVfx"/> (ground mist / embers / point light #f97316)
        /// on every xenite deposit — claim only intensifies the light.
        /// Separate from crystal materials — never wash Tripo albedo.
        /// </summary>
        public static readonly Color XeniteClaimedGlowColor = new Color(0.78f, 0.22f, 0.02f, 1f);

        /// <summary>
        /// Resource ids with v1 embedded deposit art — only these spawn in the Unity POC map.
        /// Add ids here as each deposit brief ships; others stay in data but are not rendered.
        /// </summary>
        public static readonly string[] ActivePocDepositResourceIds = { "xenite" };

        /// <summary>
        /// REGRESSION: <c>xenite_rift.prefab</c> must reference mesh/material GUIDs under
        /// <c>Assets/Resources/RTG_Deposits/</c>. Prefabs baked from <c>TripoModels/</c> look
        /// fine in the editor until those paths strip or desync — then Tripo succeeds and
        /// procedural fallback never runs, so deposits are invisible.
        /// Also: never bake flat fuel×2.2 emission onto Resources mats — washes Tripo albedo to yellow.
        /// Also: Sync must Persist Xenite materials onto the instance before SaveAsPrefabAsset —
        /// normalizing Materials/*.mat alone leaves FBX-embedded mats without _BaseMap on the prefab.
        /// Flat albedo: <c>Resources/RTG_Deposits/Xenite_Albedo.jpg</c> (like TripoHull_Albedo).
        /// </summary>
        public const string XeniteResourcesLocalPrefabGuard =
            "Resources/RTG_Deposits mesh+external mat+_BaseMap+Xenite_Albedo (not FBX-embedded mats; no fuel wash)";

        public static bool IsActivePocDeposit(string resourceId)
        {
            if (string.IsNullOrEmpty(resourceId)) return false;
            for (int i = 0; i < ActivePocDepositResourceIds.Length; i++)
            {
                if (ActivePocDepositResourceIds[i] == resourceId)
                    return true;
            }

            return false;
        }

        /// <summary>Meters above resolved ground for deposit root (avoids burying in Cesium mesh).</summary>
        public const float DefaultDepositSurfaceClearanceM = 0.45f;

        /// <summary>Retries while Cesium tiles stream in at spawn.</summary>
        public const int DepositAnchorMaxAttempts = 5;

        public const float DepositAnchorRetryDelaySeconds = 1.25f;

        /// <summary>Max deposit glow ring alpha — higher values read as floating pins.</summary>
        public const float MaxDepositGlowAlpha = 0.22f;

        /// <summary>
        /// Mist particle start alpha for xenite vent vapor (soft translucent gas).
        /// Kept high enough to read outdoors against Cesium terrain.
        /// </summary>
        public const float MaxClaimedHaloAlpha = 0.72f;

        /// <summary>
        /// Embedded deposits must sit near local Y=0 on the anchored root (terrain surface).
        /// </summary>
        public const float MaxBodyRootOffsetM = 2f;

        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public static void WarnIfDepositUsesFloatingPinPattern(string resourceId, float bodyRootLocalYM)
        {
            if (Mathf.Abs(bodyRootLocalYM) > MaxBodyRootOffsetM)
            {
                UnityEngine.Debug.LogWarning(
                    $"[RTG] Deposit '{resourceId}' body root offset {bodyRootLocalYM:F1}m — " +
                    "embedded deposits must sit flush on terrain. See XENITE_DEPOSIT_DESIGN_BRIEF.md.");
            }
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public static void WarnIfXeniteColorDrift(Color color)
        {
            if (ColorDistance(color, XeniteCanonicalColor) > 0.35f)
            {
                UnityEngine.Debug.LogWarning(
                    "[RTG] Xenite deposit color drifted from canonical orange (#f97316). " +
                    "See XENITE_DEPOSIT_DESIGN_BRIEF.md and RtgTerrainDepositGuards.XeniteCanonicalColor.");
            }
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public static void WarnIfDepositGlowTooStrong(float alpha)
        {
            if (alpha > MaxDepositGlowAlpha)
            {
                UnityEngine.Debug.LogWarning(
                    $"[RTG] Deposit glow alpha {alpha:F2} exceeds {MaxDepositGlowAlpha} — " +
                    "reads as floating pin at pass-over altitude.");
            }
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public static void WarnIfPeriodicTrailReprojection(string caller)
        {
            UnityEngine.Debug.LogError(
                $"[RTG] {caller} must not periodically reproject deposit heights from raycasts. " +
                "Anchor once via RtgTerrainHeight.SampleHeightMostDetailed. " +
                $"See {nameof(RtgTerrainElevationGuards)}.");
        }

        private static float ColorDistance(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);
        }
    }
}
