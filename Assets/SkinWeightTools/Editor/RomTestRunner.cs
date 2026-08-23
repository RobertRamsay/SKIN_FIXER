using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SkinWeightTools.EditorTools
{
    public class RomVertexResult
    {
        public int vertexIndex = 0;
        public float distortion = 0f;
    }

    public class RomReport
    {
        public bool completed = false;
        public float worstDistortion = 0f;
        public string worstStepLabel = "";
        public List<string> stepLabels = new List<string>();
        public List<float> stepDistortion = new List<float>();
        public float[] vertexDistortion = new float[0];
        public List<RomVertexResult> worstVertices = new List<RomVertexResult>();
    }

    /// <summary>
    /// Drives each joint through its range, bakes the deformed mesh at every sample and
    /// measures edge length distortion against the rest pose. Skinning is rigid per edge
    /// in a healthy rig, so clusters of high distortion mark where the weights are failing.
    /// </summary>
    public static class RomTestRunner
    {
        public static RomReport Run(
            SkinnedMeshRenderer renderer,
            Animator animator,
            TopologyGraph graph,
            RomSequence sequence)
        {
            RomReport report = new RomReport();

            if (renderer == null || animator == null || graph == null || sequence == null)
            {
                Debug.LogError("RomTestRunner: renderer, animator, graph and sequence are all required.");
                return report;
            }

            if (animator.isHuman == false)
            {
                Debug.LogError("RomTestRunner: the Animator is not configured as Humanoid, so joint lookups return nothing. Set the model's Animation Type to Humanoid, or drive the bones manually.");
                return report;
            }

            List<RomStep> steps = sequence.BuildSteps();

            if (steps.Count == 0)
            {
                Debug.LogWarning("RomTestRunner: no joints are enabled.");
                return report;
            }

            List<Transform> drivenTransforms = sequence.CollectDrivenTransforms(animator);

            if (drivenTransforms.Count == 0)
            {
                Debug.LogError("RomTestRunner: none of the requested joints resolved on this rig.");
                return report;
            }

            Undo.RecordObjects(drivenTransforms.ToArray(), "ROM Test");

            Dictionary<HumanBodyBones, Quaternion> restRotations = sequence.CaptureRest(animator);

            Mesh restBake = new Mesh();
            Mesh poseBake = new Mesh();

            try
            {
                renderer.BakeMesh(restBake, true);
                Vector3[] restPositions = restBake.vertices;

                float[] restEdgeLengths = BuildEdgeLengths(graph, restPositions);
                report.vertexDistortion = new float[graph.vertexCount];

                bool cancelled = false;

                for (int stepIndex = 0; stepIndex < steps.Count; stepIndex++)
                {
                    RomStep step = steps[stepIndex];
                    float stepWorst = 0f;

                    for (int sample = 1; sample <= sequence.samplesPerStep; sample++)
                    {
                        float phase = (float)sample / (float)sequence.samplesPerStep;
                        float fraction = (float)stepIndex / (float)steps.Count;

                        bool pressedCancel = EditorUtility.DisplayCancelableProgressBar(
                            "ROM test",
                            step.label + "  sample " + sample.ToString() + " of " + sequence.samplesPerStep.ToString(),
                            fraction);

                        if (pressedCancel == true)
                        {
                            cancelled = true;
                            break;
                        }

                        sequence.RestoreRest(animator, restRotations);
                        sequence.ApplyStep(step, phase, animator, restRotations);

                        renderer.BakeMesh(poseBake, true);
                        Vector3[] posedPositions = poseBake.vertices;

                        float sampleWorst = AccumulateDistortion(
                            graph,
                            posedPositions,
                            restEdgeLengths,
                            report.vertexDistortion);

                        if (sampleWorst > stepWorst)
                        {
                            stepWorst = sampleWorst;
                        }
                    }

                    sequence.RestoreRest(animator, restRotations);

                    report.stepLabels.Add(step.label);
                    report.stepDistortion.Add(stepWorst);

                    if (stepWorst > report.worstDistortion)
                    {
                        report.worstDistortion = stepWorst;
                        report.worstStepLabel = step.label;
                    }

                    if (cancelled == true)
                    {
                        break;
                    }
                }

                report.completed = cancelled == false;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                sequence.RestoreRest(animator, restRotations);
                UnityEngine.Object.DestroyImmediate(restBake);
                UnityEngine.Object.DestroyImmediate(poseBake);
            }

            report.worstVertices = CollectWorstVertices(report.vertexDistortion, 32);

            return report;
        }

        private static float[] BuildEdgeLengths(TopologyGraph graph, Vector3[] positions)
        {
            float[] lengths = new float[graph.adjacencyList.Length];

            for (int group = 0; group < graph.groupCount; group++)
            {
                int start = graph.NeighbourStart(group);
                int end = graph.NeighbourEnd(group);
                int vertexA = graph.GetRepresentativeVertex(group);

                for (int link = start; link < end; link++)
                {
                    int vertexB = graph.GetRepresentativeVertex(graph.adjacencyList[link]);
                    lengths[link] = Vector3.Distance(positions[vertexA], positions[vertexB]);
                }
            }

            return lengths;
        }

        private static float AccumulateDistortion(
            TopologyGraph graph,
            Vector3[] posedPositions,
            float[] restEdgeLengths,
            float[] vertexDistortion)
        {
            float worst = 0f;

            for (int group = 0; group < graph.groupCount; group++)
            {
                int start = graph.NeighbourStart(group);
                int end = graph.NeighbourEnd(group);
                int vertexA = graph.GetRepresentativeVertex(group);

                for (int link = start; link < end; link++)
                {
                    float restLength = restEdgeLengths[link];

                    if (restLength <= 0.00001f)
                    {
                        continue;
                    }

                    int vertexB = graph.GetRepresentativeVertex(graph.adjacencyList[link]);
                    float posedLength = Vector3.Distance(posedPositions[vertexA], posedPositions[vertexB]);
                    float distortion = Mathf.Abs(posedLength / restLength - 1f);

                    if (distortion > vertexDistortion[vertexA])
                    {
                        vertexDistortion[vertexA] = distortion;
                    }

                    if (distortion > worst)
                    {
                        worst = distortion;
                    }
                }
            }

            return worst;
        }

        private static List<RomVertexResult> CollectWorstVertices(float[] vertexDistortion, int count)
        {
            List<RomVertexResult> results = new List<RomVertexResult>();

            for (int vertex = 0; vertex < vertexDistortion.Length; vertex++)
            {
                if (vertexDistortion[vertex] <= 0f)
                {
                    continue;
                }

                RomVertexResult entry = new RomVertexResult();
                entry.vertexIndex = vertex;
                entry.distortion = vertexDistortion[vertex];
                results.Add(entry);
            }

            results.Sort(CompareResultDescending);

            if (results.Count > count)
            {
                results.RemoveRange(count, results.Count - count);
            }

            return results;
        }

        private static int CompareResultDescending(RomVertexResult left, RomVertexResult right)
        {
            return right.distortion.CompareTo(left.distortion);
        }

        /// <summary>
        /// Feeds the worst offenders straight into a layer mask. Distortion is stored on the
        /// welded representative vertex, so the group is expanded back out to every vertex.
        /// </summary>
        public static int[] ExtractProblemVertices(RomReport report, TopologyGraph graph, float threshold)
        {
            List<int> found = new List<int>();

            if (report == null || report.vertexDistortion.Length == 0)
            {
                return found.ToArray();
            }

            if (graph == null || graph.vertexCount != report.vertexDistortion.Length)
            {
                for (int vertex = 0; vertex < report.vertexDistortion.Length; vertex++)
                {
                    if (report.vertexDistortion[vertex] >= threshold)
                    {
                        found.Add(vertex);
                    }
                }

                return found.ToArray();
            }

            bool[] groupFlagged = new bool[graph.groupCount];

            for (int vertex = 0; vertex < report.vertexDistortion.Length; vertex++)
            {
                if (report.vertexDistortion[vertex] >= threshold)
                {
                    groupFlagged[graph.weldMap[vertex]] = true;
                }
            }

            for (int vertex = 0; vertex < graph.vertexCount; vertex++)
            {
                if (groupFlagged[graph.weldMap[vertex]] == true)
                {
                    found.Add(vertex);
                }
            }

            return found.ToArray();
        }
    }
}
