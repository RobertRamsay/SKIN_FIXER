using System.IO;
using UnityEditor;
using UnityEngine;

namespace SkinWeightTools.EditorTools
{
    public static class MeshWeightWriter
    {
        public const string OutputFolder = "Assets/SkinWeightTools/GeneratedMeshes";

        /// <summary>
        /// Model imported meshes are read only sub assets, so every edit targets a clone.
        /// The source FBX is never touched and the renderer can always be reverted.
        /// </summary>
        public static Mesh CreateWorkingCopy(Mesh sourceMesh)
        {
            if (sourceMesh == null)
            {
                return null;
            }

            Mesh copy = UnityEngine.Object.Instantiate(sourceMesh);
            copy.name = sourceMesh.name + "_SWT";
            return copy;
        }

        public static void ApplyWeights(Mesh targetMesh, SkinWeightSet weightSet)
        {
            if (targetMesh == null || weightSet == null)
            {
                return;
            }

            weightSet.WriteToMesh(targetMesh);
            targetMesh.RecalculateBounds();
        }

        public static string SaveAsAsset(Mesh mesh)
        {
            if (mesh == null)
            {
                return string.Empty;
            }

            if (Directory.Exists(OutputFolder) == false)
            {
                Directory.CreateDirectory(OutputFolder);
                AssetDatabase.Refresh();
            }

            string path = OutputFolder + "/" + mesh.name + ".asset";
            path = AssetDatabase.GenerateUniqueAssetPath(path);

            AssetDatabase.CreateAsset(mesh, path);
            AssetDatabase.SaveAssets();

            return path;
        }

        public static void AssignToRenderer(SkinnedMeshRenderer renderer, Mesh mesh)
        {
            if (renderer == null)
            {
                return;
            }

            Undo.RecordObject(renderer, "Assign Skin Weight Mesh");
            renderer.sharedMesh = mesh;
            EditorUtility.SetDirty(renderer);
        }

        /// <summary>Frees a scratch mesh that was never saved as an asset.</summary>
        public static void DisposeIfUnsaved(Mesh mesh)
        {
            if (mesh == null)
            {
                return;
            }

            if (AssetDatabase.Contains(mesh) == true)
            {
                return;
            }

            UnityEngine.Object.DestroyImmediate(mesh);
        }
    }
}
