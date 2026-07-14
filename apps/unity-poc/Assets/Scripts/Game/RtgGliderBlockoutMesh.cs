using System.Collections.Generic;
using UnityEngine;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Procedural low-poly blockout mesh for the player glider (Phase A 3D proof).
    /// Normalized space: +Z nose, +Y up, wings along X. Scale with <see cref="sizeMeters"/>.
    /// </summary>
    public static class RtgGliderBlockoutMesh
    {
        public readonly struct BuildResult
        {
            public readonly Mesh Mesh;
            public readonly Vector3 MainEngineLocal;
            public readonly Vector3 LeftEngineLocal;
            public readonly Vector3 RightEngineLocal;

            public BuildResult(
                Mesh mesh,
                Vector3 mainEngineLocal,
                Vector3 leftEngineLocal,
                Vector3 rightEngineLocal)
            {
                Mesh = mesh;
                MainEngineLocal = mainEngineLocal;
                LeftEngineLocal = leftEngineLocal;
                RightEngineLocal = rightEngineLocal;
            }
        }

        public static BuildResult Build(float sizeMeters)
        {
            float s = Mathf.Max(6f, sizeMeters);
            var vertices = new List<Vector3>();
            var colors = new List<Color>();
            var triangles = new List<int>();

            Color hull = new Color(0.9f, 0.92f, 0.96f);
            Color accent = new Color(0.82f, 0.16f, 0.2f);
            Color cockpit = new Color(0.35f, 0.82f, 0.95f);
            Color engine = new Color(0.28f, 0.3f, 0.36f);

            void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Color color)
            {
                int i = vertices.Count;
                vertices.Add(a * s);
                vertices.Add(b * s);
                vertices.Add(c * s);
                colors.Add(color);
                colors.Add(color);
                colors.Add(color);
                triangles.Add(i);
                triangles.Add(i + 1);
                triangles.Add(i + 2);
            }

            void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
            {
                AddTriangle(a, b, c, color);
                AddTriangle(a, c, d, color);
            }

            void AddBox(Vector3 center, Vector3 size, Color color)
            {
                Vector3 h = size * 0.5f;
                Vector3 p0 = center + new Vector3(-h.x, -h.y, -h.z);
                Vector3 p1 = center + new Vector3(h.x, -h.y, -h.z);
                Vector3 p2 = center + new Vector3(h.x, -h.y, h.z);
                Vector3 p3 = center + new Vector3(-h.x, -h.y, h.z);
                Vector3 p4 = center + new Vector3(-h.x, h.y, -h.z);
                Vector3 p5 = center + new Vector3(h.x, h.y, -h.z);
                Vector3 p6 = center + new Vector3(h.x, h.y, h.z);
                Vector3 p7 = center + new Vector3(-h.x, h.y, h.z);

                AddQuad(p0, p1, p2, p3, color);
                AddQuad(p4, p7, p6, p5, color);
                AddQuad(p0, p4, p5, p1, color);
                AddQuad(p1, p5, p6, p2, color);
                AddQuad(p2, p6, p7, p3, color);
                AddQuad(p3, p7, p4, p0, color);
            }

            // Delta top surface
            Vector3 nose = new Vector3(0f, 0.14f, 0.46f);
            Vector3 tail = new Vector3(0f, 0.1f, -0.4f);
            Vector3 wingLf = new Vector3(-0.48f, 0.11f, 0.02f);
            Vector3 wingRf = new Vector3(0.48f, 0.11f, 0.02f);
            Vector3 wingLm = new Vector3(-0.5f, 0.09f, -0.2f);
            Vector3 wingRm = new Vector3(0.5f, 0.09f, -0.2f);
            AddTriangle(nose, wingLf, wingRf, hull);
            AddTriangle(wingLf, wingLm, tail, hull);
            AddTriangle(wingRf, tail, wingRm, hull);
            AddTriangle(wingLf, tail, wingRf, hull);
            AddTriangle(wingLm, wingRm, tail, hull);

            // Belly plate
            Vector3 bellyN = nose + new Vector3(0f, -0.12f, 0f);
            Vector3 bellyT = tail + new Vector3(0f, -0.08f, 0f);
            Vector3 bellyLf = wingLf + new Vector3(0f, -0.1f, 0f);
            Vector3 bellyRf = wingRf + new Vector3(0f, -0.1f, 0f);
            Vector3 bellyLm = wingLm + new Vector3(0f, -0.08f, 0f);
            Vector3 bellyRm = wingRm + new Vector3(0f, -0.08f, 0f);
            AddTriangle(bellyN, bellyRf, bellyLf, hull * 0.85f);
            AddTriangle(bellyLf, bellyRf, bellyRm, hull * 0.85f);
            AddTriangle(bellyLf, bellyRm, bellyLm, hull * 0.85f);
            AddTriangle(bellyLf, bellyLm, bellyT, hull * 0.85f);
            AddTriangle(bellyLm, bellyRm, bellyT, hull * 0.85f);
            AddTriangle(bellyRf, bellyT, bellyRm, hull * 0.85f);

            // Side facets for thickness
            AddQuad(nose, bellyN, bellyLf, wingLf, hull);
            AddQuad(nose, wingRf, bellyRf, bellyN, hull);
            AddQuad(wingLm, bellyLm, bellyT, tail, hull * 0.9f);
            AddQuad(tail, bellyT, bellyRm, wingRm, hull * 0.9f);

            // Cockpit canopy
            AddBox(new Vector3(0f, 0.2f, 0.18f), new Vector3(0.12f, 0.06f, 0.18f), cockpit);

            // Red accent spine
            AddBox(new Vector3(0f, 0.16f, 0.05f), new Vector3(0.05f, 0.02f, 0.55f), accent);

            // Wing accent edges
            AddBox(new Vector3(-0.46f, 0.12f, -0.05f), new Vector3(0.04f, 0.015f, 0.35f), accent);
            AddBox(new Vector3(0.46f, 0.12f, -0.05f), new Vector3(0.04f, 0.015f, 0.35f), accent);

            // Engine nacelles
            Vector3 mainEngine = new Vector3(0f, 0.08f, -0.36f);
            Vector3 leftEngine = new Vector3(-0.38f, 0.07f, -0.28f);
            Vector3 rightEngine = new Vector3(0.38f, 0.07f, -0.28f);
            AddBox(mainEngine, new Vector3(0.1f, 0.1f, 0.14f), engine);
            AddBox(leftEngine, new Vector3(0.08f, 0.08f, 0.12f), engine);
            AddBox(rightEngine, new Vector3(0.08f, 0.08f, 0.12f), engine);

            // Nozzle rings (emissive hint via brighter engine color)
            Color nozzle = new Color(0.45f, 0.88f, 1f);
            AddBox(mainEngine + new Vector3(0f, 0f, -0.08f), new Vector3(0.06f, 0.06f, 0.03f), nozzle);
            AddBox(leftEngine + new Vector3(0f, 0f, -0.07f), new Vector3(0.05f, 0.05f, 0.025f), nozzle * 0.85f);
            AddBox(rightEngine + new Vector3(0f, 0f, -0.07f), new Vector3(0.05f, 0.05f, 0.025f), nozzle * 0.85f);

            var mesh = new Mesh { name = "RTG_GliderBlockout" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetColors(colors);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return new BuildResult(
                mesh,
                mainEngine * s + new Vector3(0f, 0f, -0.09f * s),
                leftEngine * s + new Vector3(0f, 0f, -0.08f * s),
                rightEngine * s + new Vector3(0f, 0f, -0.08f * s));
        }
    }
}
