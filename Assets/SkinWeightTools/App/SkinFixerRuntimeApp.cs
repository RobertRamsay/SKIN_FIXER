using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace SkinWeightTools.App
{
    /// <summary>
    /// One skinned mesh under repair. The imported mesh is kept untouched so Revert always
    /// has something to go back to, and every edit lands on a working clone.
    /// </summary>
    public class MeshTarget
    {
        public SkinnedMeshRenderer renderer = null;
        public Mesh originalMesh = null;
        public Mesh workingMesh = null;
        public SkinWeightSet sourceSet = null;
        public SkinWeightSet currentSet = null;
        public TopologyGraph graph = null;

        public bool IsAnalysed()
        {
            return sourceSet != null && graph != null;
        }
    }

    /// <summary>
    /// Runtime front end for the weight tools. Pick a file, watch the rig move, and press a
    /// button to re-solve the weights while it keeps moving. The solver itself is the same
    /// code the editor window drives; only the loading, animation and mesh writing differ.
    /// </summary>
    public class SkinFixerRuntimeApp : MonoBehaviour
    {
        [Header("Scene")]
        [Tooltip("Loaded models are parented here. Falls back to this transform.")]
        public Transform modelRoot = null;

        public GenericRomAnimator romAnimator = null;
        public RuntimeOrbitCamera orbitCamera = null;

        [Header("Solve settings")]
        public EvenOutSettings evenOutSettings = new EvenOutSettings();

        [Tooltip("Vertices closer together than this are welded before adjacency is built.")]
        public float weldTolerance = 0.0001f;

        [Range(0f, 1f)]
        [Tooltip("Strength of a single smoothing pass, applied per button press.")]
        public float smoothStrength = 0.5f;

        /// <summary>Raised whenever the status line changes, so the UI does not have to poll.</summary>
        public event Action<string> StatusChanged;

        /// <summary>Raised when a model finishes loading or is cleared.</summary>
        public event Action ModelChanged;

        private readonly List<MeshTarget> targets = new List<MeshTarget>();
        private LoadedModel loaded = null;
        private bool busy = false;
        private int smoothPasses = 0;
        private string status = "Press Load Model to begin.";

        public string Status
        {
            get { return status; }
        }

        public bool IsBusy
        {
            get { return busy; }
        }

        public bool HasModel
        {
            get { return loaded != null && loaded.IsValid(); }
        }

        public int SmoothPasses
        {
            get { return smoothPasses; }
        }

        public IReadOnlyList<MeshTarget> Targets
        {
            get { return targets; }
        }

        private void Awake()
        {
            if (modelRoot == null)
            {
                modelRoot = transform;
            }
        }

        private void SetStatus(string message)
        {
            status = message;

            if (StatusChanged != null)
            {
                StatusChanged(message);
            }
        }

        // ------------------------------------------------------------------ loading

        public void LoadModelViaDialog()
        {
            if (busy == true)
            {
                return;
            }

            string path = RuntimeFileDialog.OpenFile("Open a skinned model", RuntimeFileDialog.ModelFilter);

            if (string.IsNullOrEmpty(path) == true)
            {
                SetStatus("Load cancelled.");
                return;
            }

            LoadModel(path);
        }

        public async void LoadModel(string path)
        {
            if (busy == true)
            {
                return;
            }

            busy = true;

            try
            {
                ClearModel();
                SetStatus("Loading " + System.IO.Path.GetFileName(path) + "...");

                LoadedModel result = await RuntimeModelLoader.LoadAsync(path, modelRoot);

                if (result.IsValid() == false)
                {
                    if (result.root != null)
                    {
                        Destroy(result.root);
                    }

                    SetStatus(result.error);
                    return;
                }

                loaded = result;
                BuildTargets(result);

                if (romAnimator != null)
                {
                    romAnimator.Bind(result.renderers);
                }

                FrameCamera();

                SetStatus("Loaded " + result.renderers.Count.ToString() + " skinned mesh(es). Analysing topology...");

                // The UI only redraws between frames, so let the status above appear before
                // the blocking Dijkstra work starts.
                await Task.Yield();

                await AnalyseAllAsync();

                if (ModelChanged != null)
                {
                    ModelChanged();
                }
            }
            catch (Exception error)
            {
                SetStatus("Load failed: " + error.Message);
                Debug.LogException(error);
            }
            finally
            {
                busy = false;
            }
        }

        private void BuildTargets(LoadedModel result)
        {
            targets.Clear();

            for (int index = 0; index < result.renderers.Count; index++)
            {
                MeshTarget target = new MeshTarget();
                target.renderer = result.renderers[index];
                target.originalMesh = target.renderer.sharedMesh;
                targets.Add(target);
            }
        }

        private async Task AnalyseAllAsync()
        {
            int totalVertices = 0;
            int totalGroups = 0;

            for (int index = 0; index < targets.Count; index++)
            {
                MeshTarget target = targets[index];

                SetStatus("Analysing mesh " + (index + 1).ToString() + " of " + targets.Count.ToString()
                    + " (" + target.originalMesh.vertexCount.ToString() + " verts)...");

                await Task.Yield();

                target.sourceSet = SkinWeightSet.FromMesh(target.originalMesh);
                target.graph = TopologyGraph.Build(target.originalMesh, weldTolerance);
                target.currentSet = target.sourceSet.Clone();

                totalVertices = totalVertices + target.sourceSet.vertexCount;
                totalGroups = totalGroups + target.graph.groupCount;
            }

            // Radius is in metres, and the sensible default depends on how dense this
            // particular mesh is. Six average edges reaches across a joint without spanning a limb.
            if (evenOutSettings.useHopCount == false && targets.Count > 0)
            {
                float averageEdge = 0f;
                int counted = 0;

                for (int index = 0; index < targets.Count; index++)
                {
                    if (targets[index].graph.averageEdgeLength > 0f)
                    {
                        averageEdge = averageEdge + targets[index].graph.averageEdgeLength;
                        counted = counted + 1;
                    }
                }

                if (counted > 0)
                {
                    evenOutSettings.searchRadius = (averageEdge / counted) * 6f;
                }
            }

            smoothPasses = 0;

            SetStatus("Ready. " + totalVertices.ToString() + " verts, " + totalGroups.ToString()
                + " welded groups. Press Play, then Even Out or Smooth.");
        }

        public void ClearModel()
        {
            if (romAnimator != null)
            {
                romAnimator.Stop();
            }

            for (int index = 0; index < targets.Count; index++)
            {
                RuntimeMeshWeightWriter.Destroy(targets[index].workingMesh);
            }

            targets.Clear();

            if (loaded != null && loaded.root != null)
            {
                Destroy(loaded.root);
            }

            loaded = null;
            smoothPasses = 0;
        }

        // ------------------------------------------------------------------ solving

        /// <summary>
        /// Re-solves from the original weights every press, so pressing Even Out 3 after
        /// Even Out 4 gives the same result as pressing it first. Stacking is what the
        /// Smooth button is for.
        /// </summary>
        public void ApplyEvenOut(int maxInfluences)
        {
            if (RequireAnalysed() == false)
            {
                return;
            }

            evenOutSettings.maxInfluences = maxInfluences;
            smoothPasses = 0;

            for (int index = 0; index < targets.Count; index++)
            {
                MeshTarget target = targets[index];
                target.currentSet = SkinWeightSolver.EvenOut(target.sourceSet, target.graph, evenOutSettings, null, null);
                PushToMesh(target);
            }

            SetStatus("Even Out " + maxInfluences.ToString() + " applied. Average influences now "
                + AverageInfluences().ToString("F2") + ".");
        }

        /// <summary>One Laplacian pass over the current weights. Presses accumulate.</summary>
        public void ApplySmooth()
        {
            if (RequireAnalysed() == false)
            {
                return;
            }

            for (int index = 0; index < targets.Count; index++)
            {
                MeshTarget target = targets[index];
                SkinWeightSolver.SmoothWeights(target.currentSet, target.graph, 1, smoothStrength, null);
                target.currentSet.NormalizeAll();
                PushToMesh(target);
            }

            smoothPasses = smoothPasses + 1;

            SetStatus("Smooth pass " + smoothPasses.ToString() + " at strength "
                + smoothStrength.ToString("F2") + ". Average influences now "
                + AverageInfluences().ToString("F2") + ".");
        }

        /// <summary>
        /// Smoothing raises the influence count, because a vertex picks up whatever its
        /// neighbours carry. This trims back to the influence budget without re-solving.
        /// </summary>
        public void CullToBudget()
        {
            if (RequireAnalysed() == false)
            {
                return;
            }

            for (int index = 0; index < targets.Count; index++)
            {
                MeshTarget target = targets[index];

                for (int vertex = 0; vertex < target.currentSet.vertexCount; vertex++)
                {
                    target.currentSet.CullVertex(vertex, evenOutSettings.maxInfluences);
                }

                PushToMesh(target);
            }

            SetStatus("Culled to " + evenOutSettings.maxInfluences.ToString()
                + " influences. Average influences now " + AverageInfluences().ToString("F2") + ".");
        }

        public void Revert()
        {
            if (RequireAnalysed() == false)
            {
                return;
            }

            smoothPasses = 0;

            for (int index = 0; index < targets.Count; index++)
            {
                MeshTarget target = targets[index];
                target.currentSet = target.sourceSet.Clone();
                RuntimeMeshWeightWriter.AssignToRenderer(target.renderer, target.originalMesh);
            }

            SetStatus("Reverted to the weights the file shipped with.");
        }

        private bool RequireAnalysed()
        {
            if (busy == true)
            {
                SetStatus("Still working. Wait for the current step to finish.");
                return false;
            }

            if (targets.Count == 0)
            {
                SetStatus("Load a model first.");
                return false;
            }

            for (int index = 0; index < targets.Count; index++)
            {
                if (targets[index].IsAnalysed() == false)
                {
                    SetStatus("Topology is not analysed yet.");
                    return false;
                }
            }

            return true;
        }

        private void PushToMesh(MeshTarget target)
        {
            if (target.workingMesh == null)
            {
                target.workingMesh = RuntimeMeshWeightWriter.CreateWorkingCopy(target.originalMesh);
            }

            RuntimeMeshWeightWriter.ApplyWeights(target.workingMesh, target.currentSet);
            RuntimeMeshWeightWriter.AssignToRenderer(target.renderer, target.workingMesh);
        }

        private float AverageInfluences()
        {
            float total = 0f;
            int counted = 0;

            for (int index = 0; index < targets.Count; index++)
            {
                SkinWeightSet set = targets[index].currentSet;

                if (set == null)
                {
                    continue;
                }

                total = total + set.AverageInfluenceCount();
                counted = counted + 1;
            }

            if (counted == 0)
            {
                return 0f;
            }

            return total / (float)counted;
        }

        // ------------------------------------------------------------------ camera

        private void FrameCamera()
        {
            if (orbitCamera == null || loaded == null || loaded.root == null)
            {
                return;
            }

            Bounds bounds = new Bounds(loaded.root.transform.position, Vector3.one);
            Renderer[] renderers = loaded.root.GetComponentsInChildren<Renderer>(true);
            bool started = false;

            for (int index = 0; index < renderers.Length; index++)
            {
                if (started == false)
                {
                    bounds = renderers[index].bounds;
                    started = true;
                    continue;
                }

                bounds.Encapsulate(renderers[index].bounds);
            }

            if (started == true)
            {
                orbitCamera.Frame(bounds);
            }
        }

        private void OnDestroy()
        {
            for (int index = 0; index < targets.Count; index++)
            {
                RuntimeMeshWeightWriter.Destroy(targets[index].workingMesh);
            }
        }
    }
}
