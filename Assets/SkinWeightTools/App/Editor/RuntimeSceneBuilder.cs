using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SkinWeightTools.App.EditorTools
{
    /// <summary>
    /// Generates the play mode scene, in the same spirit as WorkspaceSceneBuilder: the .unity
    /// file is written by the editor version in use rather than committed by hand, and can be
    /// regenerated whenever it drifts.
    /// </summary>
    public static class RuntimeSceneBuilder
    {
        public const string SceneFolder = "Assets/SkinWeightTools/Scenes";
        public const string ScenePath = "Assets/SkinWeightTools/Scenes/SkinFixer_Runtime.unity";

        private static readonly Color BackgroundColour = new Color(0.212f, 0.216f, 0.227f, 1f);
        private static readonly Color AmbientColour = new Color(0.29f, 0.30f, 0.33f, 1f);

        [MenuItem("Tools/Skin Weight Tools/Create Runtime Scene", false, 22)]
        public static void CreateRuntimeSceneMenu()
        {
            if (File.Exists(ScenePath) == true)
            {
                bool replace = EditorUtility.DisplayDialog(
                    "Replace runtime scene",
                    "A runtime scene already exists at\n\n" + ScenePath + "\n\nReplacing it discards anything you added to that scene.",
                    "Replace",
                    "Cancel");

                if (replace == false)
                {
                    return;
                }
            }

            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo() == false)
            {
                return;
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildContents();
            ApplyRenderSettings();

            if (Directory.Exists(SceneFolder) == false)
            {
                Directory.CreateDirectory(SceneFolder);
                AssetDatabase.Refresh();
            }

            if (EditorSceneManager.SaveScene(scene, ScenePath) == false)
            {
                Debug.LogError("RuntimeSceneBuilder: failed to save the scene to " + ScenePath);
                return;
            }

            AddToBuildSettings();
            AssetDatabase.Refresh();

            Debug.Log("Skin Fixer runtime scene created at " + ScenePath + ". Press Play, then Load Model.");
        }

        private static void BuildContents()
        {
            GameObject cameraObject = new GameObject("Preview Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = BackgroundColour;
            camera.fieldOfView = 45f;
            camera.tag = "MainCamera";
            cameraObject.AddComponent<AudioListener>();

            AddPipelineComponent(cameraObject, "UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");

            RuntimeOrbitCamera orbit = cameraObject.AddComponent<RuntimeOrbitCamera>();
            orbit.pivot = new Vector3(0f, 1f, 0f);
            orbit.distance = 3.5f;

            GameObject lighting = new GameObject("Lighting");

            CreateDirectionalLight(lighting.transform, "Key Light", new Vector3(48f, 145f, 0f), new Color(1f, 0.976f, 0.937f, 1f), 1.35f, LightShadows.Soft);
            CreateDirectionalLight(lighting.transform, "Fill Light", new Vector3(22f, 225f, 0f), new Color(0.804f, 0.867f, 1f, 1f), 0.55f, LightShadows.None);
            CreateDirectionalLight(lighting.transform, "Rim Light", new Vector3(12f, 25f, 0f), new Color(0.902f, 0.941f, 1f, 1f), 0.8f, LightShadows.None);

            GameObject appObject = new GameObject("SkinFixer App");

            GameObject modelRoot = new GameObject("Models");
            modelRoot.transform.SetParent(appObject.transform, false);

            SkinFixerRuntimeApp app = appObject.AddComponent<SkinFixerRuntimeApp>();
            GenericRomAnimator animator = appObject.AddComponent<GenericRomAnimator>();
            SkinFixerRuntimeUI ui = appObject.AddComponent<SkinFixerRuntimeUI>();

            app.modelRoot = modelRoot.transform;
            app.romAnimator = animator;
            app.orbitCamera = orbit;

            ui.app = app;
            ui.romAnimator = animator;
        }

        private static Light CreateDirectionalLight(
            Transform parent,
            string name,
            Vector3 eulerAngles,
            Color colour,
            float intensity,
            LightShadows shadows)
        {
            GameObject holder = new GameObject(name);
            holder.transform.SetParent(parent, false);
            holder.transform.rotation = Quaternion.Euler(eulerAngles);

            Light light = holder.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = colour;
            light.intensity = intensity;
            light.shadows = shadows;

            AddPipelineComponent(holder, "UnityEngine.Rendering.Universal.UniversalAdditionalLightData, Unity.RenderPipelines.Universal.Runtime");

            return light;
        }

        /// <summary>
        /// URP wants its own data component on cameras and lights. Resolved by name so the
        /// scene builder still works if the project is switched off URP later.
        /// </summary>
        private static void AddPipelineComponent(GameObject target, string typeName)
        {
            Type componentType = Type.GetType(typeName);

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
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = AmbientColour;
            RenderSettings.fog = false;
        }

        /// <summary>Without this the scene is missing from a player build and Load Model has nothing to run in.</summary>
        private static void AddToBuildSettings()
        {
            EditorBuildSettingsScene[] existing = EditorBuildSettings.scenes;

            for (int index = 0; index < existing.Length; index++)
            {
                if (existing[index].path == ScenePath)
                {
                    return;
                }
            }

            EditorBuildSettingsScene[] updated = new EditorBuildSettingsScene[existing.Length + 1];
            Array.Copy(existing, updated, existing.Length);
            updated[existing.Length] = new EditorBuildSettingsScene(ScenePath, true);

            EditorBuildSettings.scenes = updated;
        }
    }
}
