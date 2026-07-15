using System;
using System.Collections;
using System.Reflection;
using System.Threading.Tasks;
using CesiumForUnity;
using Unity.Mathematics;
using UnityEngine;

namespace RoutesToGlory.Game
{
    /// <summary>
    /// Shared helpers for Cesium terrain height queries.
    /// </summary>
    public static class RtgTerrainHeightSampler
    {
        public struct SampleRequest
        {
            public double Longitude;
            public double Latitude;
            public double FallbackHeightM;
        }

        /// <summary>Resolve the terrain tileset from the material controller or scene.</summary>
        public static Cesium3DTileset ResolveTileset()
        {
#if UNITY_2023_1_OR_NEWER
            RtgTerrainMaterialController controller =
                UnityEngine.Object.FindFirstObjectByType<RtgTerrainMaterialController>();
#else
            RtgTerrainMaterialController controller =
                UnityEngine.Object.FindObjectOfType<RtgTerrainMaterialController>();
#endif
            if (controller != null && controller.terrainTileset != null)
                return controller.terrainTileset;

#if UNITY_2023_1_OR_NEWER
            return UnityEngine.Object.FindFirstObjectByType<Cesium3DTileset>();
#else
            return UnityEngine.Object.FindObjectOfType<Cesium3DTileset>();
#endif
        }

        /// <summary>
        /// Cesium Reinterop returns a non-generic Task with a Result property.
        /// </summary>
        public static bool TryGetSampleHeightResult(Task task, out CesiumSampleHeightResult result)
        {
            result = null;
            if (task == null || task.Status != TaskStatus.RanToCompletion)
                return false;

            PropertyInfo prop = task.GetType().GetProperty("Result");
            if (prop == null)
                return false;

            result = prop.GetValue(task) as CesiumSampleHeightResult;
            return result != null;
        }

        /// <summary>
        /// Sample terrain heights for each request. Invokes onComplete with ellipsoid heights (m).
        /// </summary>
        public static IEnumerator SampleHeightsCoroutine(
            Cesium3DTileset tileset,
            SampleRequest[] requests,
            Action<double[]> onComplete)
        {
            if (requests == null || requests.Length == 0)
            {
                onComplete?.Invoke(Array.Empty<double>());
                yield break;
            }

            double[] heights = new double[requests.Length];
            for (int i = 0; i < requests.Length; i++)
                heights[i] = requests[i].FallbackHeightM;

            if (tileset == null)
            {
                onComplete?.Invoke(heights);
                yield break;
            }

            var positions = new double3[requests.Length];
            for (int i = 0; i < requests.Length; i++)
            {
                positions[i] = new double3(
                    requests[i].Longitude,
                    requests[i].Latitude,
                    requests[i].FallbackHeightM);
            }

            Task task = tileset.SampleHeightMostDetailed(positions);
            yield return new WaitForTask(task);

            if (TryGetSampleHeightResult(task, out CesiumSampleHeightResult sample))
            {
                for (int i = 0; i < requests.Length; i++)
                {
                    if (sample.sampleSuccess != null &&
                        i < sample.sampleSuccess.Length &&
                        sample.sampleSuccess[i] &&
                        sample.longitudeLatitudeHeightPositions != null &&
                        i < sample.longitudeLatitudeHeightPositions.Length)
                    {
                        heights[i] = sample.longitudeLatitudeHeightPositions[i].z;
                    }
                }
            }

            onComplete?.Invoke(heights);
        }
    }
}
