using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SkinWeightTools.EditorTools
{
    public static class SkinnedMeshImportUtility
    {
        public const string ImportFolder = "Assets/SkinWeightTools/Imported";

        public static readonly string[] SupportedExtensions = new string[]
        {
            ".fbx", ".gltf", ".glb", ".usd", ".usda", ".usdc", ".usdz"
        };

        public static bool IsGltfPackagePresent()
        {
            Type legacyType = Type.GetType("GLTFast.GltfImport, glTFast");

            if (legacyType != null)
            {
                return true;
            }

            Type cloudType = Type.GetType("GLTFast.GltfImport, Unity.Cloud.GLTFast");

            if (cloudType != null)
            {
                return true;
            }

            return false;
        }

        public static bool IsUsdPackagePresent()
        {
            Type usdType = Type.GetType("Unity.Formats.USD.UsdAsset, Unity.Formats.USD.Runtime");

            if (usdType != null)
            {
                return true;
            }

            return false;
        }

        /// <summary>Returns an empty string when the format is ready to import.</summary>
        public static string DescribeMissingSupport(string extension)
        {
            string lowered = extension.ToLowerInvariant();

            if (lowered == ".gltf" || lowered == ".glb")
            {
                if (IsGltfPackagePresent() == false)
                {
                    return "glTF import needs the glTFast package.\n\nWindow > Package Manager > + > Add package by name > com.unity.cloud.gltfast";
                }
            }

            if (lowered == ".usd" || lowered == ".usda" || lowered == ".usdc" || lowered == ".usdz")
            {
                if (IsUsdPackagePresent() == false)
                {
                    return "USD import needs the USD package.\n\nWindow > Package Manager > + > Add package by name > com.unity.formats.usd";
                }
            }

            return string.Empty;
        }

        public static bool IsSupportedExtension(string extension)
        {
            string lowered = extension.ToLowerInvariant();

            for (int index = 0; index < SupportedExtensions.Length; index++)
            {
                if (SupportedExtensions[index] == lowered)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Copies an external file into the project and imports it with the full set of
        /// skin influences preserved. Returns the asset path, or an empty string on failure.
        /// </summary>
        public static string ImportExternalFile(string sourcePath)
        {
            if (string.IsNullOrEmpty(sourcePath) == true)
            {
                return string.Empty;
            }

            if (File.Exists(sourcePath) == false)
            {
                EditorUtility.DisplayDialog("File missing", "Could not read " + sourcePath, "OK");
                return string.Empty;
            }

            string extension = Path.GetExtension(sourcePath);

            if (IsSupportedExtension(extension) == false)
            {
                EditorUtility.DisplayDialog(
                    "Unsupported format",
                    "The extension " + extension + " is not handled by this tool.",
                    "OK");
                return string.Empty;
            }

            string missing = DescribeMissingSupport(extension);

            if (string.IsNullOrEmpty(missing) == false)
            {
                EditorUtility.DisplayDialog("Missing package", missing, "OK");
                return string.Empty;
            }

            if (Directory.Exists(ImportFolder) == false)
            {
                Directory.CreateDirectory(ImportFolder);
                AssetDatabase.Refresh();
            }

            string fileName = Path.GetFileName(sourcePath);
            string destination = Path.Combine(ImportFolder, fileName).Replace("\\", "/");

            try
            {
                File.Copy(sourcePath, destination, true);
            }
            catch (IOException error)
            {
                EditorUtility.DisplayDialog("Copy failed", error.Message, "OK");
                return string.Empty;
            }

            AssetDatabase.ImportAsset(destination, ImportAssetOptions.ForceUpdate);
            ConfigureModelImporter(destination);

            return destination;
        }

        /// <summary>
        /// Keeps every influence and makes the mesh readable, so the tool sees the raw
        /// weights rather than a version Unity has already culled to four bones.
        /// </summary>
        public static void ConfigureModelImporter(string assetPath)
        {
            ModelImporter importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;

            if (importer == null)
            {
                return;
            }

            bool changed = false;

            if (importer.isReadable == false)
            {
                importer.isReadable = true;
                changed = true;
            }

            if (importer.skinWeights != ModelImporterSkinWeights.Custom)
            {
                importer.skinWeights = ModelImporterSkinWeights.Custom;
                changed = true;
            }

            if (importer.maxBonesPerVertex < 32)
            {
                importer.maxBonesPerVertex = 32;
                changed = true;
            }

            if (importer.minBoneWeight > 0.0001f)
            {
                importer.minBoneWeight = 0.0001f;
                changed = true;
            }

            if (changed == true)
            {
                importer.SaveAndReimport();
            }
        }

        public static List<SkinnedMeshRenderer> InstantiateAndCollect(string assetPath)
        {
            List<SkinnedMeshRenderer> found = new List<SkinnedMeshRenderer>();
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);

            if (prefab == null)
            {
                return found;
            }

            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            instance.name = prefab.name + " (SkinWeightTools)";
            Undo.RegisterCreatedObjectUndo(instance, "Import Skinned Mesh");

            SkinFixerWorkspace workspace = SkinFixerWorkspace.FindInScene();

            if (workspace != null && workspace.modelRoot != null)
            {
                instance.transform.SetParent(workspace.modelRoot, false);
            }

            instance.GetComponentsInChildren<SkinnedMeshRenderer>(true, found);
            Selection.activeGameObject = instance;

            if (workspace != null)
            {
                WorkspaceSceneBuilder.FrameModel(workspace);
            }

            return found;
        }

        public static string OpenImportPanel()
        {
            string filter = "fbx,gltf,glb,usd,usda,usdc,usdz";
            return EditorUtility.OpenFilePanel("Import skinned mesh", "", filter);
        }
    }
}
