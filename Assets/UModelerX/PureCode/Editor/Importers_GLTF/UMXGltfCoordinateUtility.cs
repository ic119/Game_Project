using UnityEngine;

namespace Tripolygon.UModelerX.Editor.Importers.GLTF
{
    internal static class UMXGltfCoordinateUtility
    {
        public static Vector3 ToUnityPosition(Vector3 value)
        {
            return new Vector3(value.x, value.y, -value.z);
        }

        public static Vector3 ToUnityNormal(Vector3 value)
        {
            return new Vector3(value.x, value.y, -value.z);
        }

        public static Vector4 ToUnityTangent(Vector4 value)
        {
            return new Vector4(value.x, value.y, -value.z, -value.w);
        }

        public static Vector2 ToUnityUv(Vector2 value)
        {
            return new Vector2(value.x, 1.0f - value.y);
        }

        public static Quaternion ToUnityRotation(float[] value)
        {
            if (value == null || value.Length != 4)
            {
                return Quaternion.identity;
            }

            return new Quaternion(-value[0], -value[1], value[2], value[3]);
        }

        public static Vector3 ToUnityTranslation(float[] value)
        {
            if (value == null || value.Length != 3)
            {
                return Vector3.zero;
            }

            return new Vector3(value[0], value[1], -value[2]);
        }

        public static Vector3 ToUnityScale(float[] value)
        {
            if (value == null || value.Length != 3)
            {
                return Vector3.one;
            }

            return new Vector3(value[0], value[1], value[2]);
        }

        public static void ReverseTriangleWinding(int[] indices)
        {
            if (indices == null)
            {
                return;
            }

            for (var i = 0; i + 2 < indices.Length; i += 3)
            {
                var temp = indices[i];
                indices[i] = indices[i + 2];
                indices[i + 2] = temp;
            }
        }
    }
}
