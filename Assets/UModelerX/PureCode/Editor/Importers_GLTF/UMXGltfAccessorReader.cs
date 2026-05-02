using System;

using UnityEngine;

namespace Tripolygon.UModelerX.Editor.Importers.GLTF
{
    internal static class UMXGltfAccessorReader
    {
        private readonly struct AccessorContext
        {
            public AccessorContext(
                byte[] buffer,
                int elementCount,
                int elementOffset,
                int componentCount,
                int componentSize,
                int componentType,
                bool normalized,
                int stride)
            {
                Buffer = buffer;
                ElementCount = elementCount;
                ElementOffset = elementOffset;
                ComponentCount = componentCount;
                ComponentSize = componentSize;
                ComponentType = componentType;
                Normalized = normalized;
                Stride = stride;
            }

            public byte[] Buffer { get; }

            public int ComponentCount { get; }

            public int ComponentSize { get; }

            public int ComponentType { get; }

            public int ElementCount { get; }

            public int ElementOffset { get; }

            public bool Normalized { get; }

            public int Stride { get; }
        }

        public static Vector3[] ReadVector3(UMXGltfLoadedDocument document, int accessorIndex)
        {
            var context = CreateContext(document, accessorIndex, "VEC3");
            var result = new Vector3[context.ElementCount];

            for (var i = 0; i < result.Length; ++i)
            {
                var baseOffset = context.ElementOffset + context.Stride * i;
                result[i] = new Vector3(
                    ReadFloat(context, baseOffset, 0),
                    ReadFloat(context, baseOffset, 1),
                    ReadFloat(context, baseOffset, 2));
            }

            return result;
        }

        public static Vector4[] ReadVector4(UMXGltfLoadedDocument document, int accessorIndex)
        {
            var context = CreateContext(document, accessorIndex, "VEC4");
            var result = new Vector4[context.ElementCount];

            for (var i = 0; i < result.Length; ++i)
            {
                var baseOffset = context.ElementOffset + context.Stride * i;
                result[i] = new Vector4(
                    ReadFloat(context, baseOffset, 0),
                    ReadFloat(context, baseOffset, 1),
                    ReadFloat(context, baseOffset, 2),
                    ReadFloat(context, baseOffset, 3));
            }

            return result;
        }

        public static Vector2[] ReadVector2(UMXGltfLoadedDocument document, int accessorIndex)
        {
            var context = CreateContext(document, accessorIndex, "VEC2");
            var result = new Vector2[context.ElementCount];

            for (var i = 0; i < result.Length; ++i)
            {
                var baseOffset = context.ElementOffset + context.Stride * i;
                result[i] = new Vector2(
                    ReadFloat(context, baseOffset, 0),
                    ReadFloat(context, baseOffset, 1));
            }

            return result;
        }

        public static Color[] ReadColors(UMXGltfLoadedDocument document, int accessorIndex)
        {
            var accessor = GetAccessor(document, accessorIndex);
            if (accessor.type != "VEC3" && accessor.type != "VEC4")
            {
                throw new InvalidOperationException($"Accessor {accessorIndex} must be VEC3 or VEC4 for vertex colors.");
            }

            var context = CreateContext(document, accessorIndex, accessor.type);
            var result = new Color[context.ElementCount];
            var hasAlpha = context.ComponentCount == 4;

            for (var i = 0; i < result.Length; ++i)
            {
                var baseOffset = context.ElementOffset + context.Stride * i;
                var r = ReadFloat(context, baseOffset, 0);
                var g = ReadFloat(context, baseOffset, 1);
                var b = ReadFloat(context, baseOffset, 2);
                var a = hasAlpha ? ReadFloat(context, baseOffset, 3) : 1.0f;
                result[i] = new Color(r, g, b, a);
            }

            return result;
        }

        public static int[] ReadIndices(UMXGltfLoadedDocument document, int accessorIndex)
        {
            var context = CreateContext(document, accessorIndex, "SCALAR");
            var result = new int[context.ElementCount];

            for (var i = 0; i < result.Length; ++i)
            {
                var elementOffset = context.ElementOffset + context.Stride * i;
                result[i] = ReadInt(context, elementOffset);
            }

            return result;
        }

        public static int GetCount(UMXGltfLoadedDocument document, int accessorIndex)
        {
            return GetAccessor(document, accessorIndex).count;
        }

        private static AccessorContext CreateContext(UMXGltfLoadedDocument document, int accessorIndex, string expectedType)
        {
            var accessor = GetAccessor(document, accessorIndex);
            if (accessor.bufferView < 0)
            {
                throw new InvalidOperationException($"Accessor {accessorIndex} does not reference a bufferView.");
            }

            if (!string.Equals(accessor.type, expectedType, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Accessor {accessorIndex} must be {expectedType}, but was {accessor.type}.");
            }

            var bufferView = document.Root.bufferViews[accessor.bufferView];
            var sourceBuffer = document.GetBufferBytes(bufferView.buffer);
            var componentSize = GetComponentSize(accessor.componentType);
            var componentCount = GetComponentCount(accessor.type);
            var elementSize = componentSize * componentCount;
            var stride = bufferView.byteStride > 0 ? bufferView.byteStride : elementSize;
            var elementOffset = bufferView.byteOffset + accessor.byteOffset;

            return new AccessorContext(
                sourceBuffer,
                accessor.count,
                elementOffset,
                componentCount,
                componentSize,
                accessor.componentType,
                accessor.normalized,
                stride);
        }

        private static UMXGltfAccessor GetAccessor(UMXGltfLoadedDocument document, int accessorIndex)
        {
            if (document.Root.accessors == null || accessorIndex < 0 || accessorIndex >= document.Root.accessors.Length)
            {
                throw new IndexOutOfRangeException($"Accessor {accessorIndex} is invalid.");
            }

            var accessor = document.Root.accessors[accessorIndex];
            if (accessor == null)
            {
                throw new InvalidOperationException($"Accessor {accessorIndex} is null.");
            }

            return accessor;
        }

        private static int GetComponentCount(string accessorType)
        {
            return accessorType switch
            {
                "SCALAR" => 1,
                "VEC2" => 2,
                "VEC3" => 3,
                "VEC4" => 4,
                _ => throw new NotSupportedException($"Accessor type {accessorType} is not supported.")
            };
        }

        private static int GetComponentSize(int componentType)
        {
            return componentType switch
            {
                5121 => 1,
                5123 => 2,
                5125 => 4,
                5126 => 4,
                _ => throw new NotSupportedException($"Component type {componentType} is not supported.")
            };
        }

        private static float ReadFloat(AccessorContext context, int baseOffset, int componentIndex)
        {
            var componentOffset = baseOffset + context.ComponentSize * componentIndex;

            switch (context.ComponentType)
            {
                case 5126:
                    return BitConverter.ToSingle(context.Buffer, componentOffset);
                case 5121:
                    {
                        var value = context.Buffer[componentOffset];
                        return context.Normalized ? value / 255.0f : value;
                    }
                case 5123:
                    {
                        var value = BitConverter.ToUInt16(context.Buffer, componentOffset);
                        return context.Normalized ? value / 65535.0f : value;
                    }
            }

            throw new NotSupportedException($"Component type {context.ComponentType} cannot be read as float.");
        }

        private static int ReadInt(AccessorContext context, int elementOffset)
        {
            switch (context.ComponentType)
            {
                case 5121:
                    return context.Buffer[elementOffset];
                case 5123:
                    return BitConverter.ToUInt16(context.Buffer, elementOffset);
                case 5125:
                    return checked((int)BitConverter.ToUInt32(context.Buffer, elementOffset));
            }

            throw new NotSupportedException($"Index component type {context.ComponentType} is not supported.");
        }
    }
}
