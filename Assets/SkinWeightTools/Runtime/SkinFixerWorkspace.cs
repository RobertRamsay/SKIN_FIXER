using UnityEngine;

namespace SkinWeightTools
{
    /// <summary>
    /// Marks a scene as a Skin Fixer workspace and holds the references the tooling needs.
    /// Imported models are parented under modelRoot so the scene stays tidy across many
    /// test imports, and the lighting rig can be adjusted without hunting through hierarchy.
    /// </summary>
    [DisallowMultipleComponent]
    public class SkinFixerWorkspace : MonoBehaviour
    {
        [Tooltip("Imported skinned meshes are parented here.")]
        public Transform modelRoot = null;

        [Tooltip("Camera used to review deformation.")]
        public Camera previewCamera = null;

        [Tooltip("Parent of the three light rig.")]
        public Transform lightingRoot = null;

        public Light keyLight = null;
        public Light fillLight = null;
        public Light rimLight = null;

        [Tooltip("Ground reference plane. Disabled for meshes that are not floor standing.")]
        public GameObject groundPlane = null;

        [Tooltip("Roughly how tall the model is expected to be, in metres. Used to place the camera.")]
        public float expectedModelHeight = 1.8f;

        public static SkinFixerWorkspace FindInScene()
        {
            return Object.FindFirstObjectByType<SkinFixerWorkspace>(FindObjectsInactive.Include);
        }

        /// <summary>Bounds of every renderer under modelRoot. Returns false when nothing is loaded.</summary>
        public bool TryGetModelBounds(out Bounds bounds)
        {
            bounds = new Bounds(Vector3.zero, Vector3.one);

            if (modelRoot == null)
            {
                return false;
            }

            Renderer[] renderers = modelRoot.GetComponentsInChildren<Renderer>(true);

            if (renderers.Length == 0)
            {
                return false;
            }

            bool started = false;

            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer current = renderers[index];

                if (current == null)
                {
                    continue;
                }

                if (started == false)
                {
                    bounds = current.bounds;
                    started = true;
                    continue;
                }

                bounds.Encapsulate(current.bounds);
            }

            return started;
        }

        public void SetGroundVisible(bool visible)
        {
            if (groundPlane == null)
            {
                return;
            }

            groundPlane.SetActive(visible);
        }
    }
}
