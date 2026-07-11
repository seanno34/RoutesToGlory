using System.Collections;
using System.Collections.Generic;
using System.IO;
using CesiumForUnity;
using UnityEngine;
using UnityEngine.Networking;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Loads a world map from @empire/api (or a local sample file) and spawns each
    /// Echo Site (settlement) and resource node as a georeferenced, glowing beacon
    /// under the Cesium georeference.
    ///
    /// Data source is a single switch: <see cref="dataSource"/>. In Play mode the
    /// live path uses UnityWebRequest; the editor "Load Echo Sites" menu command
    /// uses the synchronous sample path so you can preview placement without Play
    /// mode or a running backend. The parsing + spawning code is identical for both,
    /// so flipping to the live API once MySQL is up needs no code changes.
    /// </summary>
    public class RtgEchoSiteLoader : MonoBehaviour
    {
        public enum DataSource { SampleFile, LiveApi }

        [Header("Data source")]
        public DataSource dataSource = DataSource.SampleFile;

        [Tooltip("Base URL of @empire/api, e.g. http://localhost:3001/api")]
        public string apiBaseUrl = "http://localhost:3001/api";

        [Tooltip("World id (UUID) to load when dataSource = LiveApi")]
        public string worldId = "";

        [Tooltip("Empire id (UUID) for the player; used by route sessions. Set by '6b. Connect Echo Sites to Live API'.")]
        public string empireId = "";

        [Tooltip("File under Assets/StreamingAssets/ to load when dataSource = SampleFile")]
        public string sampleFileName = "sample-world-map.json";

        [Header("Placement")]
        [Tooltip("Approx. ground height (m above ellipsoid) for the POC area near Douglas, WY.")]
        public double groundHeightMeters = 1476.0;

        [Tooltip("Meters a settlement beacon floats above the ground.")]
        public float settlementFloatHeight = 120f;

        [Tooltip("Meters a resource beacon floats above the ground.")]
        public float resourceFloatHeight = 70f;

        [Tooltip("Multiplier from beacon size to label text size. Raise/lower if labels read too big or small.")]
        public float labelSizeFactor = 0.06f;

        [Tooltip("Label foreground color (a black outline is drawn behind it for contrast).")]
        public Color labelColor = new Color(0.90f, 0.98f, 1.00f); // pale alien cyan-white

        [Tooltip("Load automatically when entering Play mode.")]
        public bool loadOnPlay = true;

        [Header("Tap-to-connect test layout")]
        [Tooltip("Offset nearby markers off the tour corridor so you can test tap-claim (near) and reject (far).")]
        public bool scatterForTapTest = true;

        [Tooltip("Only scatter items within this radius of the play center (Douglas). Far metro sites stay put.")]
        public float scatterRadiusMeters = 8000f;

        public double scatterCenterLat = 42.7597;
        public double scatterCenterLng = -105.3819;

        [Tooltip("East offset (m) for 'near' markers — should be within minConnectDistanceM of the corridor.")]
        public float nearTapOffsetM = 450f;

        [Tooltip("East offset (m) for 'far' markers — should exceed minConnectDistanceM from the corridor.")]
        public float farTapOffsetM = 1800f;

        [Tooltip("Goodie huts are pinned on the corridor tour north leg (matches RtgPlayerLocation.CorridorTourLoop).")]
        public float goodieHutCorridorFraction = 0.55f;

        private const string MarkerContainerName = "Markers";
        private int _scatterIndex;
        private string _corridorGoodieId;

        private const double CorridorDLat = 0.012;

        /// <summary>
        /// The most recently loaded/parsed world map, or null before the first load.
        /// Exposed so other systems (e.g. the player's "tour nearby sites" route) can
        /// reuse the same data without re-fetching.
        /// </summary>
        public RtgWorldMap LastMap { get; private set; }

        // Cache runtime-created emissive materials by color so we don't leak one per marker.
        private readonly Dictionary<Color, Material> _materialCache = new();

        private void Awake()
        {
            RtgDevWorldConfig.TryApplyTo(this);
        }

        private void Start()
        {
            if (Application.isPlaying && loadOnPlay)
            {
                StartCoroutine(LoadRoutine());
            }
        }

        /// <summary>Editor entry point: loads the sample file synchronously and spawns markers.</summary>
        public void LoadSampleImmediate()
        {
            string json = ReadSampleFile();
            if (string.IsNullOrEmpty(json)) return;
            SpawnAll(Parse(json));
        }

        private IEnumerator LoadRoutine()
        {
            yield return FetchAndSpawn();
        }

        /// <summary>Re-fetch the live map and respawn markers (e.g. after founding a goodie hut).</summary>
        public IEnumerator ReloadFromApi()
        {
            if (dataSource != DataSource.LiveApi) yield break;
            yield return FetchAndSpawn();
        }

        private IEnumerator FetchAndSpawn()
        {
            string json = null;

            if (dataSource == DataSource.SampleFile)
            {
                json = ReadSampleFile();
            }
            else
            {
                string url = $"{apiBaseUrl.TrimEnd('/')}/worlds/{worldId}/map";
                using UnityWebRequest req = UnityWebRequest.Get(url);
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError(
                        $"[RTG] Echo Site load failed: {req.responseCode} {req.error} ({url}). " +
                        "Is @empire/api running (pnpm --filter @empire/api dev)? " +
                        "On iPhone, apiBaseUrl must be your Mac LAN IP, not localhost. " +
                        "Check rtg-dev-world.json / RTG Echo Sites in the scene.");
                    yield break;
                }
                json = req.downloadHandler.text;
            }

            if (!string.IsNullOrEmpty(json)) SpawnAll(Parse(json));
        }

        private string ReadSampleFile()
        {
            string path = Path.Combine(Application.streamingAssetsPath, sampleFileName);
            if (!File.Exists(path))
            {
                Debug.LogError($"[RTG] Sample world map not found at {path}");
                return null;
            }
            return File.ReadAllText(path);
        }

        private static RtgWorldMap Parse(string json)
        {
            RtgWorldMap map = JsonUtility.FromJson<RtgWorldMap>(json);
            if (map == null) Debug.LogError("[RTG] Failed to parse world map JSON.");
            return map;
        }

        // ------------------------------------------------------------------ //
        // Spawning
        // ------------------------------------------------------------------ //

        public void SpawnAll(RtgWorldMap map)
        {
            if (map == null) return;

            LastMap = map;
            Transform container = ResetContainer();
            _scatterIndex = 0;
            _corridorGoodieId = SelectCorridorGoodieTarget(map);
            int settlements = 0, resources = 0;

            if (map.settlements != null)
            {
                foreach (RtgSettlement s in map.settlements)
                {
                    SpawnSettlement(s, container);
                    settlements++;
                }
            }

            if (map.resources != null)
            {
                foreach (RtgResourceNode r in map.resources)
                {
                    SpawnResource(r, container);
                    resources++;
                }
            }

            Debug.Log($"[RTG] Spawned {settlements} Echo Site(s) and {resources} resource node(s).");
            DrawPersistedRoutes(map);
            RtgMapConnections.Apply(map, empireId);
            SetupFogOfWar(container, map?.routes);
        }

        private void SetupFogOfWar(Transform markersContainer, RtgRoute[] routes = null)
        {
            if (!Application.isPlaying) return;
            RtgFogOfWar fog = RtgFogOfWar.Ensure(this);
            if (fog != null) fog.OnMapSpawned(markersContainer, routes);
        }

        private void DrawPersistedRoutes(RtgWorldMap map)
        {
            RtgPersistedRouteDrawer drawer = RtgPersistedRouteDrawer.FindOrCreate();
            if (drawer == null) return;
            drawer.DrawAll(map?.routes);
        }

        public void ClearMarkers()
        {
            Transform existing = transform.Find(MarkerContainerName);
            if (existing != null) DestroyObject(existing.gameObject);
            RtgPersistedRouteDrawer drawer = RtgPersistedRouteDrawer.FindOrCreate();
            drawer?.Clear();
        }

        private Transform ResetContainer()
        {
            ClearMarkers();
            var go = new GameObject(MarkerContainerName);
            go.transform.SetParent(transform, false);
            return go.transform;
        }

        private void SpawnSettlement(RtgSettlement s, Transform container)
        {
            Color color = AlignmentColor(s.alignment, s.is_goodie_hut != 0);
            float diameter = TierDiameter(s.tier);

            double lat = s.lat, lng = s.lng;
            bool isGoodieHut = s.is_goodie_hut != 0 || s.tier == "goodie_hut";
            bool pinOnCorridor = isGoodieHut && s.id == _corridorGoodieId;
            string tapTag = ApplyTapTestScatter(ref lat, ref lng, pinOnCorridor, s.id);

            GameObject root = CreateMarkerRoot($"Echo Site — {s.name} ({s.tier})", container);
            AddVisual(root.transform, PrimitiveType.Sphere, color, Vector3.one * diameter, Quaternion.identity);
            AddLabel(root.transform, $"{s.name}\n{TierLabel(s.tier)} · {s.alignment}{tapTag}", diameter);
            AnchorAt(root, lng, lat, groundHeightMeters + settlementFloatHeight);
            root.AddComponent<RtgMapMarker>().Configure(
                RtgMapMarker.Kind.Settlement, s.id, s.name, s.tier, lat, lng);
        }

        private void SpawnResource(RtgResourceNode r, Transform container)
        {
            Color color = ResourceColor(r.resource_id);
            float size = RichnessSize(r.richness);

            double lat = r.lat, lng = r.lng;
            string tapTag = ApplyTapTestScatter(ref lat, ref lng, false, r.id);

            GameObject root = CreateMarkerRoot($"Resource — {r.resource_id} ({r.richness})", container);
            AddVisual(root.transform, PrimitiveType.Cube, color, Vector3.one * size, Quaternion.Euler(45f, 45f, 0f));
            AddLabel(root.transform, $"{ResourceName(r.resource_id)}\n{r.richness}{tapTag}", size);
            AnchorAt(root, lng, lat, groundHeightMeters + resourceFloatHeight);
            root.AddComponent<RtgMapMarker>().Configure(
                RtgMapMarker.Kind.Resource, r.id, ResourceName(r.resource_id), r.richness, lat, lng);
        }

        private string SelectCorridorGoodieTarget(RtgWorldMap map)
        {
            if (map?.settlements == null) return null;

            string bestId = null;
            double bestDist = double.MaxValue;
            foreach (RtgSettlement s in map.settlements)
            {
                if (s == null || (s.is_goodie_hut == 0 && s.tier != "goodie_hut")) continue;
                double dist = HaversineM(scatterCenterLat, scatterCenterLng, s.lat, s.lng);
                if (dist > scatterRadiusMeters || dist >= bestDist) continue;
                bestDist = dist;
                bestId = s.id;
            }
            return bestId;
        }

        /// <summary>
        /// Cycles markers into on-corridor / near / far buckets for tap-to-connect testing.
        /// The nearest Douglas goodie hut is pinned on the simulated tour's north corridor leg.
        /// Returns a short label suffix.
        /// </summary>
        private string ApplyTapTestScatter(
            ref double lat, ref double lng, bool isGoodieHut = false, string stableId = null)
        {
            if (isGoodieHut)
            {
                lat = scatterCenterLat + CorridorDLat * goodieHutCorridorFraction;
                lng = scatterCenterLng;
                return scatterForTapTest ? "\n◎ goodie hut · on route" : "";
            }

            if (!scatterForTapTest) return "";

            double dist = HaversineM(scatterCenterLat, scatterCenterLng, lat, lng);
            if (dist > scatterRadiusMeters)
                return "";

            // Stable bucket per marker so reload/claim does not reshuffle positions.
            int bucket = 0;
            if (!string.IsNullOrEmpty(stableId))
                bucket = Mathf.Abs(stableId.GetHashCode()) % 3;

            double eastM = bucket switch
            {
                1 => nearTapOffsetM,
                2 => farTapOffsetM,
                _ => 0,
            };

            if (eastM > 0)
            {
                double lngM = 111_320.0 * System.Math.Cos(lat * System.Math.PI / 180.0);
                lng += eastM / lngM;
            }

            return bucket switch
            {
                0 => "\n◎ tap: map",
                1 => "\n◎ tap: near",
                2 => "\n✕ tap: far",
                _ => "",
            };
        }

        private static double HaversineM(double lat1, double lng1, double lat2, double lng2)
        {
            const double R = 6_371_000;
            double ToRad(double d) => d * System.Math.PI / 180.0;
            double dLat = ToRad(lat2 - lat1);
            double dLng = ToRad(lng2 - lng1);
            double a = System.Math.Sin(dLat / 2) * System.Math.Sin(dLat / 2) +
                       System.Math.Cos(ToRad(lat1)) * System.Math.Cos(ToRad(lat2)) *
                       System.Math.Sin(dLng / 2) * System.Math.Sin(dLng / 2);
            return 2 * R * System.Math.Asin(System.Math.Sqrt(a));
        }

        // ------------------------------------------------------------------ //
        // Helpers
        // ------------------------------------------------------------------ //

        // Each marker is an unscaled root (which carries the CesiumGlobeAnchor) with
        // two children: the scaled beacon mesh and a billboarded text label. Keeping
        // the root at scale 1 means the label's text size is independent of the
        // (large) beacon scale.
        private static GameObject CreateMarkerRoot(string name, Transform parent)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            return root;
        }

        private void AddVisual(
            Transform root, PrimitiveType shape, Color color, Vector3 scale, Quaternion localRotation)
        {
            Mesh mesh = shape == PrimitiveType.Cube ? RtgMeshPrimitives.Cube : RtgMeshPrimitives.Sphere;
            GameObject go = RtgMeshPrimitives.CreateMeshObject(
                "Beacon", mesh, GetEmissiveMaterial(color), root);
            go.transform.localRotation = localRotation;
            go.transform.localScale = scale;
        }

        // Outline offsets (8-way) for the faux text outline. Legacy TextMesh has no
        // outline, so we draw black copies around a lighter main copy.
        private static readonly Vector2[] OutlineDirections =
        {
            new Vector2(1f, 0f), new Vector2(-1f, 0f), new Vector2(0f, 1f), new Vector2(0f, -1f),
            new Vector2(1f, 1f), new Vector2(1f, -1f), new Vector2(-1f, 1f), new Vector2(-1f, -1f),
        };

        private void AddLabel(Transform root, string text, float beaconSize)
        {
            var pivot = new GameObject("Label");
            pivot.transform.SetParent(root, false);
            pivot.transform.localPosition = new Vector3(0f, beaconSize * 0.9f, 0f);
            pivot.AddComponent<RtgBillboard>();

            float charSize = Mathf.Max(1f, beaconSize * labelSizeFactor);
            float outline = charSize * 0.08f;

            // Black outline copies at z = 0, offset in the label plane.
            foreach (Vector2 dir in OutlineDirections)
            {
                Vector2 offset = dir.normalized * outline;
                CreateTextMesh(pivot.transform, text, charSize, Color.black,
                    new Vector3(offset.x, offset.y, 0f));
            }

            // Main text pulled slightly toward the camera (local -z) so the transparent
            // sort draws it on top of the outline copies.
            CreateTextMesh(pivot.transform, text, charSize, labelColor,
                new Vector3(0f, 0f, -charSize * 0.5f));
        }

        private static TextMesh CreateTextMesh(
            Transform parent, string text, float charSize, Color color, Vector3 localPosition)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var tm = go.AddComponent<TextMesh>();
            tm.font = font;
            tm.text = text;
            tm.anchor = TextAnchor.LowerCenter;
            tm.alignment = TextAlignment.Center;
            tm.fontSize = 64;
            tm.characterSize = charSize;
            tm.color = color;

            var mr = go.GetComponent<MeshRenderer>();
            if (font != null) mr.sharedMaterial = font.material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            return tm;
        }

        /// <summary>Adds a CesiumGlobeAnchor and positions the marker at lon/lat/height.</summary>
        private static void AnchorAt(GameObject go, double lon, double lat, double height)
        {
            CesiumGlobeAnchor anchor = go.GetComponent<CesiumGlobeAnchor>();
            if (anchor == null) anchor = go.AddComponent<CesiumGlobeAnchor>();
            anchor.SetPositionLongitudeLatitudeHeight(lon, lat, height);
        }

        private Material GetEmissiveMaterial(Color color)
        {
            if (_materialCache.TryGetValue(color, out Material cached) && cached != null)
                return cached;

            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"))
            {
                name = $"RTG_Beacon_{ColorUtility.ToHtmlStringRGB(color)}"
            };
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", 0.6f);
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", color * 2.2f); // glow
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

            _materialCache[color] = mat;
            return mat;
        }

        private static Color AlignmentColor(string alignment, bool isGoodieHut)
        {
            if (isGoodieHut) return new Color(1.00f, 0.82f, 0.25f);   // gold
            switch (alignment)
            {
                case "friendly":      return new Color(0.35f, 1.00f, 0.55f); // green
                case "hostile":       return new Color(1.00f, 0.35f, 0.28f); // red-orange
                case "alien_enclave": return new Color(0.85f, 0.30f, 1.00f); // magenta
                default:              return new Color(0.35f, 0.85f, 1.00f); // neutral cyan
            }
        }

        private static float TierDiameter(string tier)
        {
            switch (tier)
            {
                case "super_city": return 200f;
                case "city":       return 150f;
                case "town":       return 110f;
                case "settlement": return 80f;
                case "goodie_hut": return 60f;
                default:           return 80f;
            }
        }

        private static float RichnessSize(string richness)
        {
            switch (richness)
            {
                case "rich":     return 90f;
                case "moderate": return 60f;
                case "sparse":   return 40f;
                default:         return 55f;
            }
        }

        private static Color ResourceColor(string resourceId)
        {
            switch (resourceId)
            {
                case "xenite":         return new Color(0.40f, 1.00f, 0.50f);
                case "solari_dust":    return new Color(1.00f, 0.85f, 0.30f);
                case "ferracite":      return new Color(1.00f, 0.55f, 0.25f);
                case "lumin_spring":   return new Color(0.40f, 0.95f, 1.00f);
                case "quantium_shard": return new Color(0.65f, 0.45f, 1.00f);
                case "voidglass":      return new Color(0.70f, 0.80f, 1.00f);
                case "mycelium_core":  return new Color(0.70f, 1.00f, 0.35f);
                case "chrono_moss":    return new Color(0.35f, 0.90f, 0.75f);
                case "aegis_bark":     return new Color(0.70f, 0.60f, 0.35f);
                case "nebula_pearl":   return new Color(1.00f, 0.55f, 0.85f);
                default:               return new Color(0.85f, 0.85f, 0.85f);
            }
        }

        private static string TierLabel(string tier)
        {
            switch (tier)
            {
                case "super_city": return "Super City";
                case "city":       return "City";
                case "town":       return "Town";
                case "settlement": return "Settlement";
                case "goodie_hut": return "Goodie Hut";
                default:           return tier;
            }
        }

        private static string ResourceName(string resourceId)
        {
            switch (resourceId)
            {
                case "xenite":         return "Xenite";
                case "solari_dust":    return "Solari Dust";
                case "ferracite":      return "Ferracite";
                case "lumin_spring":   return "Lumin Spring";
                case "quantium_shard": return "Quantium Shard";
                case "voidglass":      return "Voidglass";
                case "mycelium_core":  return "Mycelium Core";
                case "chrono_moss":    return "Chrono Moss";
                case "aegis_bark":     return "Aegis Bark";
                case "nebula_pearl":   return "Nebula Pearl";
                default:               return resourceId;
            }
        }

        private static void DestroyObject(Object obj)
        {
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }
    }
}
