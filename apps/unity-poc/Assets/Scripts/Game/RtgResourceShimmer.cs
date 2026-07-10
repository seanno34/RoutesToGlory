using UnityEngine;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Pulses emissive glow on a resource beacon for the fog reveal shimmer window
    /// (resourceShimmerDurationMs from server config, default 8 s).
    /// </summary>
    public class RtgResourceShimmer : MonoBehaviour
    {
        private Renderer[] _renderers;
        private Color[] _baseEmission;
        private float _endsAt;
        private float _pulseSpeed = 2.5f;

        public void Begin(float durationSeconds)
        {
            CacheRenderers();
            _endsAt = Time.time + durationSeconds;
            enabled = true;
        }

        private void CacheRenderers()
        {
            if (_renderers != null) return;
            _renderers = GetComponentsInChildren<Renderer>();
            _baseEmission = new Color[_renderers.Length];
            for (int i = 0; i < _renderers.Length; i++)
            {
                Material mat = _renderers[i].material;
                _baseEmission[i] = mat.HasProperty("_EmissionColor")
                    ? mat.GetColor("_EmissionColor")
                    : Color.black;
            }
        }

        private void Update()
        {
            if (Time.time >= _endsAt)
            {
                Restore();
                enabled = false;
                return;
            }

            float pulse = 0.55f + 0.45f * Mathf.Sin(Time.time * _pulseSpeed);
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] == null) continue;
                Material mat = _renderers[i].material;
                if (!mat.HasProperty("_EmissionColor")) continue;
                mat.SetColor("_EmissionColor", _baseEmission[i] * (1f + pulse * 2.5f));
            }
        }

        private void Restore()
        {
            if (_renderers == null) return;
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] == null) continue;
                Material mat = _renderers[i].material;
                if (mat.HasProperty("_EmissionColor"))
                    mat.SetColor("_EmissionColor", _baseEmission[i]);
            }
        }

        private void OnDestroy() => Restore();
    }
}
