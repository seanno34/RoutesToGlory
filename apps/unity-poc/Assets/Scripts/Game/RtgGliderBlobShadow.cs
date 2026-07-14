using UnityEngine;

namespace RoutesToGlory.Game
{
    /// <summary>Soft ellipse blob shadow under the glider — no textured quad footprint.</summary>
    public class RtgGliderBlobShadow : MonoBehaviour
    {
        public float widthMeters = 16f;
        public float lengthMeters = 20f;
        public float heightOffsetMeters = 0.04f;
        public Vector3 localOffsetMeters = new Vector3(0.2f, 0f, -1.2f);
        public Color shadowColor = new Color(0.02f, 0.05f, 0.12f, 0.4f);

        private MeshRenderer _renderer;
        private Material _material;

        public void Configure(float shipSizeMeters)
        {
            widthMeters = shipSizeMeters * 0.72f;
            lengthMeters = shipSizeMeters * 0.88f;
            EnsureShadow();
            ApplyPlacement();
        }

        public void ApplyPlacement()
        {
            transform.localPosition = new Vector3(
                localOffsetMeters.x,
                heightOffsetMeters,
                localOffsetMeters.z);
            transform.localRotation = Quaternion.identity;
            transform.localScale = new Vector3(widthMeters, 1f, lengthMeters);
        }

        private void EnsureShadow()
        {
            if (_renderer != null) return;

            var filter = GetComponent<MeshFilter>();
            if (filter == null) filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = RtgMeshPrimitives.GroundQuad;

            _renderer = GetComponent<MeshRenderer>();
            if (_renderer == null) _renderer = gameObject.AddComponent<MeshRenderer>();
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;

            Material template = Resources.Load<Material>("RTG_PlayerShip/GliderBlobShadow");
            if (template != null && template.shader != null && template.shader.isSupported)
            {
                _material = new Material(template) { name = "RTG_GliderBlobShadow_Runtime" };
                if (_material.HasProperty("_ShadowColor"))
                    _material.SetColor("_ShadowColor", shadowColor);
                _renderer.sharedMaterial = _material;
                return;
            }

            Shader shader = Shader.Find("RTG/GliderBlobShadow");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) return;

            _material = new Material(shader) { name = "RTG_GliderBlobShadow_Runtime" };
            if (_material.HasProperty("_ShadowColor"))
                _material.SetColor("_ShadowColor", shadowColor);
            else if (_material.HasProperty("_BaseColor"))
                _material.SetColor("_BaseColor", shadowColor);
            _renderer.sharedMaterial = _material;
        }

        private void OnDestroy()
        {
            if (_material == null) return;
            if (Application.isPlaying) Destroy(_material);
            else DestroyImmediate(_material);
        }
    }
}
