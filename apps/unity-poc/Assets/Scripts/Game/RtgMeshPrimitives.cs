using UnityEngine;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Procedural meshes for beacon visuals. Avoids GameObject.CreatePrimitive,
    /// which requires Physics collider types that IL2CPP iOS builds often strip.
    /// </summary>
    public static class RtgMeshPrimitives
    {
        private static Mesh _sphere;
        private static Mesh _cube;
        private static Mesh _groundQuad;
        private static Mesh _verticalQuad;

        public static Mesh Sphere => _sphere ??= BuildUvSphere(18, 12);
        public static Mesh Cube => _cube ??= BuildCube();
        /// <summary>Horizontal quad on the XZ plane (normal +Y). UV top = +Z (nose).</summary>
        public static Mesh GroundQuad => _groundQuad ??= BuildGroundQuad();
        /// <summary>Vertical quad on the XY plane (normal +Z). UV top = +Y.</summary>
        public static Mesh VerticalQuad => _verticalQuad ??= BuildVerticalQuad();

        public static GameObject CreateMeshObject(string name, Mesh mesh, Material material, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            return go;
        }

        private static Mesh BuildGroundQuad()
        {
            var mesh = new Mesh { name = "RTG_GroundQuad" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f),
                new Vector3(0.5f, 0f, -0.5f),
                new Vector3(-0.5f, 0f, 0.5f),
                new Vector3(0.5f, 0f, 0.5f),
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
            };
            mesh.normals = new[]
            {
                Vector3.up, Vector3.up, Vector3.up, Vector3.up,
            };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh BuildVerticalQuad()
        {
            var mesh = new Mesh { name = "RTG_VerticalQuad" };
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
            mesh.normals = new[]
            {
                Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward,
            };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh BuildCube()
        {
            var mesh = new Mesh { name = "RTG_Cube" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, -0.5f),
                new Vector3(0.5f, 0.5f, -0.5f), new Vector3(-0.5f, 0.5f, -0.5f),
                new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(0.5f, -0.5f, 0.5f),
                new Vector3(0.5f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f),
            };
            mesh.triangles = new[]
            {
                0, 2, 1, 0, 3, 2,
                1, 6, 5, 1, 2, 6,
                5, 7, 4, 5, 6, 7,
                4, 3, 0, 4, 7, 3,
                3, 6, 2, 3, 7, 6,
                4, 1, 5, 4, 0, 1,
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh BuildUvSphere(int segments, int rings)
        {
            var mesh = new Mesh { name = "RTG_Sphere" };
            int vertCount = (segments + 1) * (rings + 1);
            var vertices = new Vector3[vertCount];
            var normals = new Vector3[vertCount];
            var uvs = new Vector2[vertCount];
            int vi = 0;

            for (int ring = 0; ring <= rings; ring++)
            {
                float v = ring / (float)rings;
                float phi = v * Mathf.PI;
                float sinPhi = Mathf.Sin(phi);
                float cosPhi = Mathf.Cos(phi);

                for (int seg = 0; seg <= segments; seg++)
                {
                    float u = seg / (float)segments;
                    float theta = u * Mathf.PI * 2f;
                    float sinTheta = Mathf.Sin(theta);
                    float cosTheta = Mathf.Cos(theta);

                    Vector3 normal = new Vector3(sinPhi * cosTheta, cosPhi, sinPhi * sinTheta);
                    vertices[vi] = normal * 0.5f;
                    normals[vi] = normal;
                    uvs[vi] = new Vector2(u, 1f - v);
                    vi++;
                }
            }

            int triCount = segments * rings * 6;
            var triangles = new int[triCount];
            int ti = 0;
            for (int ring = 0; ring < rings; ring++)
            {
                for (int seg = 0; seg < segments; seg++)
                {
                    int a = ring * (segments + 1) + seg;
                    int b = a + segments + 1;
                    triangles[ti++] = a;
                    triangles[ti++] = b;
                    triangles[ti++] = a + 1;
                    triangles[ti++] = b;
                    triangles[ti++] = b + 1;
                    triangles[ti++] = a + 1;
                }
            }

            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
