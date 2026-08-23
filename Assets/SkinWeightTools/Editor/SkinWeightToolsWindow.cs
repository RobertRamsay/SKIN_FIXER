using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SkinWeightTools.EditorTools
{
    public class SkinWeightToolsWindow : EditorWindow
    {
        private enum ToolTab
        {
            Source = 0,
            EvenOut = 1,
            Layers = 2,
            RangeOfMotion = 3
        }

        private ToolTab activeTab = ToolTab.Source;
        private Vector2 scrollPosition = new Vector2(0f, 0f);

        private SkinnedMeshRenderer targetRenderer = null;
        private Animator targetAnimator = null;
        private Mesh originalMesh = null;
        private Mesh workingMesh = null;
        private SkinWeightSet sourceSet = null;
        private SkinWeightSet resultSet = null;
        private TopologyGraph topologyGraph = null;

        private float weldTolerance = 0.0001f;
        private EvenOutSettings evenOutSettings = new EvenOutSettings();
        private FalloffPreset selectedPreset = FalloffPreset.Smooth;
        private WeightLayerStack layerStack = new WeightLayerStack();

        private RomSequence romSequence = RomSequence.BuildDefaultHumanoid();
        private RomReport romReport = null;
        private float problemThreshold = 0.25f;
        private float allJointAngle = 60f;

        private string statusMessage = "Select a Skinned Mesh Renderer to begin.";

        [MenuItem("Tools/Skin Weight Tools/Weight Editor")]
        public static void ShowWindow()
        {
            SkinWeightToolsWindow window = GetWindow<SkinWeightToolsWindow>();
            window.titleContent = new GUIContent("Skin Weights");
            window.minSize = new Vector2(420f, 520f);
            window.Show();
        }

        private void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            DrawHeader();
            EditorGUILayout.Space();

            string[] tabNames = new string[] { "Source", "Even Out", "Layers", "ROM" };
            activeTab = (ToolTab)GUILayout.Toolbar((int)activeTab, tabNames);
            EditorGUILayout.Space();

            if (activeTab == ToolTab.Source)
            {
                DrawSourceTab();
            }

            if (activeTab == ToolTab.EvenOut)
            {
                DrawEvenOutTab();
            }

            if (activeTab == ToolTab.Layers)
            {
                DrawLayersTab();
            }

            if (activeTab == ToolTab.RangeOfMotion)
            {
                DrawRomTab();
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(statusMessage, MessageType.Info);

            EditorGUILayout.EndScrollView();
        }

        private void DrawHeader()
        {
            EditorGUILayout.LabelField("Skin Weight Tools", EditorStyles.boldLabel);

            SkinnedMeshRenderer nextRenderer = EditorGUILayout.ObjectField(
                "Skinned Mesh Renderer",
                targetRenderer,
                typeof(SkinnedMeshRenderer),
                true) as SkinnedMeshRenderer;

            if (nextRenderer != targetRenderer)
            {
                targetRenderer = nextRenderer;
                BindRenderer();
            }

            targetAnimator = EditorGUILayout.ObjectField(
                "Animator",
                targetAnimator,
                typeof(Animator),
                true) as Animator;
        }

        private void DrawSourceTab()
        {
            EditorGUILayout.LabelField("Import", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "FBX and USD use Unity's own importers. glTF and GLB need com.unity.cloud.gltfast.",
                EditorStyles.wordWrappedMiniLabel);

            if (GUILayout.Button("Import model file") == true)
            {
                RunImport();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Format support", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("FBX", "Built in");
            EditorGUILayout.LabelField("glTF and GLB", DescribePackage(SkinnedMeshImportUtility.IsGltfPackagePresent()));
            EditorGUILayout.LabelField("USD", DescribePackage(SkinnedMeshImportUtility.IsUsdPackagePresent()));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Topology", EditorStyles.boldLabel);
            weldTolerance = EditorGUILayout.FloatField("Weld tolerance", weldTolerance);

            if (GUILayout.Button("Analyse mesh") == true)
            {
                AnalyseMesh();
            }

            EditorGUILayout.Space();

            if (sourceSet != null)
            {
                EditorGUILayout.LabelField("Vertices", sourceSet.vertexCount.ToString());
                EditorGUILayout.LabelField("Bones", sourceSet.boneCount.ToString());
                EditorGUILayout.LabelField("Max influences", sourceSet.MaxInfluenceCount().ToString());
                EditorGUILayout.LabelField("Average influences", sourceSet.AverageInfluenceCount().ToString("F2"));
            }

            if (topologyGraph != null)
            {
                EditorGUILayout.LabelField("Welded groups", topologyGraph.groupCount.ToString());
                EditorGUILayout.LabelField("Average edge length", topologyGraph.averageEdgeLength.ToString("F5"));
                EditorGUILayout.LabelField("Shortest edge length", topologyGraph.shortestEdgeLength.ToString("F5"));

                float weldRatio = 1f;

                if (topologyGraph.vertexCount > 0)
                {
                    weldRatio = (float)topologyGraph.groupCount / (float)topologyGraph.vertexCount;
                }

                if (weldRatio > 0.98f)
                {
                    EditorGUILayout.HelpBox(
                        "Almost no vertices welded. If this mesh has UV or normal seams, topological distance will not cross them. Try raising the weld tolerance and analysing again.",
                        MessageType.Warning);
                }
            }

            EditorGUILayout.Space();

            if (GUILayout.Button("Revert renderer to original mesh") == true)
            {
                RevertToOriginal();
            }
        }

        private string DescribePackage(bool present)
        {
            if (present == true)
            {
                return "Package found";
            }

            return "Package not installed";
        }

        private void DrawEvenOutTab()
        {
            if (topologyGraph == null)
            {
                EditorGUILayout.HelpBox("Run Analyse mesh on the Source tab first.", MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField("Even Out", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Each press re-solves from the original weights, so presses do not stack.",
                EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("EVEN OUT 2") == true)
            {
                evenOutSettings.maxInfluences = 2;
                RunEvenOut();
            }

            if (GUILayout.Button("EVEN OUT 3") == true)
            {
                evenOutSettings.maxInfluences = 3;
                RunEvenOut();
            }

            if (GUILayout.Button("EVEN OUT 4") == true)
            {
                evenOutSettings.maxInfluences = 4;
                RunEvenOut();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Topological distance", EditorStyles.boldLabel);

            evenOutSettings.useHopCount = EditorGUILayout.Toggle("Use edge hops", evenOutSettings.useHopCount);

            if (evenOutSettings.useHopCount == true)
            {
                evenOutSettings.searchRadius = EditorGUILayout.Slider(
                    "Search radius (hops)", evenOutSettings.searchRadius, 1f, 24f);
            }

            if (evenOutSettings.useHopCount == false)
            {
                float suggested = topologyGraph.averageEdgeLength * 24f;

                if (suggested <= 0f)
                {
                    suggested = 1f;
                }

                evenOutSettings.searchRadius = EditorGUILayout.Slider(
                    "Search radius (m)", evenOutSettings.searchRadius, 0.0001f, suggested);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Falloff", EditorStyles.boldLabel);

            FalloffPreset nextPreset = (FalloffPreset)EditorGUILayout.EnumPopup("Preset", selectedPreset);

            if (nextPreset != selectedPreset)
            {
                selectedPreset = nextPreset;
                evenOutSettings.ApplyFalloffPreset(selectedPreset);
            }

            evenOutSettings.falloff = EditorGUILayout.CurveField("Falloff curve", evenOutSettings.falloff);
            evenOutSettings.falloffBlend = EditorGUILayout.Slider("Falloff blend", evenOutSettings.falloffBlend, 0f, 1f);
            evenOutSettings.newBoneBias = EditorGUILayout.Slider("New bone bias", evenOutSettings.newBoneBias, 0f, 1f);

            EditorGUILayout.LabelField(
                "Falloff blend at 0 is a plain top N cull. Raise it to let the distance field redistribute.",
                EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Smoothing and thresholds", EditorStyles.boldLabel);

            evenOutSettings.smoothIterations = EditorGUILayout.IntSlider("Smooth iterations", evenOutSettings.smoothIterations, 0, 8);
            evenOutSettings.smoothStrength = EditorGUILayout.Slider("Smooth strength", evenOutSettings.smoothStrength, 0f, 1f);
            evenOutSettings.seedThreshold = EditorGUILayout.Slider("Seed threshold", evenOutSettings.seedThreshold, 0.05f, 0.95f);
            evenOutSettings.weightThreshold = EditorGUILayout.Slider("Weight threshold", evenOutSettings.weightThreshold, 0.00001f, 0.05f);
            evenOutSettings.preserveDominantBone = EditorGUILayout.Toggle("Preserve dominant bone", evenOutSettings.preserveDominantBone);
            evenOutSettings.useDominantFallbackSeeding = EditorGUILayout.Toggle("Dominant fallback seeding", evenOutSettings.useDominantFallbackSeeding);

            EditorGUILayout.Space();

            if (GUILayout.Button("Re-run with current settings") == true)
            {
                RunEvenOut();
            }

            DrawBakeControls();
        }

        private void DrawLayersTab()
        {
            if (topologyGraph == null)
            {
                EditorGUILayout.HelpBox("Run Analyse mesh on the Source tab first.", MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField("Layer stack", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Layers evaluate in order on top of the Even Out result.",
                EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Add Tightener") == true)
            {
                layerStack.AddLayer(WeightLayerType.Tighten);
            }

            if (GUILayout.Button("Add Loosener") == true)
            {
                layerStack.AddLayer(WeightLayerType.Loosen);
            }

            if (GUILayout.Button("Add Smoother") == true)
            {
                layerStack.AddLayer(WeightLayerType.Smooth);
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();

            int removeIndex = -1;
            int moveFrom = -1;
            int moveTo = -1;

            for (int index = 0; index < layerStack.layers.Count; index++)
            {
                WeightLayer layer = layerStack.layers[index];

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                EditorGUILayout.BeginHorizontal();
                layer.enabled = EditorGUILayout.Toggle(layer.enabled, GUILayout.Width(18f));
                layer.layerName = EditorGUILayout.TextField(layer.layerName);

                if (GUILayout.Button("Up", GUILayout.Width(32f)) == true)
                {
                    moveFrom = index;
                    moveTo = index - 1;
                }

                if (GUILayout.Button("Dn", GUILayout.Width(32f)) == true)
                {
                    moveFrom = index;
                    moveTo = index + 1;
                }

                if (GUILayout.Button("X", GUILayout.Width(24f)) == true)
                {
                    removeIndex = index;
                }

                EditorGUILayout.EndHorizontal();

                layer.layerType = (WeightLayerType)EditorGUILayout.EnumPopup("Type", layer.layerType);
                layer.strength = EditorGUILayout.Slider("Strength", layer.strength, 0f, 1f);

                if (layer.layerType != WeightLayerType.Smooth && layer.layerType != WeightLayerType.Normalize)
                {
                    layer.sharpness = EditorGUILayout.Slider("Sharpness", layer.sharpness, 1f, 8f);
                }

                layer.useHopCount = EditorGUILayout.Toggle("Use edge hops", layer.useHopCount);
                layer.maskRadius = EditorGUILayout.FloatField("Mask radius", layer.maskRadius);
                layer.maskFalloff = EditorGUILayout.CurveField("Mask falloff", layer.maskFalloff);
                layer.maxInfluences = EditorGUILayout.IntSlider("Max influences", layer.maxInfluences, 1, 8);

                EditorGUILayout.LabelField("Mask vertices", layer.maskVertices.Length.ToString());

                if (layer.maskVertices.Length == 0)
                {
                    EditorGUILayout.LabelField("An empty mask affects the whole mesh.", EditorStyles.miniLabel);
                }

                if (romReport != null)
                {
                    if (GUILayout.Button("Fill mask from ROM problem areas") == true)
                    {
                        layer.maskVertices = RomTestRunner.ExtractProblemVertices(romReport, topologyGraph, problemThreshold);
                        statusMessage = "Mask filled with " + layer.maskVertices.Length.ToString() + " vertices.";
                    }
                }

                if (layer.maskVertices.Length > 0)
                {
                    if (GUILayout.Button("Clear mask") == true)
                    {
                        layer.maskVertices = new int[0];
                    }
                }

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space();
            }

            if (removeIndex >= 0)
            {
                layerStack.layers.RemoveAt(removeIndex);
            }

            if (moveFrom >= 0)
            {
                layerStack.MoveLayer(moveFrom, moveTo);
            }

            if (GUILayout.Button("Evaluate layer stack") == true)
            {
                EvaluateLayers();
            }

            DrawBakeControls();
        }

        private void DrawRomTab()
        {
            EditorGUILayout.LabelField("Range of motion test", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Needs a Humanoid Animator. Bones are restored to their rest pose afterwards.",
                EditorStyles.wordWrappedMiniLabel);

            romSequence.symmetry = (RomSymmetry)EditorGUILayout.EnumPopup("Sequence", romSequence.symmetry);
            romSequence.samplesPerStep = EditorGUILayout.IntSlider("Samples per step", romSequence.samplesPerStep, 2, 24);

            EditorGUILayout.BeginHorizontal();
            allJointAngle = EditorGUILayout.Slider("Angle for all joints", allJointAngle, 5f, 120f);

            if (GUILayout.Button("Apply", GUILayout.Width(60f)) == true)
            {
                romSequence.SetAllAngles(allJointAngle);
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Joints", EditorStyles.boldLabel);

            for (int index = 0; index < romSequence.joints.Count; index++)
            {
                RomJointDefinition joint = romSequence.joints[index];

                EditorGUILayout.BeginHorizontal();
                joint.enabled = EditorGUILayout.Toggle(joint.enabled, GUILayout.Width(18f));
                EditorGUILayout.LabelField(joint.label, GUILayout.Width(70f));
                joint.bendAngle = EditorGUILayout.Slider(joint.bendAngle, 0f, 150f);
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space();

            if (GUILayout.Button("Run ROM test") == true)
            {
                RunRomTest();
            }

            if (romReport != null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Results", EditorStyles.boldLabel);

                if (romReport.completed == false)
                {
                    EditorGUILayout.HelpBox("The last test was cancelled, so results are partial.", MessageType.Warning);
                }

                EditorGUILayout.LabelField("Worst step", romReport.worstStepLabel);
                EditorGUILayout.LabelField("Worst distortion", romReport.worstDistortion.ToString("F3"));

                for (int index = 0; index < romReport.stepLabels.Count; index++)
                {
                    EditorGUILayout.LabelField(romReport.stepLabels[index], romReport.stepDistortion[index].ToString("F3"));
                }

                EditorGUILayout.Space();
                problemThreshold = EditorGUILayout.Slider("Problem threshold", problemThreshold, 0.01f, 1f);

                int problemCount = RomTestRunner.ExtractProblemVertices(romReport, topologyGraph, problemThreshold).Length;
                EditorGUILayout.LabelField("Vertices over threshold", problemCount.ToString());
                EditorGUILayout.LabelField(
                    "Use the Layers tab to push these into a tightener or smoother mask.",
                    EditorStyles.wordWrappedMiniLabel);
            }
        }

        private void DrawBakeControls()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);

            if (GUILayout.Button("Bake result to mesh asset") == true)
            {
                BakeToAsset();
            }
        }

        private void RunImport()
        {
            string chosen = SkinnedMeshImportUtility.OpenImportPanel();

            if (string.IsNullOrEmpty(chosen) == true)
            {
                return;
            }

            string assetPath = SkinnedMeshImportUtility.ImportExternalFile(chosen);

            if (string.IsNullOrEmpty(assetPath) == true)
            {
                return;
            }

            List<SkinnedMeshRenderer> renderers = SkinnedMeshImportUtility.InstantiateAndCollect(assetPath);

            if (renderers.Count > 0)
            {
                targetRenderer = renderers[0];
                targetAnimator = targetRenderer.GetComponentInParent<Animator>();
                BindRenderer();
            }

            statusMessage = "Imported " + assetPath + " carrying " + renderers.Count.ToString() + " skinned renderers.";
        }

        private void BindRenderer()
        {
            sourceSet = null;
            resultSet = null;
            topologyGraph = null;
            romReport = null;

            MeshWeightWriter.DisposeIfUnsaved(workingMesh);
            workingMesh = null;

            if (targetRenderer == null)
            {
                originalMesh = null;
                statusMessage = "No renderer selected.";
                return;
            }

            originalMesh = targetRenderer.sharedMesh;

            if (targetAnimator == null)
            {
                targetAnimator = targetRenderer.GetComponentInParent<Animator>();
            }

            statusMessage = "Bound to " + targetRenderer.name + ". Run Analyse mesh next.";
        }

        private void AnalyseMesh()
        {
            if (targetRenderer == null || originalMesh == null)
            {
                statusMessage = "Select a Skinned Mesh Renderer first.";
                return;
            }

            if (originalMesh.isReadable == false)
            {
                statusMessage = "Mesh is not readable. Enable Read/Write on the model importer, then analyse again.";
                return;
            }

            sourceSet = SkinWeightSet.FromMesh(originalMesh);
            topologyGraph = TopologyGraph.Build(originalMesh, weldTolerance);

            if (evenOutSettings.useHopCount == false && topologyGraph.averageEdgeLength > 0f)
            {
                evenOutSettings.searchRadius = topologyGraph.averageEdgeLength * 6f;
            }

            statusMessage = "Analysed " + sourceSet.vertexCount.ToString() + " vertices into "
                + topologyGraph.groupCount.ToString() + " welded groups.";
        }

        private void RunEvenOut()
        {
            if (sourceSet == null || topologyGraph == null)
            {
                statusMessage = "Run Analyse mesh first.";
                return;
            }

            try
            {
                resultSet = SkinWeightSolver.EvenOut(sourceSet, topologyGraph, evenOutSettings, null, ReportProgress);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            PushToWorkingMesh();

            statusMessage = "Even Out " + evenOutSettings.maxInfluences.ToString()
                + " applied. Average influences now " + resultSet.AverageInfluenceCount().ToString("F2") + ".";
        }

        private void EvaluateLayers()
        {
            if (sourceSet == null || topologyGraph == null)
            {
                statusMessage = "Run Analyse mesh first.";
                return;
            }

            SkinWeightSet baseSet = sourceSet;

            if (resultSet != null)
            {
                baseSet = resultSet;
            }

            resultSet = layerStack.Evaluate(baseSet, topologyGraph);

            PushToWorkingMesh();
            statusMessage = "Evaluated " + layerStack.layers.Count.ToString() + " layers.";
        }

        private void PushToWorkingMesh()
        {
            if (originalMesh == null || resultSet == null)
            {
                return;
            }

            if (workingMesh == null)
            {
                workingMesh = MeshWeightWriter.CreateWorkingCopy(originalMesh);
            }

            MeshWeightWriter.ApplyWeights(workingMesh, resultSet);
            MeshWeightWriter.AssignToRenderer(targetRenderer, workingMesh);
            SceneView.RepaintAll();
        }

        private void RevertToOriginal()
        {
            if (targetRenderer == null || originalMesh == null)
            {
                return;
            }

            MeshWeightWriter.AssignToRenderer(targetRenderer, originalMesh);
            resultSet = null;
            statusMessage = "Reverted to the imported mesh.";
            SceneView.RepaintAll();
        }

        private void BakeToAsset()
        {
            if (workingMesh == null || resultSet == null)
            {
                statusMessage = "Nothing to bake. Run Even Out or evaluate layers first.";
                return;
            }

            Mesh saveCopy = MeshWeightWriter.CreateWorkingCopy(workingMesh);
            saveCopy.name = originalMesh.name + "_evened" + evenOutSettings.maxInfluences.ToString();

            string path = MeshWeightWriter.SaveAsAsset(saveCopy);
            MeshWeightWriter.AssignToRenderer(targetRenderer, saveCopy);

            MeshWeightWriter.DisposeIfUnsaved(workingMesh);
            workingMesh = saveCopy;

            statusMessage = "Saved to " + path;
        }

        private void RunRomTest()
        {
            if (targetRenderer == null || topologyGraph == null)
            {
                statusMessage = "ROM test needs a renderer and an analysed mesh.";
                return;
            }

            if (targetAnimator == null)
            {
                statusMessage = "ROM test needs an Animator. Assign one at the top of the window.";
                return;
            }

            romReport = RomTestRunner.Run(targetRenderer, targetAnimator, topologyGraph, romSequence);

            if (romReport.stepLabels.Count == 0)
            {
                statusMessage = "ROM test produced no results. Check the Console for the reason.";
                return;
            }

            statusMessage = "ROM test finished. Worst distortion " + romReport.worstDistortion.ToString("F3")
                + " at " + romReport.worstStepLabel + ".";
            SceneView.RepaintAll();
        }

        private void ReportProgress(float value, string label)
        {
            EditorUtility.DisplayProgressBar("Even Out", label, value);
        }

        private void OnDestroy()
        {
            MeshWeightWriter.DisposeIfUnsaved(workingMesh);
            workingMesh = null;
        }
    }
}
