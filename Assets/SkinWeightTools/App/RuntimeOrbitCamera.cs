using UnityEngine;
using UnityEngine.InputSystem;

namespace SkinWeightTools.App
{
    /// <summary>
    /// Turntable camera for inspecting deformation. Judging a weight fix means getting round
    /// the joint while it bends, so this is part of the tool rather than a convenience.
    /// Left drag orbits, middle drag pans, wheel dollies.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class RuntimeOrbitCamera : MonoBehaviour
    {
        public Vector3 pivot = Vector3.zero;
        public float distance = 3f;
        public float yaw = 25f;
        public float pitch = 12f;

        [Tooltip("Degrees per screen pixel of drag.")]
        public float orbitSensitivity = 0.25f;

        public float panSensitivity = 0.0025f;
        public float zoomSensitivity = 0.1f;
        public float minPitch = -85f;
        public float maxPitch = 85f;
        public float minDistance = 0.05f;

        [Tooltip("Blocks camera input while the pointer is over the tool UI.")]
        public bool ignoreWhenPointerOverUI = true;

        private void LateUpdate()
        {
            Mouse mouse = Mouse.current;

            if (mouse != null && IsPointerBlocked() == false)
            {
                Vector2 delta = mouse.delta.ReadValue();

                if (mouse.leftButton.isPressed == true)
                {
                    yaw = yaw + delta.x * orbitSensitivity;
                    pitch = Mathf.Clamp(pitch - delta.y * orbitSensitivity, minPitch, maxPitch);
                }

                if (mouse.middleButton.isPressed == true)
                {
                    // Pan scales with distance, otherwise it crawls when zoomed out and
                    // overshoots when zoomed in.
                    float scale = panSensitivity * distance;
                    pivot = pivot - transform.right * delta.x * scale - transform.up * delta.y * scale;
                }

                float scroll = mouse.scroll.ReadValue().y;

                if (Mathf.Approximately(scroll, 0f) == false)
                {
                    // Proportional zoom, so each notch covers the same fraction of the
                    // remaining distance whatever the model scale.
                    distance = Mathf.Max(minDistance, distance * (1f - Mathf.Sign(scroll) * zoomSensitivity));
                }
            }

            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            transform.rotation = rotation;
            transform.position = pivot - rotation * Vector3.forward * distance;
        }

        private bool IsPointerBlocked()
        {
            if (ignoreWhenPointerOverUI == false)
            {
                return false;
            }

            UnityEngine.EventSystems.EventSystem current = UnityEngine.EventSystems.EventSystem.current;

            if (current == null)
            {
                return false;
            }

            return current.IsPointerOverGameObject();
        }

        /// <summary>Centres on a model and backs off far enough to hold it in frame.</summary>
        public void Frame(Bounds bounds)
        {
            pivot = bounds.center;

            float radius = bounds.extents.magnitude;

            if (radius < 0.0001f)
            {
                radius = 1f;
            }

            Camera owned = GetComponent<Camera>();
            float verticalFov = 60f;

            if (owned != null)
            {
                verticalFov = owned.fieldOfView;
                owned.nearClipPlane = Mathf.Max(0.001f, radius * 0.01f);
                owned.farClipPlane = Mathf.Max(1000f, radius * 100f);
            }

            distance = radius / Mathf.Tan(verticalFov * 0.5f * Mathf.Deg2Rad) * 1.2f;
        }
    }
}
