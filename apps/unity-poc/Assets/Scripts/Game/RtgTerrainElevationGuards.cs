using System;
using System.Diagnostics;
using UnityEngine;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Regression guardrails for glider + Light Road terrain elevation.
    ///
    /// <para><b>Background (Jul 2026):</b> Per-frame raycasts and periodic full-trail
    /// reprojection caused slow-speed bounce on editor and mobile after Cesium terrain
    /// overlay work. Corridor commitment + lift-only trail updates fixed it.</para>
    ///
    /// <para><b>Glider path</b> — <see cref="RtgPlayerLocation.ApplyMarkerTerrainHeight"/> in
    /// <c>LateUpdate</c> → <see cref="RtgTerrainHeight.GetClearancePlacementHeight"/> only.
    /// Corridor samples must use cached Cesium heights (never raycast). Height changes only
    /// when terrain ahead is flat or monotonic for <c>minConsistentDistance*</c>.</para>
    ///
    /// <para><b>Light Road path</b> — <see cref="RtgLightRoad"/> runs at
    /// <see cref="LightRoadExecutionOrder"/> (after player). New points read
    /// <see cref="RtgTerrainHeight.GetTrailReferenceGroundHeight"/> (read-only glider floor).
    /// Older points are <see cref="LiftGroundMonotonic"/> only. Do not call corridor eval,
    /// <see cref="RtgTerrainHeight.GetGroundHeight"/> per frame, or refresh the whole trail
    /// from raw heights.</para>
    ///
    /// <para><b>Persisted Light Roads</b> — <see cref="RtgPersistedRouteDrawer"/> samples
    /// Cesium terrain per vertex (same sampler as deposits) then adds travel/connector
    /// clearance. Never leave saved routes at fixed ellipsoid
    /// <c>groundHeightMeters + offset</c> — path_json is lat/lng only and that floats
    /// lines above the terrain-following glider.</para>
    ///
    /// <para><b>Clearance stack</b> (ellipsoid = sampled terrain + clearance):
    /// travel / live Light Road ≈ <see cref="TravelRoadClearanceM"/>,
    /// connectors ≈ <see cref="ConnectorClearanceM"/>,
    /// glider ≈ <see cref="GliderClearanceM"/>. Keep glider above route clearances;
    /// prefer lowering routes toward terrain over raising the ship.</para>
    ///
    /// <para><b>Before changing this pipeline</b>, playtest slow cruise on editor + device:
    /// glider must not bounce; trail must not clip hills or jitter. Also reload a saved
    /// world with routes — persisted lines must sit just above terrain, under the glider.</para>
    /// </summary>
    public static class RtgTerrainElevationGuards
    {
        /// <summary>Player marker height runs first; Light Road reads glider state after.</summary>
        public const int PlayerLocationExecutionOrder = 0;

        /// <summary>Must run after <see cref="RtgPlayerLocation"/> LateUpdate terrain pass.</summary>
        public const int LightRoadExecutionOrder = 100;

        /// <summary>
        /// Live + persisted travel ribbon clearance above sampled terrain.
        /// Matches <c>RtgLightRoad.roadClearanceMeters</c> / <c>RtgPlayerLocation.roadHeightMeters</c>
        /// / <c>RtgPersistedRouteDrawer.travelHeightAboveTerrainM</c>.
        /// </summary>
        public const float TravelRoadClearanceM = 3f;

        /// <summary>
        /// Tap-claim connector clearance above sampled terrain
        /// (<c>RtgPersistedRouteDrawer.connectorHeightAboveTerrainM</c>).
        /// </summary>
        public const float ConnectorClearanceM = 7f;

        /// <summary>
        /// Default glider clearance above committed corridor ground
        /// (<c>RtgPlayerLocation.markerHeight</c> / <c>RtgTerrainHeight.markerClearanceM</c>).
        /// Must stay above <see cref="TravelRoadClearanceM"/> and <see cref="ConnectorClearanceM"/>.
        /// </summary>
        public const float GliderClearanceM = 15f;

        /// <summary>Tolerance (m) when comparing ground heights for monotonic lift.</summary>
        public const double LiftEpsilonM = 0.01;

        /// <summary>
        /// Raises stored ground to <paramref name="candidateM"/> but never lowers it.
        /// Used by Light Road so late Cesium samples can clear hills without reintroducing bounce.
        /// </summary>
        public static double LiftGroundMonotonic(double storedGroundM, double candidateM)
        {
            return candidateM > storedGroundM + LiftEpsilonM ? candidateM : storedGroundM;
        }

        /// <summary>
        /// Corridor / glider smoothing must not read per-frame raycasts — only cache + hold.
        /// Deposits and one-off probes may still use <see cref="RtgTerrainHeight.GetGroundHeight"/>.
        /// </summary>
        public static double CorridorSampleHeightOrHold(
            double cachedHeightM,
            bool hasCached,
            double holdGroundM,
            double fallbackGroundM)
        {
            if (hasCached)
                return cachedHeightM;

            return holdGroundM > double.MinValue + 1 ? holdGroundM : fallbackGroundM;
        }

        /// <summary>DEV: warns if Light Road code paths call forbidden corridor evaluation.</summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public static void WarnIfLightRoadEvaluatesCorridor(string caller)
        {
            UnityEngine.Debug.LogWarning(
                $"[RTG] {caller} must not use EvaluateCorridorGroundHeight for the Light Road. " +
                "Use GetTrailReferenceGroundHeight + TryGetCachedGroundHeight + LiftGroundMonotonic. " +
                $"See {nameof(RtgTerrainElevationGuards)}.");
        }

        /// <summary>DEV: warns if corridor sampling is wired to volatile height sources.</summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public static void WarnIfCorridorUsesRaycast(string caller)
        {
            UnityEngine.Debug.LogError(
                $"[RTG] {caller} must not use raycasts for corridor samples — causes slow-speed bounce. " +
                "Use RtgTerrainHeight corridor cache / CorridorSampleHeightOrHold only. " +
                $"See {nameof(RtgTerrainElevationGuards)}.");
        }

        /// <summary>DEV: warns if trail refresh lowers point heights (reintroduces bounce).</summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public static void WarnIfTrailGroundLowered(double beforeM, double afterM, string context)
        {
            if (afterM < beforeM - LiftEpsilonM)
            {
                UnityEngine.Debug.LogError(
                    $"[RTG] Light Road lowered ground at {context} ({beforeM:F2} → {afterM:F2} m). " +
                    "Trail points must only lift via LiftGroundMonotonic.");
            }
        }
    }
}
