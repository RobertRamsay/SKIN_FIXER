using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace SkinWeightTools.EditorTools
{
    /// <summary>
    /// Builds the workspace scene from script rather than shipping a hand written .unity
    /// file. Unity writes YAML for the exact editor version in use, and the scene can be
    /// regenerated at any time if it gets damaged.
    /// </summary>
    public static class WorkspaceSceneBuilder
    {
        public const string SceneFolder = "Assets/SkinWeightTools/Scenes";
        public const string ScenePath = "Assets/SkinWeightTools/Scenes/SkinFixer_Workspace.unity";

        private static readonly Color BackgroundColour = new Color(0.212f, 0.216f, 0.227f, 1f);
        private static readonly Color AmbientColour = new Color(0.29f, 0.30f, 0.33f, 1f);

        [MenuItem("Tools/Skin Weight Tools/Create Workspace Scene", false, 20)]
        public static void CreateWorkspaceSceneMenu()
        {
            CreateWorkspaceScene(true);
        }

        /// <summary>Returns the saved scene path, or an empty string when the user cancels.</summary>
        public static string CreateWorkspaceScene(bool promptOnOverwrite)
        {
            if (promptOnOverwrite == true && File.Exists(ScenePath) == true)
            {
                bool replace = EditorUtility.DisplayDialog(
                    "Replace workspace scene",
                    "A workspace scene already exists at\n\n" + ScenePath + "\n\nReplacing it discards anything you added to that scene.",
                    "Replace",
                    "Cancel");

                if (replace == false)
                {
                    return string.Empty;
                }
            }

            bool proceed = EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();

            if (proceed == false)
            {
                return string.Empty;
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildContents();
            ApplyRenderSettings();

            if (Directory.Exists(SceneFolder) == false)
            {
                Directory.CreateDirectory(SceneFolder);
                AssetDatabase.Refresh();
            }

            bool saved = EditorSceneManager.SaveScene(scene, ScenePath);

            if (saved == false)
            {
                Debug.LogError("WorkspaceSceneBuilder: failed to save the scene to " + ScenePath);
                return string.Empty;
            }

            AssetDatabase.Refresh();
            Debug.Log("Skin Fixer workspace scene created at " + ScenePath);

            return ScenePath;
        }

        private static SkinFixerWorkspace BuildContents()
        {
            GameObject root = new GameObject("SkinFixer Workspace");
            SkinFixerWorkspace workspace = root.AddComponent<SkinFixerWorkspace>();

            GameObject modelRoot = new GameObject("Models");
            modelRoot.transform.SetParent(root.transform, false);
            workspace.modelRoot = modelRoot.transform;

            GameObject lightingRoot = new GameObject("Lighting");
            lightingRoot.transform.SetParent(root.transform, false);
            workspace.lightingRoot = lightingRoot.transform;

            workspace.keyLight = CreateDirectionalLight(
                lightingRoot.transform,
                "Key Light",
                new Vector3(48f, 145f, 0f),
                new Color(1f, 0.976f, 0.937f, 1f),
                1.35f,
                LightShadows.Soft);

            workspace.fillLight = CreateDirectionalLight(
                lightingRoot.transform,
                "Fill Light",
                new Vector3(22f, 225f, 0f),
                new Color(0.804f, 0.867f, 1f, 1f),
                0.45f,
                LightShadows.None);

            workspace.rimLight = CreateDirectionalLight(
                lightingRoot.transform,
                "Rim Light",
                new Vector3(12f, 20f, 0f),
                new Color(1f, 0.941f, 0.855f, 1f),
                0.85f,
                LightShadows.None);

            workspace.groundPlane = CreateGround(root.transform);
            workspace.previewCamera = CreateCamera(root.transform, workspace.expectedModelHeight);

            Selection.activeGameObject = root;
            return workspace;
        }

        private static Light CreateDirectionalLight(
            Transform parent,
            string name,
            Vector3 eulerAngles,
            Color colour,
            float intensity,
            LightShadows shadows)
        {
            GameObject lightObject = new GameObject(name);
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.rotation = Quaternion.Euler(eulerAngles);

            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = colour;
            light.intensity = intensity;
            light.shadows = shadows;

            AddPipelineComponent(lightObject, "UnityEngine.Rendering.Universal.UniversalAdditionalLightData");

            return light;
        }

        private static GameObject CreateGround(Transform parent)
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground Reference";
            ground.transform.SetParent(parent, false);
            ground.transform.localScale = new Vector3(1f, 1f, 1f);

            Collider collider = ground.GetComponent<Collider>();

            if (collider != null)
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }

            return ground;
        }

        private static Camera CreateCamera(Transform parent, float expectedHeight)
        {
            GameObject cameraObject = new GameObject("Preview Camera");
            cameraObject.transform.SetParent(parent, false);
            cameraObject.tag = "MainCamera";

            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = BackgroundColour;
            camera.fieldOfView = 40f;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 500f;

            AddPipelineComponent(cameraObject, "UnityEngine.Rendering.Universal.UniversalAdditionalCameraData");

            PlaceCameraForHeight(camera, expectedHeight);

            return camera;
        }

        /// <summary>
        /// Frames a subject of the given height standing on the ground plane. Characters
        /// arrive at wildly different scales, so this is a starting point rather than a fit.
        /// </summary>
        public static void PlaceCameraForHeight(Camera camera, float height)
        {
            if (camera == null)
            {
                return;
            }

            float safeHeight = Mathf.Max(height, 0.01f);
            Vector3 focus = new Vector3(0f, safeHeight * 0.55f, 0f);
            Vector3 position = new Vector3(safeHeight * 0.9f, safeHeight * 0.75f, safeHeight * 1.9f);

            camera.transform.position = position;
            camera.transform.LookAt(focus);
        }

        /// <summary>
        /// Adds a render pipeline companion component when the pipeline is installed.
        /// Resolved by name so the tool does not take a hard dependency on URP and still
        /// compiles in a Built-in or HDRP project.
        /// </summary>
        private static void AddPipelineComponent(GameObject target, string typeName)
        {
            Type componentType = Type.GetType(typeName + ", Unity.RenderPipelines.Universal.Runtime");

            if (componentType == null)
            {
                return;
            }

            if (target.GetComponent(componentType) != null)
            {
                return;
            }

            target.AddComponent(componentType);
        }

        private static void ApplyRenderSettings()
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = AmbientColour;
            RenderSettings.fog = false;
        }

        /// <summary>Points the preview camera and the Scene view at whatever is loaded.</summary>
        public static bool FrameModel(SkinFixerWorkspace workspace)
        {
            if (workspace == null)
            {
                return false;
            }

            Bounds bounds = new Bounds(Vector3.zero, Vector3.one);
            bool found = workspace.TryGetModelBounds(out bounds);

            if (found == false)
            {
                return false;
            }

            float height = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));

            if (workspace.previewCamera != null)
            {
                float safeHeight = Mathf.Max(height, 0.01f);
                Vector3 position = bounds.center + new Vector3(safeHeight * 0.9f, safeHeight * 0.35f, safeHeight * 1.9f);
                workspace.previewCamera.transform.position = position;
                workspace.previewCamera.transform.LookAt(bounds.center);
            }

            if (SceneView.lastActiveSceneView != null)
            {
                SceneView.lastActiveSceneView.Frame(bounds, false);
            }

            return true;
        }
    }
}
