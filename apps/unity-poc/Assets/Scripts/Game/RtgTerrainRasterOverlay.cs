using System.Collections.Generic;
using CesiumForUnity;
using UnityEngine;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// DEPRECATED — Earth raster tiles are not the art path.
    /// Use <see cref="RtgTerrainMaterialController"/> + AlienTerrainBiome.shader instead.
    /// See docs/CESIUM_ALIEN_WORLD_ARCHITECTURE.md.
    /// </summary>
    [System.Obsolete("Use RtgTerrainMaterialController — Earth raster overlays are deprecated.")]
    [DisallowMultipleComponent]
    public class RtgTerrainRasterOverlay : MonoBehaviour
    {
        private const string TileUserAgent =
            "RoutesToGlory-Unity-POC/1.0 (+https://github.com/routestoglory; terrain-tiles@dev.local)";

        private const string EsriWorldTopoUrl =
            "https://services.arcgisonline.com/ArcGIS/rest/services/World_Topo_Map/MapServer/tile/{z}/{reverseY}/{x}";

        public enum TileProvider
        {
            /// <summary>
            /// Proxy via @empire/api — avoids Cesium curl HTTP/2 errors against public CDNs (recommended).
            /// </summary>
            EmpireApiProxy,
            /// <summary>Esri World Topo — direct; uses {reverseY} for TMS row order.</summary>
            EsriWorldTopo,
            /// <summary>Carto Voyager — direct; may hit curl HTTP/2 PROTOCOL_ERROR on some hosts.</summary>
            CartoVoyager,
            /// <summary>Topographic shading + landcover — requires User-Agent header.</summary>
            OpenTopoMap,
            /// <summary>Standard OSM — requires User-Agent; light dev use only.</summary>
            OpenStreetMap,
            /// <summary>Stadia Stamen terrain — set apiKey (free tier at stadiamaps.com).</summary>
            StadiaStamenTerrain,
            /// <summary>Use <see cref="customTemplateUrl"/>.</summary>
            Custom,
        }

        [Header("Target")]
        [Tooltip("Cesium World Terrain tileset on this GameObject. Auto-filled if empty.")]
        public Cesium3DTileset terrainTileset;

        [Header("Tile source")]
        public TileProvider provider = TileProvider.EmpireApiProxy;

        [Tooltip("Base URL of @empire/api for EmpireApiProxy. Auto-filled from RTG Echo Sites if empty.")]
        public string apiBaseUrl = "";

        [Tooltip("Used when provider = Custom. Supports {z} {x} {y} and {reverseY} (Web Mercator).")]
        public string customTemplateUrl = EsriWorldTopoUrl;

        [Tooltip("Stadia Maps API key — only for StadiaStamenTerrain.")]
        public string stadiaApiKey = "";

        [Header("Zoom")]
        [Tooltip("Minimum tile zoom streamed to the device.")]
        public int minimumLevel = 10;

        [Tooltip("Maximum tile zoom — 16–17 reads well at glider pass-over.")]
        public int maximumLevel = 17;

        [Header("Load tuning")]
        [Tooltip("Lower concurrent tile fetches reduces curl HTTP/2 stream errors.")]
        public bool reduceConcurrentTileLoads = true;

        [Tooltip("Cap when reduceConcurrentTileLoads is enabled.")]
        public uint maxSimultaneousTileLoads = 8;

        [Header("Material")]
        [Tooltip("Remove RTG_AlienTerrain teal tint so raster imagery is visible.")]
        public bool clearOpaqueMaterialTint = true;

        [Tooltip("Re-apply overlay settings when entering Play mode (editor tweaks).")]
        public bool applyOnStart = true;

        private CesiumUrlTemplateRasterOverlay _overlay;

        private void Awake()
        {
            if (terrainTileset == null)
                terrainTileset = GetComponent<Cesium3DTileset>();
            SyncApiBaseFromLoader();
        }

        private void Start()
        {
            if (applyOnStart)
                Apply();
        }

        /// <summary>Wire or refresh the raster overlay (safe to call from dev menus).</summary>
        public void Apply()
        {
            if (terrainTileset == null)
            {
                Debug.LogError("[RTG] Terrain raster overlay — no Cesium3DTileset found.");
                return;
            }

            SyncApiBaseFromLoader();

            if (clearOpaqueMaterialTint)
                terrainTileset.opaqueMaterial = null;

            if (reduceConcurrentTileLoads)
                terrainTileset.maximumSimultaneousTileLoads = maxSimultaneousTileLoads;

            _overlay = GetComponent<CesiumUrlTemplateRasterOverlay>();
            if (_overlay == null)
                _overlay = gameObject.AddComponent<CesiumUrlTemplateRasterOverlay>();

            string url = ResolveTemplateUrl();
            if (string.IsNullOrWhiteSpace(url))
            {
                Debug.LogError("[RTG] Terrain raster overlay — template URL is empty.");
                return;
            }

            _overlay.projection = CesiumUrlTemplateRasterOverlayProjection.WebMercator;
            _overlay.minimumLevel = Mathf.Max(0, minimumLevel);
            _overlay.maximumLevel = Mathf.Max(_overlay.minimumLevel, maximumLevel);
            _overlay.tileWidth = 256;
            _overlay.tileHeight = 256;
            _overlay.requestHeaders = BuildRequestHeaders(provider);
            _overlay.templateUrl = url;

            Debug.Log(
                $"[RTG] Terrain raster overlay active — {provider}, z{_overlay.minimumLevel}–{_overlay.maximumLevel}\n" +
                $"  URL: {url}");
        }

        private void SyncApiBaseFromLoader()
        {
            if (!string.IsNullOrWhiteSpace(apiBaseUrl)) return;

#if UNITY_2023_1_OR_NEWER
            RtgEchoSiteLoader loader = Object.FindFirstObjectByType<RtgEchoSiteLoader>();
#else
            RtgEchoSiteLoader loader = Object.FindObjectOfType<RtgEchoSiteLoader>();
#endif
            if (loader != null && !string.IsNullOrWhiteSpace(loader.apiBaseUrl))
                apiBaseUrl = loader.apiBaseUrl.Trim();
        }

        private static List<CesiumUrlTemplateRasterOverlay.HeaderEntry> BuildRequestHeaders(
            TileProvider provider)
        {
            var headers = new List<CesiumUrlTemplateRasterOverlay.HeaderEntry>
            {
                new CesiumUrlTemplateRasterOverlay.HeaderEntry
                {
                    Name = "User-Agent",
                    Value = TileUserAgent,
                },
                new CesiumUrlTemplateRasterOverlay.HeaderEntry
                {
                    Name = "Connection",
                    Value = "close",
                },
            };

            // Local API proxy uses plain HTTP/1.1 from our server — skip extra CDN headers.
            if (provider == TileProvider.EmpireApiProxy)
                return headers;

            headers.Add(new CesiumUrlTemplateRasterOverlay.HeaderEntry
            {
                Name = "Accept",
                Value = "image/png,image/*,*/*",
            });

            return headers;
        }

        private string ResolveTemplateUrl()
        {
            switch (provider)
            {
                case TileProvider.EmpireApiProxy:
                    string baseUrl = apiBaseUrl?.Trim();
                    if (string.IsNullOrWhiteSpace(baseUrl))
                    {
                        Debug.LogWarning(
                            "[RTG] EmpireApiProxy needs apiBaseUrl — falling back to Esri World Topo.");
                        return EsriWorldTopoUrl;
                    }
                    return $"{baseUrl.TrimEnd('/')}/tiles/terrain/{{z}}/{{x}}/{{y}}.png";
                case TileProvider.EsriWorldTopo:
                    return EsriWorldTopoUrl;
                case TileProvider.CartoVoyager:
                    return "https://a.basemaps.cartocdn.com/rastertiles/voyager/{z}/{x}/{y}.png";
                case TileProvider.OpenTopoMap:
                    return "https://a.tile.opentopomap.org/{z}/{x}/{y}.png";
                case TileProvider.OpenStreetMap:
                    return "https://tile.openstreetmap.org/{z}/{x}/{y}.png";
                case TileProvider.StadiaStamenTerrain:
                    if (string.IsNullOrWhiteSpace(stadiaApiKey))
                    {
                        Debug.LogWarning(
                            "[RTG] Stadia terrain selected but stadiaApiKey is empty — falling back to Esri.");
                        return EsriWorldTopoUrl;
                    }
                    return "https://tiles.stadiamaps.com/tiles/stamen_terrain_background/{z}/{x}/{y}.png?api_key=" +
                           stadiaApiKey.Trim();
                case TileProvider.Custom:
                    return customTemplateUrl?.Trim();
                default:
                    return null;
            }
        }
    }
}
