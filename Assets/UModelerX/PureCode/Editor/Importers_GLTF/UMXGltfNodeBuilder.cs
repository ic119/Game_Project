using System.IO;

using UnityEngine;

namespace Tripolygon.UModelerX.Editor.Importers.GLTF
{
    internal sealed class UMXGltfNodeBuilder
    {
        private readonly UMXGltfBuiltMesh[] builtMeshes;
        private readonly UMXGltfLoadedDocument document;

        public UMXGltfNodeBuilder(UMXGltfLoadedDocument loadedDocument, UMXGltfBuiltMesh[] meshes)
        {
            document = loadedDocument;
            builtMeshes = meshes;
        }

        public GameObject BuildSceneRoot()
        {
            var rootName = Path.GetFileNameWithoutExtension(document.AssetPath);
            var rootObject = new GameObject(string.IsNullOrEmpty(rootName) ? "GLTF_Root" : rootName);

            var sceneIndex = ResolveSceneIndex();
            if (document.Root.scenes == null || sceneIndex < 0 || sceneIndex >= document.Root.scenes.Length)
            {
                return rootObject;
            }

            var scene = document.Root.scenes[sceneIndex];
            if (scene?.nodes == null)
            {
                return rootObject;
            }

            for (var i = 0; i < scene.nodes.Length; ++i)
            {
                var nodeIndex = scene.nodes[i];
                BuildNodeRecursive(nodeIndex, rootObject.transform);
            }

            return rootObject;
        }

        private int ResolveSceneIndex()
        {
            if (document.Root.scene >= 0)
            {
                return document.Root.scene;
            }

            return document.Root.scenes != null && document.Root.scenes.Length > 0 ? 0 : -1;
        }

        private GameObject BuildNodeRecursive(int nodeIndex, Transform parent)
        {
            var node = document.Root.nodes[nodeIndex];
            var nodeName = string.IsNullOrEmpty(node?.name) ? $"GLTF_Node_{nodeIndex}" : node.name;
            var nodeObject = new GameObject(nodeName);
            nodeObject.transform.SetParent(parent, false);

            if (node != null)
            {
                nodeObject.transform.localPosition = UMXGltfCoordinateUtility.ToUnityTranslation(node.translation);
                nodeObject.transform.localRotation = UMXGltfCoordinateUtility.ToUnityRotation(node.rotation);
                nodeObject.transform.localScale = UMXGltfCoordinateUtility.ToUnityScale(node.scale);

                if (node.mesh >= 0)
                {
                    AttachMesh(nodeObject, node.mesh);
                }

                if (node.children != null)
                {
                    for (var i = 0; i < node.children.Length; ++i)
                    {
                        BuildNodeRecursive(node.children[i], nodeObject.transform);
                    }
                }
            }

            return nodeObject;
        }

        private void AttachMesh(GameObject nodeObject, int meshIndex)
        {
            if (meshIndex < 0 || meshIndex >= builtMeshes.Length)
            {
                return;
            }

            var builtMesh = builtMeshes[meshIndex];
            if (builtMesh == null || builtMesh.Primitives == null || builtMesh.Primitives.Count == 0)
            {
                return;
            }

            if (builtMesh.Primitives.Count == 1)
            {
                AttachPrimitive(nodeObject, builtMesh.Primitives[0]);
                return;
            }

            for (var i = 0; i < builtMesh.Primitives.Count; ++i)
            {
                var primitiveObject = new GameObject($"{builtMesh.Name}_Primitive_{i}");
                primitiveObject.transform.SetParent(nodeObject.transform, false);
                AttachPrimitive(primitiveObject, builtMesh.Primitives[i]);
            }
        }

        private static void AttachPrimitive(GameObject target, UMXGltfBuiltPrimitive primitive)
        {
            var meshFilter = target.AddComponent<MeshFilter>();
            var meshRenderer = target.AddComponent<MeshRenderer>();
            meshFilter.sharedMesh = primitive.Mesh;
            meshRenderer.sharedMaterial = primitive.Material;
        }
    }
}
