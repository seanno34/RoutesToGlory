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

        private const string MarkerContainerName = "Markers";

        // Cache runtime-created emissive materials by color so we don't leak one per marker.
        private readonly Dictionary<Color, Material> _materialCache = new();

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
                    Debug.LogError($"[RTG] Echo Site load failed: {req.responseCode} {req.error} ({url})");
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

            Transform container = ResetContainer();
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
        }

        public void ClearMarkers()
        {
            Transform existing = transform.Find(MarkerContainerName);
            if (existing != null) DestroyObject(existing.gameObject);
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

            GameObject root = CreateMarkerRoot($"Echo Site — {s.name} ({s.tier})", container);
            AddVisual(root.transform, PrimitiveType.Sphere, color, Vector3.one * diameter, Quaternion.identity);
            AddLabel(root.transform, $"{s.name}\n{TierLabel(s.tier)} · {s.alignment}", diameter);
            AnchorAt(root, s.lng, s.lat, groundHeightMeters + settlementFloatHeight);
        }

        private void SpawnResource(RtgResourceNode r, Transform container)
        {
            Color color = ResourceColor(r.resource_id);
            float size = RichnessSize(r.richness);

            GameObject root = CreateMarkerRoot($"Resource — {r.resource_id} ({r.richness})", container);
            // Cube tilted into a "crystal" so resource nodes read differently from sites.
            AddVisual(root.transform, PrimitiveType.Cube, color, Vector3.one * size, Quaternion.Euler(45f, 45f, 0f));
            AddLabel(root.transform, $"{ResourceName(r.resource_id)}\n{r.richness}", size);
            AnchorAt(root, r.lng, r.lat, groundHeightMeters + resourceFloatHeight);
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
            GameObject go = GameObject.CreatePrimitive(shape);
            go.name = "Beacon";

            // No physics on visual-only beacons.
            Collider col = go.GetComponent<Collider>();
            if (col != null) DestroyObject(col);

            go.transform.SetParent(root, false);
            go.transform.localRotation = localRotation;
            go.transform.localScale = scale;

            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = GetEmissiveMaterial(color);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
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
