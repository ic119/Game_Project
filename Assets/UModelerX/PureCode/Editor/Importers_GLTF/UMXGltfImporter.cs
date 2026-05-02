using System;

using UnityEditor.AssetImporters;
using UnityEngine;

namespace Tripolygon.UModelerX.Editor.Importers.GLTF
{
#if !GLTFAST_PRESENT
    [ScriptedImporter(1, new[] { "gltf", "glb" })]
    internal sealed class UMXGltfImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            try
            {
                var document = UMXGltfDocumentLoader.Load(ctx.assetPath);
                var assetContext = new UMXGltfAssetContext(ctx);
                var materialBuilder = new UMXGltfMaterialBuilder(document, assetContext);
                var meshBuilder = new UMXGltfMeshBuilder(document, assetContext, materialBuilder);
                var builtMeshes = meshBuilder.BuildAll();
                var nodeBuilder = new UMXGltfNodeBuilder(document, builtMeshes);
                var rootObject = nodeBuilder.BuildSceneRoot();

                assetContext.AddObject("main", rootObject);
                ctx.SetMainObject(rootObject);
            }
            catch (Exception exception)
            {
                var errorObject = new GameObject("GLTF Import Failed");
                var assetContext = new UMXGltfAssetContext(ctx);
                assetContext.AddObject("main_error", errorObject);
                ctx.LogImportError(exception.ToString());
                ctx.SetMainObject(errorObject);
            }
        }
    }
#endif
}
