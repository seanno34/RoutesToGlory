using System;
using System.Collections.Generic;
using CesiumForUnity;
using UnityEngine;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Procedural alien terrain dressing: trees, rocks, and brush scattered around
    /// the player. <b>POC note:</b> these are amateur placeholders built to test
    /// the Pathfinder beam laser — not production environmental art. Replacement
    /// deferred until after terrain tiles (Phase 1) and embedded resources (Phase 2).
    /// See docs/REALISTIC_TERRAIN_POC.md Phase 3.
    /// </summary>
    public class RtgTerrainScatter : MonoBehaviour
    {
        private enum PropKind { Tree, Rock, Brush }

        [Header("Activation")]
        [Tooltip("Scatter trees/rocks/brush while playing. POC default off — xenite deposits only.")]
        public bool enabledInPlay = false;

        [Header("Coverage")]
        [Tooltip("Fog tile size (m) — must match RtgFogOfWar.tileSizeM.")]
        public float tileSizeM = 400f;

        [Tooltip("Tile radius around the player to keep dressed.")]
        public int tileRadius = 4;

        [Tooltip("Only dress tiles the fog system has permanently revealed. Off when the world is pre-surveyed.")]
        public bool requireRevealedTiles = false;

        [Header("Density (per tile)")]
        public int treesPerTile = 2;
        public int rocksPerTile = 2;
        public int brushPerTile = 4;

        [Header("Placement")]
        [Tooltip("Ellipsoid height fallback until terrain samples arrive.")]
        public double groundHeightMeters = 1476.0;

        [Tooltip("Skip scatter within this radius (m) of Echo Sites / resources.")]
        public float markerExclusionMeters = 90f;

        [Tooltip("Edge padding inside each tile so props do not spawn on tile borders.")]
        public float tileEdgePaddingM = 28f;

        [Header("Scale (meters)")]
        public float treeHeightMin = 10f;
        public float treeHeightMax = 22f;
        public float rockSizeMin = 2.5f;
        public float rockSizeMax = 7f;
        public float brushSizeMin = 1.2f;
        public float brushSizeMax = 3.5f;

        [Header("Pathfinder persistence")]
        [Tooltip("Tiles cleared by the Pathfinder beam stay bare when revisited.")]
        public bool persistClearedTiles = true;

        private Transform _root;
        private RtgTerrainHeight _terrainHeight;
        private RtgFogOfWar _fog;
        private readonly Dictionary<string, Transform> _chunks = new();
        private readonly Dictionary<string, List<RtgScatterObstacle>> _obstaclesByTile = new();
        private readonly HashSet<string> _clearedTiles = new();
        private readonly Dictionary<Color, Material> _materialCache = new();
        private int _nextObstacleId = 1;
        private string _centerTileId;
        private float _nextRefreshTime;
        private const float RefreshIntervalSeconds = 0.75f;
        private const int ScatterSeed = 0x5A7E42;

        public static RtgTerrainScatter Find()
        {
#if UNITY_2023_1_OR_NEWER
            return UnityEngine.Object.FindFirstObjectByType<RtgTerrainScatter>();
#else
            return UnityEngine.Object.FindObjectOfType<RtgTerrainScatter>();
#endif
        }

        public static RtgTerrainScatter Ensure(RtgEchoSiteLoader loader)
        {
            RtgTerrainScatter scatter = Find();
            if (scatter != null) return scatter;

            CesiumGeoreference geo = UnityEngine.Object.FindObjectOfType<CesiumGeoreference>();
            if (geo == null) return null;

            var go = new GameObject("RTG Terrain Scatter");
            go.transform.SetParent(geo.transform, false);
            scatter = go.AddComponent<RtgTerrainScatter>();
            if (loader != null)
                scatter.groundHeightMeters = loader.groundHeightMeters;
            return scatter;
        }

        public void OnMapSpawned()
        {
            ClearAllChunks();
            _obstaclesByTile.Clear();
            _clearedTiles.Clear();
            _nextObstacleId = 1;
            _centerTileId = null;
            _nextRefreshTime = 0f;
        }

        /// <summary>Called when fog permanently reveals new tiles.</summary>
        public void NotifyExplorationChanged()
        {
            _centerTileId = null;
            _nextRefreshTime = 0f;
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying || !enabledInPlay) return;

            if (!TryGetPlayerLatLng(out double lat, out double lng)) return;

            string tileId = RtgFogTileMath.LatLngToTileId(lat, lng, tileSizeM);
            if (tileId == _centerTileId && Time.time < _nextRefreshTime)
                return;

            _centerTileId = tileId;
            _nextRefreshTime = Time.time + RefreshIntervalSeconds;
            RefreshAround(lat, lng);
        }

        private void RefreshAround(double centerLat, double centerLng)
        {
            EnsureRoot();
            EnsureServices();

            var wanted = new HashSet<string>();
            List<string> tiles = RtgFogTileMath.TilesInRadius(
                centerLat, centerLng, tileRadius * tileSizeM, tileSizeM);

            foreach (string tileId in tiles)
            {
                if (requireRevealedTiles && _fog != null && !_fog.IsTileDressable(tileId))
                    continue;

                wanted.Add(tileId);
                if (_chunks.ContainsKey(tileId)) continue;
                if (persistClearedTiles && _clearedTiles.Contains(tileId)) continue;
                SpawnChunk(tileId);
            }

            var remove = new List<string>();
            foreach (KeyValuePair<string, Transform> kv in _chunks)
            {
                if (!wanted.Contains(kv.Key))
                    remove.Add(kv.Key);
            }

            foreach (string tileId in remove)
            {
                RemoveChunk(tileId);
            }
        }

        /// <summary>
        /// Nearest vaporizable obstacle in the forward wedge within maxForwardM, or null.
        /// </summary>
        public bool TryFindNearestThreat(
            double playerLat,
            double playerLng,
            float headingRad,
            float maxForwardM,
            float halfWidthNearM,
            float halfWidthFarM,
            out RtgScatterObstacle nearest,
            out float forwardM)
        {
            nearest = null;
            forwardM = float.MaxValue;
            float bestForward = float.MaxValue;

            foreach (KeyValuePair<string, List<RtgScatterObstacle>> kv in _obstaclesByTile)
            {
                foreach (RtgScatterObstacle obstacle in kv.Value)
                {
                    if (obstacle == null) continue;
                    RtgForwardCorridor.TryCorridorFrame(
                        playerLat, playerLng, obstacle.lat, obstacle.lng, headingRad,
                        out float f, out float lateral);
                    if (!RtgForwardCorridor.IsInsideWedge(
                            f, lateral, maxForwardM, halfWidthNearM, halfWidthFarM, obstacle.radiusMeters))
                    {
                        continue;
                    }

                    if (f < bestForward)
                    {
                        bestForward = f;
                        nearest = obstacle;
                    }
                }
            }

            if (nearest == null) return false;
            forwardM = bestForward;
            return true;
        }

        /// <summary>
        /// Nearest vaporizable obstacle using world-space corridor math (aligned with beam visuals).
        /// </summary>
        public bool TryFindNearestThreatWorld(
            Vector3 originWorld,
            Vector3 forwardXZ,
            float maxForwardM,
            float halfWidthNearM,
            float halfWidthFarM,
            out RtgScatterObstacle nearest,
            out float forwardM)
        {
            nearest = null;
            forwardM = float.MaxValue;
            float bestForward = float.MaxValue;

            foreach (KeyValuePair<string, List<RtgScatterObstacle>> kv in _obstaclesByTile)
            {
                foreach (RtgScatterObstacle obstacle in kv.Value)
                {
                    if (obstacle == null) continue;
                    if (!RtgForwardCorridor.TryWorldCorridorFrame(
                            originWorld,
                            obstacle.transform.position,
                            forwardXZ,
                            out float f,
                            out float lateral))
                    {
                        continue;
                    }

                    if (!RtgForwardCorridor.IsInsideWedge(
                            f, lateral, maxForwardM, halfWidthNearM, halfWidthFarM, obstacle.radiusMeters))
                    {
                        continue;
                    }

                    if (f < bestForward)
                    {
                        bestForward = f;
                        nearest = obstacle;
                    }
                }
            }

            if (nearest == null) return false;
            forwardM = bestForward;
            return true;
        }

        /// <summary>Disintegrate vaporizable props inside the active beam wedge.</summary>
        public int VaporizeInCorridor(
            double playerLat,
            double playerLng,
            float headingRad,
            float maxForwardM,
            float halfWidthNearM,
            float halfWidthFarM)
        {
            var toRemove = new List<(string tileId, RtgScatterObstacle obstacle)>();

            foreach (KeyValuePair<string, List<RtgScatterObstacle>> kv in _obstaclesByTile)
            {
                foreach (RtgScatterObstacle obstacle in kv.Value)
                {
                    if (obstacle == null) continue;
                    RtgForwardCorridor.TryCorridorFrame(
                        playerLat, playerLng, obstacle.lat, obstacle.lng, headingRad,
                        out float f, out float lateral);
                    if (!RtgForwardCorridor.IsInsideWedge(
                            f, lateral, maxForwardM, halfWidthNearM, halfWidthFarM, obstacle.radiusMeters))
                    {
                        continue;
                    }

                    toRemove.Add((kv.Key, obstacle));
                }
            }

            foreach ((string tileId, RtgScatterObstacle obstacle) in toRemove)
                DisintegrateObstacle(tileId, obstacle);

            return toRemove.Count;
        }

        /// <summary>
        /// Disintegrate props inside the active beam wedge using world-space contact checks.
        /// </summary>
        public int VaporizeInCorridorWorld(
            Vector3 originWorld,
            Vector3 forwardXZ,
            float maxForwardM,
            float halfWidthNearM,
            float halfWidthFarM)
        {
            var toRemove = new List<(string tileId, RtgScatterObstacle obstacle)>();

            foreach (KeyValuePair<string, List<RtgScatterObstacle>> kv in _obstaclesByTile)
            {
                foreach (RtgScatterObstacle obstacle in kv.Value)
                {
                    if (obstacle == null) continue;
                    if (!RtgForwardCorridor.TryWorldCorridorFrame(
                            originWorld,
                            obstacle.transform.position,
                            forwardXZ,
                            out float f,
                            out float lateral))
                    {
                        continue;
                    }

                    if (!RtgForwardCorridor.IsInsideWedge(
                            f, lateral, maxForwardM, halfWidthNearM, halfWidthFarM, obstacle.radiusMeters))
                    {
                        continue;
                    }

                    toRemove.Add((kv.Key, obstacle));
                }
            }

            foreach ((string tileId, RtgScatterObstacle obstacle) in toRemove)
                DisintegrateObstacle(tileId, obstacle);

            return toRemove.Count;
        }

        /// <summary>Immediately disintegrate one locked-on obstacle (Pathfinder snap kill).</summary>
        public bool TryVaporizeObstacle(RtgScatterObstacle obstacle)
        {
            if (obstacle == null) return false;

            foreach (KeyValuePair<string, List<RtgScatterObstacle>> kv in _obstaclesByTile)
            {
                if (!kv.Value.Contains(obstacle)) continue;
                DisintegrateObstacle(kv.Key, obstacle);
                return true;
            }

            return false;
        }

        private void DisintegrateObstacle(string tileId, RtgScatterObstacle obstacle)
        {
            if (obstacle == null) return;

            if (_obstaclesByTile.TryGetValue(tileId, out List<RtgScatterObstacle> list))
                list.Remove(obstacle);

            if (obstacle.gameObject != null)
                Destroy(obstacle.gameObject);

            if (_obstaclesByTile.TryGetValue(tileId, out list) && list.Count == 0)
            {
                _obstaclesByTile.Remove(tileId);
                if (persistClearedTiles)
                    _clearedTiles.Add(tileId);
                if (_chunks.TryGetValue(tileId, out Transform chunk) && chunk != null)
                    Destroy(chunk.gameObject);
                _chunks.Remove(tileId);
            }
        }

        private void RemoveChunk(string tileId)
        {
            if (_chunks.TryGetValue(tileId, out Transform chunk) && chunk != null)
                Destroy(chunk.gameObject);
            _chunks.Remove(tileId);
            _obstaclesByTile.Remove(tileId);
        }

        private void SpawnChunk(string tileId)
        {
            ComputeTileBounds(tileId, out double south, out double west, out double north, out double east);
            var chunkGo = new GameObject($"Chunk_{tileId}");
            chunkGo.transform.SetParent(_root, false);
            chunkGo.hideFlags = HideFlags.HideInHierarchy;
            _chunks[tileId] = chunkGo.transform;

            var rng = new System.Random(ScatterSeed ^ tileId.GetHashCode());
            int treeBudget = treesPerTile;
            int rockBudget = rocksPerTile;
            int brushBudget = brushPerTile;
            const int maxAttempts = 48;

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                if (treeBudget <= 0 && rockBudget <= 0 && brushBudget <= 0)
                    break;

                if (!TryRandomPointInTile(
                        rng, south, west, north, east,
                        out double lat, out double lng))
                {
                    continue;
                }

                if (IsNearMarker(lat, lng))
                    continue;

                PropKind kind;
                if (treeBudget > 0 && rng.NextDouble() < 0.38)
                {
                    kind = PropKind.Tree;
                    treeBudget--;
                }
                else if (rockBudget > 0 && rng.NextDouble() < 0.55)
                {
                    kind = PropKind.Rock;
                    rockBudget--;
                }
                else if (brushBudget > 0)
                {
                    kind = PropKind.Brush;
                    brushBudget--;
                }
                else if (treeBudget > 0)
                {
                    kind = PropKind.Tree;
                    treeBudget--;
                }
                else if (rockBudget > 0)
                {
                    kind = PropKind.Rock;
                    rockBudget--;
                }
                else
                {
                    continue;
                }

                float yaw = (float)(rng.NextDouble() * 360.0);
                SpawnProp(kind, lat, lng, yaw, rng, chunkGo.transform, tileId);
            }
        }

        private void RegisterObstacle(
            string tileId,
            GameObject root,
            PropKind kind,
            double lat,
            double lng,
            float radiusM,
            float heightM)
        {
            var obstacle = root.AddComponent<RtgScatterObstacle>();
            RtgScatterObstacle.Kind obstacleKind = kind switch
            {
                PropKind.Tree => RtgScatterObstacle.Kind.Tree,
                PropKind.Rock => RtgScatterObstacle.Kind.Rock,
                _ => RtgScatterObstacle.Kind.Brush,
            };
            obstacle.Configure(_nextObstacleId++, tileId, obstacleKind, lat, lng, radiusM, heightM);

            if (!_obstaclesByTile.TryGetValue(tileId, out List<RtgScatterObstacle> list))
            {
                list = new List<RtgScatterObstacle>();
                _obstaclesByTile[tileId] = list;
            }
            list.Add(obstacle);
        }

        private void SpawnProp(
            PropKind kind,
            double lat,
            double lng,
            float yawDeg,
            System.Random rng,
            Transform parent,
            string tileId)
        {
            double groundH = GetGroundHeight(lat, lng);
            _terrainHeight?.QueueSampleIfNeeded(lat, lng);
            var root = new GameObject(kind.ToString());
            root.transform.SetParent(parent, false);
            AnchorAt(root, lng, lat, groundH);

            float radiusM;
            float heightM;
            switch (kind)
            {
                case PropKind.Tree:
                    heightM = BuildTree(root.transform, rng);
                    radiusM = heightM * 0.35f;
                    break;
                case PropKind.Rock:
                    heightM = BuildRock(root.transform, rng);
                    radiusM = heightM * 0.55f;
                    break;
                default:
                    heightM = BuildBrush(root.transform, rng);
                    radiusM = heightM * 0.75f;
                    break;
            }

            root.transform.localRotation = Quaternion.Euler(0f, yawDeg, 0f);
            RegisterObstacle(tileId, root, kind, lat, lng, radiusM, heightM);
        }

        private float BuildTree(Transform root, System.Random rng)
        {
            float height = Mathf.Lerp(treeHeightMin, treeHeightMax, (float)rng.NextDouble());
            float canopyScale = height * 0.42f;
            float trunkHeight = height * 0.55f;
            float trunkWidth = height * 0.09f;

            Material trunkMat = GetLitMaterial(
                new Color(0.22f, 0.14f, 0.32f),
                emission: new Color(0.08f, 0.05f, 0.12f));
            Material canopyMat = GetLitMaterial(
                new Color(0.18f, 0.72f, 0.58f),
                emission: new Color(0.12f, 0.55f, 0.42f));

            GameObject trunk = RtgMeshPrimitives.CreateMeshObject(
                "Trunk", RtgMeshPrimitives.Cube, trunkMat, root);
            trunk.transform.localPosition = new Vector3(0f, trunkHeight * 0.5f, 0f);
            trunk.transform.localScale = new Vector3(trunkWidth, trunkHeight, trunkWidth);

            GameObject canopy = RtgMeshPrimitives.CreateMeshObject(
                "Canopy", RtgMeshPrimitives.Sphere, canopyMat, root);
            canopy.transform.localPosition = new Vector3(0f, trunkHeight + canopyScale * 0.35f, 0f);
            float canopyJitter = 0.85f + (float)rng.NextDouble() * 0.35f;
            canopy.transform.localScale = Vector3.one * canopyScale * canopyJitter;

            // Second lobe for an alien twin-canopy silhouette.
            if (rng.NextDouble() < 0.55)
            {
                GameObject lobe = RtgMeshPrimitives.CreateMeshObject(
                    "CanopyLobe", RtgMeshPrimitives.Sphere, canopyMat, root);
                float side = canopyScale * 0.55f;
                lobe.transform.localPosition = new Vector3(
                    (float)(rng.NextDouble() - 0.5) * side,
                    trunkHeight + canopyScale * 0.2f,
                    (float)(rng.NextDouble() - 0.5) * side);
                lobe.transform.localScale = Vector3.one * canopyScale * 0.55f;
            }

            return height;
        }

        private float BuildRock(Transform root, System.Random rng)
        {
            float size = Mathf.Lerp(rockSizeMin, rockSizeMax, (float)rng.NextDouble());
            Material mat = GetLitMaterial(
                new Color(0.42f, 0.38f, 0.52f),
                emission: new Color(0.06f, 0.08f, 0.14f));

            GameObject rock = RtgMeshPrimitives.CreateMeshObject(
                "Rock", RtgMeshPrimitives.Cube, mat, root);
            rock.transform.localPosition = new Vector3(0f, size * 0.35f, 0f);
            rock.transform.localScale = new Vector3(
                size * (0.7f + (float)rng.NextDouble() * 0.5f),
                size * (0.45f + (float)rng.NextDouble() * 0.35f),
                size * (0.65f + (float)rng.NextDouble() * 0.55f));
            rock.transform.localRotation = Quaternion.Euler(
                (float)(rng.NextDouble() * 18.0),
                (float)(rng.NextDouble() * 360.0),
                (float)(rng.NextDouble() * 18.0));
            return size;
        }

        private float BuildBrush(Transform root, System.Random rng)
        {
            float size = Mathf.Lerp(brushSizeMin, brushSizeMax, (float)rng.NextDouble());
            Material mat = GetLitMaterial(
                new Color(0.28f, 0.82f, 0.48f),
                emission: new Color(0.18f, 0.65f, 0.32f));

            GameObject brush = RtgMeshPrimitives.CreateMeshObject(
                "Brush", RtgMeshPrimitives.GroundQuad, mat, root);
            brush.transform.localPosition = new Vector3(0f, size * 0.08f, 0f);
            float stretch = 0.75f + (float)rng.NextDouble() * 0.65f;
            brush.transform.localScale = new Vector3(size * stretch, 1f, size * (1.1f - stretch * 0.3f));

            if (rng.NextDouble() < 0.4)
            {
                GameObject tuft = RtgMeshPrimitives.CreateMeshObject(
                    "Tuft", RtgMeshPrimitives.Sphere, mat, root);
                tuft.transform.localPosition = new Vector3(0f, size * 0.22f, 0f);
                tuft.transform.localScale = Vector3.one * size * 0.35f;
            }

            return size;
        }

        private bool TryRandomPointInTile(
            System.Random rng,
            double south,
            double west,
            double north,
            double east,
            out double lat,
            out double lng)
        {
            double padLat = tileEdgePaddingM / RtgFogTileMath.LatM;
            double midLat = (south + north) * 0.5;
            double padLng = tileEdgePaddingM / LngMetersPerDegree(midLat);

            double minLat = south + padLat;
            double maxLat = north - padLat;
            double minLng = west + padLng;
            double maxLng = east - padLng;

            if (minLat >= maxLat || minLng >= maxLng)
            {
                lat = midLat;
                lng = (west + east) * 0.5;
                return false;
            }

            lat = minLat + rng.NextDouble() * (maxLat - minLat);
            lng = minLng + rng.NextDouble() * (maxLng - minLng);
            return true;
        }

        private bool IsNearMarker(double lat, double lng)
        {
            float exclusion = Mathf.Max(10f, markerExclusionMeters);
            foreach (RtgMapMarker marker in RtgMapMarkerRegistry.All)
            {
                if (marker == null) continue;
                if (DistanceMeters(lat, lng, marker.lat, marker.lng) < exclusion)
                    return true;
            }
            return false;
        }

        private double GetGroundHeight(double lat, double lng)
        {
            if (_terrainHeight == null)
                _terrainHeight = RtgTerrainHeight.FindOrCreate();
            return _terrainHeight != null
                ? _terrainHeight.GetGroundHeight(lat, lng)
                : groundHeightMeters;
        }

        private void EnsureRoot()
        {
            if (_root != null) return;
            var go = new GameObject("ScatterProps");
            go.transform.SetParent(transform, false);
            _root = go.transform;
        }

        private void EnsureServices()
        {
            if (_terrainHeight == null)
                _terrainHeight = RtgTerrainHeight.FindOrCreate();
            if (_fog == null)
                _fog = RtgFogOfWar.Find();
        }

        private void ClearAllChunks()
        {
            foreach (string tileId in new List<string>(_chunks.Keys))
                RemoveChunk(tileId);
            if (_root != null)
            {
                Destroy(_root.gameObject);
                _root = null;
            }
        }

        private Material GetLitMaterial(Color baseColor, Color emission)
        {
            if (_materialCache.TryGetValue(baseColor, out Material cached) && cached != null)
                return cached;

            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"))
            {
                name = $"RTG_Scatter_{ColorUtility.ToHtmlStringRGB(baseColor)}",
            };
            mat.SetColor("_BaseColor", baseColor);
            mat.SetFloat("_Smoothness", 0.35f);
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", emission);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            _materialCache[baseColor] = mat;
            return mat;
        }

        private static void AnchorAt(GameObject go, double lon, double lat, double height)
        {
            CesiumGlobeAnchor anchor = go.GetComponent<CesiumGlobeAnchor>();
            if (anchor == null) anchor = go.AddComponent<CesiumGlobeAnchor>();
            anchor.SetPositionLongitudeLatitudeHeight(lon, lat, height);
        }

        private void ComputeTileBounds(
            string tileId,
            out double south,
            out double west,
            out double north,
            out double east)
        {
            RtgFogTileMath.TileIdToCenter(tileId, tileSizeM, out double cLat, out double cLng);
            double halfLat = (tileSizeM * 0.5) / RtgFogTileMath.LatM;
            double halfLng = (tileSizeM * 0.5) / LngMetersPerDegree(cLat);
            south = cLat - halfLat;
            north = cLat + halfLat;
            west = cLng - halfLng;
            east = cLng + halfLng;
        }

        private static bool TryGetPlayerLatLng(out double lat, out double lng)
        {
#if UNITY_2023_1_OR_NEWER
            RtgPlayerLocation player = UnityEngine.Object.FindFirstObjectByType<RtgPlayerLocation>();
#else
            RtgPlayerLocation player = UnityEngine.Object.FindObjectOfType<RtgPlayerLocation>();
#endif
            if (player != null && player.TryGetPlayerLatLng(out lat, out lng))
                return true;

            lat = 0;
            lng = 0;
            return false;
        }

        private static double LngMetersPerDegree(double lat) =>
            RtgFogTileMath.LatM * Math.Cos(lat * Math.PI / 180.0);

        private static double DistanceMeters(double lat1, double lng1, double lat2, double lng2)
        {
            double avgLatRad = (lat1 + lat2) * 0.5 * Math.PI / 180.0;
            double metersPerDegLng = RtgFogTileMath.LatM * Math.Cos(avgLatRad);
            double dLat = (lat2 - lat1) * RtgFogTileMath.LatM;
            double dLng = (lng2 - lng1) * metersPerDegLng;
            return Math.Sqrt(dLat * dLat + dLng * dLng);
        }

        private void OnDestroy() => ClearAllChunks();
    }
}
