using System;
using System.Collections;
using System.Collections.Generic;
using CesiumForUnity;
using UnityEngine;
using UnityEngine.Networking;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// POC fog of war: fog tiles are spawned once over the fixed Douglas play area
    /// and never removed (no camera/zoom-driven pop-in). A tight lat/lng shader
    /// bubble around the player pin clears fog; revealed areas are stamped
    /// permanently so they stay clear after the pin moves on.
    /// </summary>
    public class RtgFogOfWar : MonoBehaviour
    {
        private const float DefaultLiveRevealM = 35f;

        [Header("API (filled by Echo Site loader / 6b)")]
        public string apiBaseUrl = "http://localhost:3001/api";
        public string worldId = "";
        public string empireId = "";

        [Header("Placement")]
        public double groundHeightMeters = 1476.0;
        public float fogHeightAboveGround = 12f;

        [Header("Play area (static fog grid — synced from Echo Site loader)")]
        public double playAreaCenterLat = 42.7597;
        public double playAreaCenterLng = -105.3819;
        public float playAreaRadiusM = 5500f;

        [Header("Pin reveal (POC)")]
        [Tooltip("Clear bubble radius around the pin in real-world meters.")]
        public float liveRevealRadiusM = DefaultLiveRevealM;

        [Header("Visuals")]
        public float tileSizeM = 400f;
        public float unexploredOpacity = 0.92f;
        public int resourceShimmerDurationMs = 8000;

        private readonly Dictionary<string, GameObject> _fogTiles = new();
        private readonly Dictionary<string, Material> _fogTileMaterials = new();
        private readonly Dictionary<string, RevealAabb> _permanentReveal = new();
        private readonly HashSet<string> _shimmeringResources = new();
        private readonly HashSet<string> _seenMarkers = new();

        private Transform _fogRoot;
        private Transform _markerRoot;
        private Material _fogMaterial;
        private bool _ready;
        private bool _initializing;
        private bool _staticFogSpawned;
        private double _focusLat, _focusLng;
        private bool _hasFocus;

        private static readonly int PlayerLatLngId = Shader.PropertyToID("_PlayerLatLng");
        private static readonly int LiveRevealRadiusId = Shader.PropertyToID("_LiveRevealRadiusM");
        private static readonly int TileBoundsId = Shader.PropertyToID("_TileBounds");
        private static readonly int RevealMinId = Shader.PropertyToID("_RevealMin");
        private static readonly int RevealMaxId = Shader.PropertyToID("_RevealMax");

        private struct RevealAabb
        {
            public double South, West, North, East;
            public bool IsEmpty => South > North || West > East;
        }

        public static RtgFogOfWar Find()
        {
#if UNITY_2023_1_OR_NEWER
            return UnityEngine.Object.FindFirstObjectByType<RtgFogOfWar>();
#else
            return UnityEngine.Object.FindObjectOfType<RtgFogOfWar>();
#endif
        }

        public static RtgFogOfWar Ensure(RtgEchoSiteLoader loader)
        {
            RtgFogOfWar fog = Find();
            if (fog == null)
            {
                CesiumGeoreference geo = UnityEngine.Object.FindObjectOfType<CesiumGeoreference>();
                if (geo == null) return null;
                var go = new GameObject("RTG Fog Of War");
                go.transform.SetParent(geo.transform, false);
                fog = go.AddComponent<RtgFogOfWar>();
            }

            if (loader != null)
            {
                fog.apiBaseUrl = loader.apiBaseUrl;
                fog.worldId = loader.worldId;
                fog.empireId = loader.empireId;
                fog.groundHeightMeters = loader.groundHeightMeters;
                fog.playAreaCenterLat = loader.scatterCenterLat;
                fog.playAreaCenterLng = loader.scatterCenterLng;
                fog.playAreaRadiusM = Mathf.Min(loader.scatterRadiusMeters, 6000f);
            }

            return fog;
        }

        private void Awake()
        {
            EnsureFogMaterial();
            _fogRoot = new GameObject("Fog Tiles").transform;
            _fogRoot.SetParent(transform, false);
        }

        public void OnMapSpawned(Transform markersContainer)
        {
            _markerRoot = markersContainer;
            if (!Application.isPlaying) return;

            if (_ready)
            {
                RefreshMarkerVisibility();
                return;
            }

            if (_initializing) return;
            _initializing = true;
            StartCoroutine(InitializeRoutine());
        }

        private IEnumerator InitializeRoutine()
        {
            bool live = !string.IsNullOrWhiteSpace(worldId) && !string.IsNullOrWhiteSpace(empireId);
            if (live)
                yield return FetchFogConfig();

            SpawnStaticPlayAreaFog();

            _initializing = false;
            _ready = true;
            RefreshMarkerVisibility();
            Debug.Log(
                $"[RTG] Fog ready — {_fogTiles.Count} static tile(s), " +
                $"live reveal {liveRevealRadiusM}m at pin (not camera).");
        }

        private void SpawnStaticPlayAreaFog()
        {
            if (_staticFogSpawned) return;

            List<string> tileIds = RtgFogTileMath.TilesInRadius(
                playAreaCenterLat, playAreaCenterLng, playAreaRadiusM, tileSizeM);

            foreach (string tileId in tileIds)
            {
                if (_fogTiles.ContainsKey(tileId)) continue;
                SpawnFogTile(tileId);
            }

            _staticFogSpawned = true;
        }

        private IEnumerator FetchFogConfig()
        {
            string url = $"{apiBaseUrl.TrimEnd('/')}/worlds/{worldId}/exploration/{empireId}";
            using UnityWebRequest req = UnityWebRequest.Get(url);
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[RTG] Exploration config fetch failed ({req.responseCode}): {req.error}");
                yield break;
            }

            var resp = JsonUtility.FromJson<ExplorationResp>(req.downloadHandler.text);
            if (resp?.fogOfWar != null)
                ApplyFogConfig(resp.fogOfWar);
        }

        private void ApplyFogConfig(FogConfig cfg)
        {
            if (cfg.tileSizeM > 0) tileSizeM = cfg.tileSizeM;
            if (cfg.unexploredOpacity >= 0) unexploredOpacity = cfg.unexploredOpacity;
            if (cfg.resourceShimmerDurationMs > 0) resourceShimmerDurationMs = cfg.resourceShimmerDurationMs;
            if (_fogMaterial != null)
                _fogMaterial.SetFloat("_Opacity", unexploredOpacity);
        }

        private void UpdatePlayerShaderUniforms(double lat, double lng)
        {
            var playerVec = new Vector4((float)lat, (float)lng, 0f, 0f);
            foreach (Material mat in _fogTileMaterials.Values)
            {
                if (mat == null) continue;
                mat.SetVector(PlayerLatLngId, playerVec);
                mat.SetFloat(LiveRevealRadiusId, liveRevealRadiusM);
            }
        }

        public void ApplyExplorationDelta(string[] newlyRevealedTileIds, string[] newResourceNodeIds)
        {
            if (!_ready) return;

            if (newlyRevealedTileIds != null)
            {
                foreach (string tileId in newlyRevealedTileIds)
                    RevealEntireTile(tileId);
            }

            if (newResourceNodeIds != null)
            {
                foreach (string nodeId in newResourceNodeIds)
                    ShimmerResourceById(nodeId);
            }
        }

        /// <summary>Server-authoritative full-tile reveal (e.g. from GPS route flush).</summary>
        private void RevealEntireTile(string tileId)
        {
            if (string.IsNullOrEmpty(tileId) || !_fogTiles.ContainsKey(tileId))
                return;

            RtgFogTileMath.TileIdToCenter(tileId, tileSizeM, out double cLat, out double cLng);
            ComputeTileBounds(cLat, cLng, out float south, out float west, out float north, out float east);

            var aabb = new RevealAabb { South = south, West = west, North = north, East = east };
            _permanentReveal[tileId] = aabb;
            ApplyRevealAabbToMaterial(tileId, aabb);
            ShimmerResourcesInTile(tileId);
        }

        private void LateUpdate()
        {
            if (!_ready) return;
            if (!TryGetFocusLatLng(out double lat, out double lng)) return;

            _focusLat = lat;
            _focusLng = lng;
            _hasFocus = true;

            UpdatePlayerShaderUniforms(lat, lng);
            CommitPermanentReveal(lat, lng);
            RefreshMarkerVisibility();
        }

        /// <summary>
        /// Stamp the live reveal bubble onto each overlapping tile so it stays clear.
        /// </summary>
        private void CommitPermanentReveal(double lat, double lng)
        {
            double dLat = liveRevealRadiusM / RtgFogTileMath.LatM;
            double lngM = RtgFogTileMath.LatM * Math.Cos(lat * Math.PI / 180.0);
            double dLng = liveRevealRadiusM / lngM;

            double stampSouth = lat - dLat;
            double stampNorth = lat + dLat;
            double stampWest = lng - dLng;
            double stampEast = lng + dLng;

            foreach (KeyValuePair<string, GameObject> kv in _fogTiles)
            {
                string tileId = kv.Key;
                RtgFogTileMath.TileIdToCenter(tileId, tileSizeM, out double cLat, out double cLng);
                ComputeTileBounds(cLat, cLng, out float south, out float west, out float north, out float east);

                double overlapSouth = Math.Max(south, stampSouth);
                double overlapNorth = Math.Min(north, stampNorth);
                double overlapWest = Math.Max(west, stampWest);
                double overlapEast = Math.Min(east, stampEast);
                if (overlapSouth >= overlapNorth || overlapWest >= overlapEast)
                    continue;

                if (!_permanentReveal.TryGetValue(tileId, out RevealAabb aabb) || aabb.IsEmpty)
                {
                    aabb = new RevealAabb
                    {
                        South = overlapSouth,
                        West = overlapWest,
                        North = overlapNorth,
                        East = overlapEast,
                    };
                }
                else
                {
                    aabb.South = Math.Min(aabb.South, overlapSouth);
                    aabb.West = Math.Min(aabb.West, overlapWest);
                    aabb.North = Math.Max(aabb.North, overlapNorth);
                    aabb.East = Math.Max(aabb.East, overlapEast);
                }

                _permanentReveal[tileId] = aabb;
                ApplyRevealAabbToMaterial(tileId, aabb);
            }
        }

        private void ApplyRevealAabbToMaterial(string tileId, RevealAabb aabb)
        {
            if (!_fogTileMaterials.TryGetValue(tileId, out Material mat) || mat == null)
                return;

            mat.SetVector(RevealMinId, new Vector4((float)aabb.South, (float)aabb.West, 0f, 0f));
            mat.SetVector(RevealMaxId, new Vector4((float)aabb.North, (float)aabb.East, 0f, 0f));
        }

        private bool IsPermanentlyRevealed(double lat, double lng)
        {
            string tileId = RtgFogTileMath.LatLngToTileId(lat, lng, tileSizeM);
            if (!_permanentReveal.TryGetValue(tileId, out RevealAabb aabb) || aabb.IsEmpty)
                return false;

            return lat >= aabb.South && lat <= aabb.North &&
                   lng >= aabb.West && lng <= aabb.East;
        }

        private void SpawnFogTile(string tileId)
        {
            RtgFogTileMath.TileIdToCenter(tileId, tileSizeM, out double centerLat, out double centerLng);
            ComputeTileBounds(centerLat, centerLng, out float south, out float west, out float north, out float east);

            var go = new GameObject($"Fog {tileId}");
            go.transform.SetParent(_fogRoot, false);

            var meshFilter = go.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = BuildFogQuadMesh();

            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.allowOcclusionWhenDynamic = false;

            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = new Vector3(tileSizeM, tileSizeM, 1f);

            var anchor = go.AddComponent<CesiumGlobeAnchor>();
            anchor.SetPositionLongitudeLatitudeHeight(centerLng, centerLat, groundHeightMeters + fogHeightAboveGround);

            var tileMat = new Material(_fogMaterial) { name = $"RTG_Fog_{tileId}" };
            tileMat.SetVector(TileBoundsId, new Vector4(south, west, north, east));
            tileMat.SetVector(RevealMinId, new Vector4(9999f, 9999f, 0f, 0f));
            tileMat.SetVector(RevealMaxId, new Vector4(-9999f, -9999f, 0f, 0f));
            mr.sharedMaterial = tileMat;

            _fogTiles[tileId] = go;
            _fogTileMaterials[tileId] = tileMat;
        }

        /// <summary>Quad mesh with oversized bounds so zoom/frustum culling does not pop tiles.</summary>
        private static Mesh BuildFogQuadMesh()
        {
            var mesh = new Mesh { name = "RTG_FogQuad" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
            };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.RecalculateNormals();
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2000f);
            return mesh;
        }

        private void ComputeTileBounds(
            double centerLat, double centerLng,
            out float south, out float west, out float north, out float east)
        {
            double halfLat = (tileSizeM * 0.5) / RtgFogTileMath.LatM;
            double lngM = RtgFogTileMath.LatM * Math.Cos(centerLat * Math.PI / 180.0);
            double halfLng = (tileSizeM * 0.5) / lngM;
            south = (float)(centerLat - halfLat);
            west = (float)(centerLng - halfLng);
            north = (float)(centerLat + halfLat);
            east = (float)(centerLng + halfLng);
        }

        private void RefreshMarkerVisibility()
        {
            if (_markerRoot == null || !_hasFocus) return;

            foreach (RtgMapMarker marker in _markerRoot.GetComponentsInChildren<RtgMapMarker>(true))
            {
                bool inLiveBubble = DistanceMeters(_focusLat, _focusLng, marker.lat, marker.lng) <= liveRevealRadiusM;
                bool visible = inLiveBubble || IsPermanentlyRevealed(marker.lat, marker.lng);
                marker.gameObject.SetActive(visible);

                if (visible && marker.kind == RtgMapMarker.Kind.Resource && _seenMarkers.Add(marker.targetId))
                    BeginShimmer(marker);
            }
        }

        private void ShimmerResourcesInTile(string tileId)
        {
            if (_markerRoot == null) return;
            foreach (RtgMapMarker marker in _markerRoot.GetComponentsInChildren<RtgMapMarker>(true))
            {
                if (marker.kind != RtgMapMarker.Kind.Resource) continue;
                string mTile = RtgFogTileMath.LatLngToTileId(marker.lat, marker.lng, tileSizeM);
                if (mTile == tileId)
                    BeginShimmer(marker);
            }
        }

        private void ShimmerResourceById(string nodeId)
        {
            if (_markerRoot == null || string.IsNullOrEmpty(nodeId)) return;
            foreach (RtgMapMarker marker in _markerRoot.GetComponentsInChildren<RtgMapMarker>(true))
            {
                if (marker.kind == RtgMapMarker.Kind.Resource && marker.targetId == nodeId)
                    BeginShimmer(marker);
            }
        }

        private void BeginShimmer(RtgMapMarker marker)
        {
            if (!_shimmeringResources.Add(marker.targetId)) return;
            RtgResourceShimmer shimmer = marker.GetComponent<RtgResourceShimmer>();
            if (shimmer == null) shimmer = marker.gameObject.AddComponent<RtgResourceShimmer>();
            shimmer.Begin(resourceShimmerDurationMs / 1000f);
        }

        private bool TryGetFocusLatLng(out double lat, out double lng)
        {
#if UNITY_2023_1_OR_NEWER
            RtgPlayerLocation player = UnityEngine.Object.FindFirstObjectByType<RtgPlayerLocation>();
#else
            RtgPlayerLocation player = UnityEngine.Object.FindObjectOfType<RtgPlayerLocation>();
#endif
            if (player != null)
            {
                Transform marker = player.transform.Find("Player Marker");
                if (marker != null)
                {
                    var anchor = marker.GetComponent<CesiumGlobeAnchor>();
                    if (anchor != null)
                    {
                        lat = anchor.latitude;
                        lng = anchor.longitude;
                        return true;
                    }
                }
            }

            lat = _focusLat;
            lng = _focusLng;
            return _hasFocus;
        }

        private static double DistanceMeters(double lat1, double lng1, double lat2, double lng2)
        {
            double avgLatRad = (lat1 + lat2) * 0.5 * Math.PI / 180.0;
            double metersPerDegLng = RtgFogTileMath.LatM * Math.Cos(avgLatRad);
            double dLat = (lat2 - lat1) * RtgFogTileMath.LatM;
            double dLng = (lng2 - lng1) * metersPerDegLng;
            return Math.Sqrt(dLat * dLat + dLng * dLng);
        }

        private void EnsureFogMaterial()
        {
            if (_fogMaterial != null) return;

            Shader shader = Shader.Find("RoutesToGlory/FogOfWarOverlay");
            if (shader == null)
            {
                Debug.LogError("[RTG] FogOfWarOverlay shader not found.");
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }

            _fogMaterial = new Material(shader) { name = "RTG_FogOfWar" };
            _fogMaterial.SetColor("_FogColor", new Color(0.0588f, 0.0902f, 0.1647f, 1f));
            _fogMaterial.SetFloat("_Opacity", unexploredOpacity);
            _fogMaterial.SetFloat("_NoiseScale", 0.015f);
            _fogMaterial.SetFloat("_PulseSpeed", 0.35f);
            _fogMaterial.SetFloat("_EdgeShimmer", 0f);
        }

        public void ClearFog()
        {
            foreach (GameObject go in _fogTiles.Values)
                if (go != null) Destroy(go);
            foreach (Material mat in _fogTileMaterials.Values)
                if (mat != null) Destroy(mat);
            _fogTiles.Clear();
            _fogTileMaterials.Clear();
            _permanentReveal.Clear();
            _ready = false;
            _initializing = false;
            _staticFogSpawned = false;
            _shimmeringResources.Clear();
            _seenMarkers.Clear();
        }

        public IEnumerator ReloadFromApi()
        {
            ClearFog();
            _initializing = true;
            yield return InitializeRoutine();
        }

        [Serializable]
        private class ExplorationResp
        {
            public string worldId;
            public string empireId;
            public FogConfig fogOfWar;
        }

        [Serializable]
        private class FogConfig
        {
            public float tileSizeM;
            public float unexploredOpacity;
            public int resourceShimmerDurationMs;
        }
    }
}
