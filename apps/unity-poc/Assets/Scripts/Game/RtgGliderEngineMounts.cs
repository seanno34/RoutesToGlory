using System.Collections.Generic;
using UnityEngine;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Three exhaust nozzle anchors in hull contract space (+Z nose, +Y up, -Z aft).
    /// </summary>
    public struct RtgGliderEngineMounts
    {
        public Vector3 Main;
        public Vector3 Left;
        public Vector3 Right;

        public RtgGliderEngineMounts(Vector3 main, Vector3 left, Vector3 right)
        {
            Main = main;
            Left = left;
            Right = right;
        }

        public static RtgGliderEngineMounts BlockoutDefaults(float wingspanMeters)
        {
            RtgGliderBlockoutMesh.BuildResult blockout = RtgGliderBlockoutMesh.Build(wingspanMeters);
            return new RtgGliderEngineMounts(
                blockout.MainEngineLocal,
                blockout.LeftEngineLocal,
                blockout.RightEngineLocal);
        }

        /// <summary>
        /// Estimate nozzle positions from imported mesh geometry in hull-local space.
        /// </summary>
        public static bool TryEstimateFromMesh(Transform hullRoot, Transform meshRoot, out RtgGliderEngineMounts mounts)
        {
            mounts = default;
            if (hullRoot == null || meshRoot == null)
                return false;

            var points = new List<Vector3>(4096);
            foreach (MeshFilter meshFilter in meshRoot.GetComponentsInChildren<MeshFilter>())
            {
                Mesh mesh = meshFilter.sharedMesh;
                if (mesh == null)
                    continue;

                Transform meshTransform = meshFilter.transform;
                foreach (Vector3 vertex in mesh.vertices)
                {
                    Vector3 world = meshTransform.TransformPoint(vertex);
                    points.Add(hullRoot.InverseTransformPoint(world));
                }
            }

            if (points.Count < 32)
                return false;

            float minX = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float minY = float.PositiveInfinity;
            float maxY = float.NegativeInfinity;
            float minZ = float.PositiveInfinity;
            float maxZ = float.NegativeInfinity;
            foreach (Vector3 point in points)
            {
                if (point.x < minX) minX = point.x;
                if (point.x > maxX) maxX = point.x;
                if (point.y < minY) minY = point.y;
                if (point.y > maxY) maxY = point.y;
                if (point.z < minZ) minZ = point.z;
                if (point.z > maxZ) maxZ = point.z;
            }

            float depth = Mathf.Max(maxZ - minZ, 0.001f);
            float span = Mathf.Max(maxX - minX, 0.001f);
            float aftCutoff = minZ + depth * 0.22f;
            float centerX = (minX + maxX) * 0.5f;
            float wingBand = span * 0.18f;

            var aftPoints = new List<Vector3>();
            foreach (Vector3 point in points)
            {
                if (point.z <= aftCutoff)
                    aftPoints.Add(point);
            }

            if (aftPoints.Count < 12)
                return false;

            if (!TryClusterAverage(aftPoints, p => p.x < centerX - wingBand, out Vector3 left)
                || !TryClusterAverage(aftPoints, p => p.x > centerX + wingBand, out Vector3 right)
                || !TryClusterAverage(
                    aftPoints,
                    p => Mathf.Abs(p.x - centerX) <= wingBand * 1.1f,
                    out Vector3 main))
            {
                return false;
            }

            mounts = new RtgGliderEngineMounts(main, left, right);
            return true;
        }

        private static bool TryClusterAverage(
            List<Vector3> points,
            System.Predicate<Vector3> predicate,
            out Vector3 average)
        {
            average = Vector3.zero;
            int count = 0;
            float bestZ = float.PositiveInfinity;
            foreach (Vector3 point in points)
            {
                if (!predicate(point))
                    continue;

                count++;
                average += point;
                if (point.z < bestZ)
                    bestZ = point.z;
            }

            if (count < 4)
                return false;

            average /= count;
            average.z = bestZ;
            return true;
        }
    }
}
