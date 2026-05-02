using System;
using System.Collections.Generic;

namespace Tripolygon.UModelerX.Editor.Importers.GLTF
{
    internal static class UMXGltfSupportedSubset
    {
        public const int FloatComponentType = 5126;
        public const int UnsignedByteComponentType = 5121;
        public const int UnsignedShortComponentType = 5123;
        public const int UnsignedIntComponentType = 5125;

        public const int TrianglesMode = 4;

        public static readonly string[] UnsupportedFeatures =
        {
            "skins",
            "animations",
            "morph targets",
            "Draco compression",
            "KTX2",
            "sparse accessors",
            "node matrix",
            "glTF extensions"
        };

        public static void ValidateOrThrow(UMXGltfRoot root)
        {
            if (root == null)
            {
                throw new InvalidOperationException("The glTF document could not be parsed.");
            }

            var issues = CollectIssues(root);
            if (issues.Count > 0)
            {
                throw new NotSupportedException(string.Join(Environment.NewLine, issues));
            }
        }

        public static List<string> CollectIssues(UMXGltfRoot root)
        {
            var issues = new List<string>();

            if (root.extensionsRequired != null && root.extensionsRequired.Length > 0)
            {
                issues.Add("Required glTF extensions are not supported in the first importer milestone.");
            }

            if (root.extensionsUsed != null && root.extensionsUsed.Length > 0)
            {
                issues.Add("Optional glTF extensions are intentionally excluded from the first importer milestone.");
            }

            if (root.accessors != null)
            {
                for (var i = 0; i < root.accessors.Length; ++i)
                {
                    var accessor = root.accessors[i];
                    if (accessor == null)
                    {
                        continue;
                    }

                    if (accessor.sparse != null && accessor.sparse.count > 0)
                    {
                        issues.Add($"Accessor {i} uses sparse data, which is not supported.");
                    }
                }
            }

            if (root.nodes != null)
            {
                for (var i = 0; i < root.nodes.Length; ++i)
                {
                    var node = root.nodes[i];
                    if (node == null)
                    {
                        continue;
                    }

                    if (node.matrix != null && node.matrix.Length == 16)
                    {
                        issues.Add($"Node {i} uses matrix transforms, which are excluded from the first importer milestone.");
                    }
                }
            }

            if (root.meshes != null)
            {
                for (var meshIndex = 0; meshIndex < root.meshes.Length; ++meshIndex)
                {
                    var mesh = root.meshes[meshIndex];
                    if (mesh?.primitives == null)
                    {
                        continue;
                    }

                    for (var primitiveIndex = 0; primitiveIndex < mesh.primitives.Length; ++primitiveIndex)
                    {
                        var primitive = mesh.primitives[primitiveIndex];
                        if (primitive == null)
                        {
                            continue;
                        }

                        if (primitive.mode != TrianglesMode)
                        {
                            issues.Add($"Mesh {meshIndex} primitive {primitiveIndex} uses draw mode {primitive.mode}, but only triangles are supported.");
                        }

                        if (primitive.targets != null && primitive.targets.Length > 0)
                        {
                            issues.Add($"Mesh {meshIndex} primitive {primitiveIndex} uses morph targets, which are not supported.");
                        }

                        if (primitive.attributes == null || primitive.attributes.POSITION < 0)
                        {
                            issues.Add($"Mesh {meshIndex} primitive {primitiveIndex} is missing POSITION.");
                        }
                    }
                }
            }

            return issues;
        }
    }
}
