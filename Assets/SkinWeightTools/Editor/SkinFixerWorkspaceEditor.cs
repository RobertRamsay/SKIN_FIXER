using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SkinWeightTools.EditorTools
{
    /// <summary>
    /// Inspector for the workspace object. Surfaces the controls that matter while judging
    /// deformation, so the common loop does not require bouncing between the hierarchy, the
    /// lighting objects and the Weight Editor window.
    /// </summary>
    [CustomEditor(typeof(SkinFixerWorkspace))]
    public class SkinFixerWorkspaceEditor : Editor
    {
        private bool showRawReferences = false;
        private List<SkinnedMeshRenderer> cachedRenderers = new List<SkinnedMeshRenderer>();

        public override void OnInspectorGUI()
        {
            SkinFixerWorkspace workspace = target as SkinFixerWorkspace;

            if (workspace == null)
            {
                return;
            }

            DrawModelSection(workspace);
            EditorGUILayout.Space();

            DrawViewSection(workspace);
            EditorGUILayout.Space();

            DrawLightingSection(workspace);
            EditorGUILayout.Space();

            showRawReferences = EditorGUILayout.Foldout(showRawReferences, "Raw references", true);

            if (showRawReferences == true)
            {
                DrawDefaultInspector();
            }
        }

        private void DrawModelSection(SkinFixerWorkspace workspace)
        {
            EditorGUILayout.LabelField("Models", EditorStyles.boldLabel);

            if (workspace.modelRoot == null)
            {
                EditorGUILayout.HelpBox(
                    "No Models root assigned. Assign one under Raw references, or rebuild the workspace from Tools > Skin Weight Tools.",
                    MessageType.Warning);
                return;
            }

            cachedRenderers.Clear();
            workspace.modelRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true, cachedRenderers);

            if (cachedRenderers.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "Nothing loaded. Use Import model file on the Weight Editor's Source tab, or drag a rig under the Models object.",
                    MessageType.Info);
            }

            for (int index = 0; index < cachedRenderers.Count; index++)
            {
                SkinnedMeshRenderer renderer = cachedRenderers[index];

                if (renderer == null)
                {
                    continue;
                }

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(renderer.name, EditorStyles.boldLabel);

                Mesh mesh = renderer.sharedMesh;

                if (mesh == null)
                {
                    EditorGUILayout.LabelField("No mesh assigned.", EditorStyles.miniLabel);
                }

                if (mesh != null)
                {
                    EditorGUILayout.LabelField(
                        "Vertices " + mesh.vertexCount.ToString() + "   Bones " + renderer.bones.Length.ToString(),
                        EditorStyles.miniLabel);

                    if (mesh.isReadable == false)
                    {
                        EditorGUILayout.HelpBox(
                            "Mesh is not readable. Enable Read/Write on its model importer before editing weights.",
                            MessageType.Warning);
                    }
                }

                EditorGUILayout.BeginHorizontal();

                if (GUILayout.Button("Edit weights") == true)
                {
                    SkinWeightToolsWindow.OpenWith(renderer);
                }

                if (GUILayout.Button("Select") == true)
                {
                    Selection.activeGameObject = renderer.gameObject;
                }

                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
            }
        }

        private void DrawViewSection(SkinFixerWorkspace workspace)
        {
            EditorGUILayout.LabelField("View", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Open Weight Editor") == true)
            {
                SkinWeightToolsWindow.ShowWindow();
            }

            if (GUILayout.Button("Frame model") == true)
            {
                bool framed = WorkspaceSceneBuilder.FrameModel(workspace);

                if (framed == false)
                {
                    Debug.Log("Skin Fixer: nothing under the Models root to frame yet.");
                }
            }

            EditorGUILayout.EndHorizontal();

            if (workspace.groundPlane != null)
            {
                bool groundVisible = workspace.groundPlane.activeSelf;
                bool nextVisible = EditorGUILayout.Toggle("Ground reference", groundVisible);

                if (nextVisible != groundVisible)
                {
                    Undo.RecordObject(workspace.groundPlane, "Toggle Ground");
                    workspace.SetGroundVisible(nextVisible);
                    EditorUtility.SetDirty(workspace.groundPlane);
                }
            }
        }

        private void DrawLightingSection(SkinFixerWorkspace workspace)
        {
            EditorGUILayout.LabelField("Lighting", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Raking the key light across a joint is the quickest way to spot a collapsing silhouette.",
                EditorStyles.wordWrappedMiniLabel);

            DrawLightSlider("Key", workspace.keyLight, 3f);
            DrawLightSlider("Fill", workspace.fillLight, 2f);
            DrawLightSlider("Rim", workspace.rimLight, 3f);

            if (workspace.lightingRoot != null)
            {
                float currentYaw = workspace.lightingRoot.localEulerAngles.y;
                float nextYaw = EditorGUILayout.Slider("Rig rotation", currentYaw, 0f, 360f);

                if (Mathf.Approximately(nextYaw, currentYaw) == false)
                {
                    Undo.RecordObject(workspace.lightingRoot, "Rotate Light Rig");
                    workspace.lightingRoot.localEulerAngles = new Vector3(0f, nextYaw, 0f);
                }
            }
        }

        private void DrawLightSlider(string label, Light light, float maximum)
        {
            if (light == null)
            {
                return;
            }

            float nextIntensity = EditorGUILayout.Slider(label, light.intensity, 0f, maximum);

            if (Mathf.Approximately(nextIntensity, light.intensity) == false)
            {
                Undo.RecordObject(light, "Set Light Intensity");
                light.intensity = nextIntensity;
                EditorUtility.SetDirty(light);
            }
        }
    }
}
