using UnityEngine;

namespace SkinWeightTools.App
{
    /// <summary>
    /// Runtime counterpart to the editor MeshWeightWriter. No AssetDatabase, so working
    /// meshes live only in memory and have to be destroyed by hand when a model is
    /// unloaded, otherwise every reload leaks a full mesh clone.
    /// </summary>
    public static class RuntimeMeshWeightWriter
    {
        public static Mesh CreateWorkingCopy(Mesh sourceMesh)
        {
            if (sourceMesh == null)
            {
                return null;
            }

            Mesh copy = Object.Instantiate(sourceMesh);
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

        public static void AssignToRenderer(SkinnedMeshRenderer renderer, Mesh mesh)
        {
            if (renderer == null)
            {
                return;
            }

            renderer.sharedMesh = mesh;
        }

        public static void Destroy(Mesh mesh)
        {
            if (mesh == null)
            {
                return;
            }

            Object.Destroy(mesh);
        }
    }
}
