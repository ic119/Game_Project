using System;
using UnityEngine;

namespace Tripolygon.UModelerX.Editor.Importers.GLTF
{
    /// <summary>
    /// GLB 바이트에서 메모리 전용으로 Mesh/Material을 빌드한다.
    /// PureCode.Editor 어셈블리에 속하므로, Editor 어셈블리에서는 delegate를 통해 호출한다.
    /// </summary>
    internal static class UMXGltfMemoryLoader
    {
        /// <summary>GLB 바이트를 메모리에서 파싱하여 Mesh, Material, Rotation을 반환한다.</summary>
        /// <returns>성공 시 (mesh, material, rotation), 실패 시 (null, null, identity)</returns>
        internal static (Mesh mesh, Material material, Quaternion rotation) LoadFromGlbBytes(byte[] glbBytes)
        {
            try
            {
                var document = UMXGltfDocumentLoader.LoadFromBytes(glbBytes, "preview");
                var context = new UMXGltfAssetContext();
                var materialBuilder = new UMXGltfMaterialBuilder(document, context);
                var meshBuilder = new UMXGltfMeshBuilder(document, context, materialBuilder);
                var builtMeshes = meshBuilder.BuildAll();
                var nodeBuilder = new UMXGltfNodeBuilder(document, builtMeshes);
                var rootObject = nodeBuilder.BuildSceneRoot();
                rootObject.hideFlags = HideFlags.HideAndDontSave;

                // MeshFilter/MeshRenderer 또는 SkinnedMeshRenderer에서 추출
                Mesh mesh = null;
                Material material = null;
                var meshFilter = rootObject.GetComponentInChildren<MeshFilter>();
                if (meshFilter != null)
                {
                    mesh = meshFilter.sharedMesh;
                    var renderer = rootObject.GetComponentInChildren<MeshRenderer>();
                    material = renderer != null ? renderer.sharedMaterial : null;
                }
                if (mesh == null)
                {
                    var skinned = rootObject.GetComponentInChildren<SkinnedMeshRenderer>();
                    if (skinned != null)
                    {
                        mesh = skinned.sharedMesh;
                        material = skinned.sharedMaterial;
                    }
                }

                // 임시 GameObject 정리 (Mesh/Material만 유지)
                UnityEngine.Object.DestroyImmediate(rootObject);

                if (mesh != null && material != null)
                {
                    mesh.hideFlags = HideFlags.HideAndDontSave;
                    material.hideFlags = HideFlags.HideAndDontSave;
                    // GLB 270° Y 회전
                    var rotation = Quaternion.Euler(0f, 270f, 0f);
                    return (mesh, material, rotation);
                }

                return (null, null, Quaternion.identity);
            }
            catch (Exception)
            {
                return (null, null, Quaternion.identity);
            }
        }
    }
}
