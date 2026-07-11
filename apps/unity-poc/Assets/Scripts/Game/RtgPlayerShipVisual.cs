using UnityEngine;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Top-down player ship stamped flat on the map plane. Uses the same sprite in
    /// Map and Route views — the painted shading reads as 3D from most camera angles.
    /// </summary>
    public class RtgPlayerShipVisual : MonoBehaviour
    {
        private const string MaterialResourcePath = "RTG_PlayerShip/PlayerShip";

        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private static Material _templateMaterial;

        [Tooltip("Top-down sprite (glider_01). Nose should point toward +Z.")]
        public Texture2D texture;

        [Tooltip("Wingspan in meters on the ground plane.")]
        public float sizeMeters = 24f;

        [Tooltip("Heading offset if the nose points backward (180 = flip).")]
        public float headingOffsetDegrees;

        [Tooltip("Lift above the marker anchor to avoid z-fighting with terrain.")]
        public float groundClearanceMeters = 1f;

        private MeshRenderer _renderer;
        private Material _material;

        public bool IsReady => _renderer != null && _material != null && texture != null;

        public void Configure(Texture2D tex, float sizeM, float headingOffsetDeg = 0f)
        {
            texture = tex;
            sizeMeters = sizeM;
            headingOffsetDegrees = headingOffsetDeg;
            Rebuild();
        }

        public void SetHeadingRadians(float headingRad)
        {
            float yaw = headingRad + headingOffsetDegrees * Mathf.Deg2Rad;
            Vector3 forward = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
            if (forward.sqrMagnitude < 1e-6f) return;

            transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        private void Rebuild()
        {
            if (texture == null) return;

            EnsureMeshChild();
            ApplyScale();
            if (!ApplyMaterial())
                return;

            transform.localPosition = new Vector3(0f, groundClearanceMeters, 0f);
        }

        private void EnsureMeshChild()
        {
            Transform meshRoot = transform.Find("Mesh");
            if (meshRoot != null)
            {
                if (Application.isPlaying) Destroy(meshRoot.gameObject);
                else DestroyImmediate(meshRoot.gameObject);
            }

            var go = new GameObject("Mesh");
            go.transform.SetParent(transform, false);

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = RtgMeshPrimitives.GroundQuad;

            _renderer = go.AddComponent<MeshRenderer>();
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
        }

        private void ApplyScale()
        {
            Transform meshRoot = transform.Find("Mesh");
            if (meshRoot == null) return;

            float aspect = texture.height > 0 ? (float)texture.width / texture.height : 1f;
            meshRoot.localScale = new Vector3(sizeMeters * aspect, 1f, sizeMeters);
        }

        private bool ApplyMaterial()
        {
            if (_renderer == null) return false;

            Material template = LoadTemplateMaterial();
            if (template == null || template.shader == null)
            {
                Debug.LogError(
                    "[RTG] PlayerShip material missing. Expected Resources/" +
                    MaterialResourcePath + ".mat in the build.");
                return false;
            }

            if (_material == null)
                _material = new Material(template) { name = "RTG_PlayerShip_Runtime" };

            _material.SetTexture(MainTexId, texture);
            _material.SetColor(ColorId, Color.white);
            _renderer.sharedMaterial = _material;
            return true;
        }

        private static Material LoadTemplateMaterial()
        {
            if (_templateMaterial != null) return _templateMaterial;
            _templateMaterial = Resources.Load<Material>(MaterialResourcePath);
            return _templateMaterial;
        }

        private void OnDestroy()
        {
            if (_material == null) return;
            if (Application.isPlaying) Destroy(_material);
            else DestroyImmediate(_material);
        }
    }
}
