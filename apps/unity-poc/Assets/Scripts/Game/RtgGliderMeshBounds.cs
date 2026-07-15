using UnityEngine;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Stable mesh-local bounds from geometry vertices — unaffected by hull bank/pitch or camera.
    /// </summary>
    public static class RtgGliderMeshBounds
    {
        public static bool TryComputeLocalBounds(Transform meshRoot, out Bounds bounds)
        {
            bounds = default;
            if (meshRoot == null)
                return false;

            bool hasPoint = false;
            Vector3 min = Vector3.positiveInfinity;
            Vector3 max = Vector3.negativeInfinity;

            foreach (MeshFilter meshFilter in meshRoot.GetComponentsInChildren<MeshFilter>())
            {
                Mesh mesh = meshFilter.sharedMesh;
                if (mesh == null)
                    continue;

                Transform source = meshFilter.transform;
                foreach (Vector3 vertex in mesh.vertices)
                {
                    Vector3 local = meshRoot.InverseTransformPoint(source.TransformPoint(vertex));
                    min = Vector3.Min(min, local);
                    max = Vector3.Max(max, local);
                    hasPoint = true;
                }
            }

            if (!hasPoint)
                return false;

            bounds = new Bounds((min + max) * 0.5f, max - min);
            return bounds.size.sqrMagnitude > 1e-8f;
        }
    }
}
