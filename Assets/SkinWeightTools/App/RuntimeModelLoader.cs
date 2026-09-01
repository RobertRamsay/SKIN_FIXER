using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

#if GLTFAST_PRESENT
using GLTFast;
#endif

namespace SkinWeightTools.App
{
    public class LoadedModel
    {
        public GameObject root = null;
        public List<SkinnedMeshRenderer> renderers = new List<SkinnedMeshRenderer>();
        public string sourcePath = "";
        public string error = "";

        public bool IsValid()
        {
            return root != null && renderers.Count > 0;
        }
    }

    /// <summary>
    /// Loads a glTF or GLB from disk at runtime. FBX is deliberately not handled: Unity has
    /// no runtime FBX importer, so that path needs a third party library.
    /// </summary>
    public static class RuntimeModelLoader
    {
        public static async Task<LoadedModel> LoadAsync(string path, Transform parent)
        {
            LoadedModel result = new LoadedModel();
            result.sourcePath = path;

            if (string.IsNullOrEmpty(path) == true)
            {
                result.error = "No file selected.";
                return result;
            }

            if (System.IO.File.Exists(path) == false)
            {
                result.error = "File not found: " + path;
                return result;
            }

#if GLTFAST_PRESENT
            GltfImport import = new GltfImport();
            bool parsed = false;

            try
            {
                // glTFast wants a URI. Local Windows paths need the file scheme and escaping,
                // which Uri does correctly for spaces and non ASCII characters.
                string uri = new Uri(System.IO.Path.GetFullPath(path)).AbsoluteUri;
                parsed = await import.Load(uri);
            }
            catch (Exception error)
            {
                result.error = "glTF load threw: " + error.Message;
                return result;
            }

            if (parsed == false)
            {
                result.error = "glTF could not be parsed. Check the console for the importer's own errors.";
                return result;
            }

            GameObject holder = new GameObject(System.IO.Path.GetFileNameWithoutExtension(path));
            holder.transform.SetParent(parent, false);

            bool instantiated = await import.InstantiateMainSceneAsync(holder.transform);

            if (instantiated == false)
            {
                UnityEngine.Object.Destroy(holder);
                result.error = "glTF parsed but the scene could not be instantiated.";
                return result;
            }

            result.root = holder;
            holder.GetComponentsInChildren<SkinnedMeshRenderer>(true, result.renderers);

            if (result.renderers.Count == 0)
            {
                result.error = "Loaded, but the file contains no skinned meshes. A static mesh has no weights to fix.";
                return result;
            }

            // A mesh that was uploaded and released cannot be read back, and every solver
            // step needs the vertex and weight buffers.
            for (int index = result.renderers.Count - 1; index >= 0; index--)
            {
                Mesh mesh = result.renderers[index].sharedMesh;

                if (mesh == null || mesh.isReadable == false)
                {
                    Debug.LogWarning("RuntimeModelLoader: skipping unreadable mesh on '" + result.renderers[index].name + "'.");
                    result.renderers.RemoveAt(index);
                }
            }

            if (result.renderers.Count == 0)
            {
                result.error = "Skinned meshes were found but none are readable.";
            }

            return result;
#else
            await Task.CompletedTask;
            result.error = "com.unity.cloud.gltfast is not installed. Add it in Window > Package Manager.";
            return result;
#endif
        }
    }
}
