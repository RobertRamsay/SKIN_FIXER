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

        /// <summary>
        /// Adds the workspace rig to whatever scene is already open, adopting an existing
        /// main camera and directional light rather than leaving duplicates behind. Use this
        /// when you already have a scene you want to keep.
        /// </summary>
        [MenuItem("Tools/Skin Weight Tools/Add Workspace To Current Scene", false, 21)]
        public static void AddWorkspaceToCurrentSceneMenu()
        {
            SkinFixerWorkspace existing = SkinFixerWorkspace.FindInScene();

            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                EditorUtility.DisplayDialog(
                    "Workspace already present",
                    "This scene already contains a Skin Fixer workspace. It has been selected in the hierarchy.",
                    "OK");
                return;
            }

            SkinFixerWorkspace workspace = BuildContents(true);
            Undo.RegisterCreatedObjectUndo(workspace.gameObject, "Add Skin Fixer Workspace");
            ApplyRenderSettings();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            Debug.Log("Skin Fixer workspace added to the open scene. Save the scene to keep it.");
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

            BuildContents(false);
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

        private static SkinFixerWorkspace BuildContents(bool adoptExisting)
        {
            GameObject root = new GameObject("SkinFixer Workspace");
            SkinFixerWorkspace workspace = root.AddComponent<SkinFixerWorkspace>();

            GameObject modelRoot = new GameObject("Models");
            modelRoot.transform.SetParent(root.transform, false);
            workspace.modelRoot = modelRoot.transform;

            GameObject lightingRoot = new GameObject("Lighting");
            lightingRoot.transform.SetParent(root.transform, false);
            workspace.lightingRoot = lightingRoot.transform;

            Light adoptedKey = null;

            if (adoptExisting == true)
            {
                adoptedKey = FindExistingDirectionalLight();
            }

            workspace.keyLight = CreateDirectionalLight(
                lightingRoot.transform,
                "Key Light",
                new Vector3(48f, 145f, 0f),
                new Color(1f, 0.976f, 0.937f, 1f),
                1.35f,
                LightShadows.Soft,
                adoptedKey);

            workspace.fillLight = CreateDirectionalLight(
                lightingRoot.transform,
                "Fill Light",
                new Vector3(22f, 225f, 0f),
                new Color(0.804f, 0.867f, 1f, 1f),
                0.45f,
                LightShadows.None,
                null);

            workspace.rimLight = CreateDirectionalLight(
                lightingRoot.transform,
                "Rim Light",
                new Vector3(12f, 20f, 0f),
                new Color(1f, 0.941f, 0.855f, 1f),
                0.85f,
                LightShadows.None,
                null);

            workspace.groundPlane = CreateGround(root.transform);
            workspace.previewCamera = CreateCamera(root.transform, workspace.expectedModelHeight, adoptExisting);

            Selection.activeGameObject = root;
            return workspace;
        }

        /// <summary>First directional light in the open scene, or null when there is none.</summary>
        private static Light FindExistingDirectionalLight()
        {
            Light[] lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None);

            for (int index = 0; index < lights.Length; index++)
            {
                if (lights[index].type == LightType.Directional)
                {
                    return lights[index];
                }
            }

            return null;
        }

        private static Light CreateDirectionalLight(
            Transform parent,
            string name,
            Vector3 eulerAngles,
            Color colour,
            float intensity,
            LightShadows shadows,
            Light adopted)
        {
            GameObject lightObject = null;
            Light light = adopted;

            if (light != null)
            {
                lightObject = light.gameObject;
                Undo.RecordObject(light, "Configure Skin Fixer Light");
                Undo.RecordObject(lightObject.transform, "Configure Skin Fixer Light");
            }

            if (light == null)
            {
                lightObject = new GameObject(name);
                light = lightObject.AddComponent<Light>();
            }

            lightObject.transform.SetParent(parent, false);
            lightObject.transform.rotation = Quaternion.Euler(eulerAngles);

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

        private static Camera CreateCamera(Transform parent, float expectedHeight, bool adoptExisting)
        {
            GameObject cameraObject = null;
            Camera camera = null;

            if (adoptExisting == true)
            {
                camera = Camera.main;
            }

            if (camera != null)
            {
                cameraObject = camera.gameObject;
                Undo.RecordObject(camera, "Configure Skin Fixer Camera");
                Undo.RecordObject(cameraObject.transform, "Configure Skin Fixer Camera");
            }

            if (camera == null)
            {
                cameraObject = new GameObject("Preview Camera");
                camera = cameraObject.AddComponent<Camera>();
            }

            cameraObject.transform.SetParent(parent, false);
            cameraObject.tag = "MainCamera";

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
