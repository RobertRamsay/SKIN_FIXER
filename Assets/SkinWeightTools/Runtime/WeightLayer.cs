using System;
using System.Collections.Generic;
using UnityEngine;

namespace SkinWeightTools
{
    public enum WeightLayerType
    {
        Tighten = 0,
        Loosen = 1,
        Smooth = 2,
        Normalize = 3
    }

    /// <summary>
    /// A modifier evaluated on top of a base weight set. Tighten raises weights to a power
    /// above one, sharpening the dominant bone. Loosen uses a power below one, spreading
    /// influence outward. Both are masked by a topological falloff around seed vertices.
    /// </summary>
    [Serializable]
    public class WeightLayer
    {
        public string layerName = "Layer";
        public bool enabled = true;
        public WeightLayerType layerType = WeightLayerType.Tighten;

        [Range(0f, 1f)]
        public float strength = 0.5f;

        [Range(1f, 8f)]
        [Tooltip("How aggressive the power curve is at full mask and full strength.")]
        public float sharpness = 2.5f;

        [Tooltip("Vertices at the centre of the affected region. Empty means the whole mesh.")]
        public int[] maskVertices = new int[0];

        [Tooltip("Mask radius in metres, or in edge hops when useHopCount is on.")]
        public float maskRadius = 0.08f;

        public bool useHopCount = false;
        public AnimationCurve maskFalloff = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

        [Tooltip("Restrict the layer to these bone indices. Empty means all bones.")]
        public int[] boneFilter = new int[0];

        public int maxInfluences = 4;

        public void Apply(SkinWeightSet target, TopologyGraph graph)
        {
            if (enabled == false)
            {
                return;
            }

            if (target == null || graph == null)
            {
                return;
            }

            if (layerType == WeightLayerType.Normalize)
            {
                target.NormalizeAll();
                return;
            }

            float[] maskField = BuildMaskField(target, graph);

            if (layerType == WeightLayerType.Smooth)
            {
                bool[] vertexMask = new bool[target.vertexCount];

                for (int vertex = 0; vertex < target.vertexCount; vertex++)
                {
                    vertexMask[vertex] = maskField[vertex] > 0.001f;
                }

                SkinWeightSolver.SmoothWeights(target, graph, 1, strength, vertexMask);
                return;
            }

            HashSet<int> filterSet = new HashSet<int>(boneFilter);

            for (int vertex = 0; vertex < target.vertexCount; vertex++)
            {
                float mask = maskField[vertex];

                if (mask <= 0.001f)
                {
                    continue;
                }

                float amount = 1f + sharpness * strength * mask;
                float exponent = amount;

                if (layerType == WeightLayerType.Loosen)
                {
                    exponent = 1f / amount;
                }

                List<BoneInfluence> list = target.influences[vertex];

                for (int slot = 0; slot < list.Count; slot++)
                {
                    BoneInfluence entry = list[slot];

                    if (filterSet.Count > 0 && filterSet.Contains(entry.boneIndex) == false)
                    {
                        continue;
                    }

                    entry.weight = Mathf.Pow(Mathf.Max(entry.weight, 0f), exponent);
                    list[slot] = entry;
                }

                target.CullVertex(vertex, maxInfluences);
            }
        }

        private float[] BuildMaskField(SkinWeightSet target, TopologyGraph graph)
        {
            float[] mask = new float[target.vertexCount];

            if (maskVertices.Length == 0)
            {
                for (int vertex = 0; vertex < mask.Length; vertex++)
                {
                    mask[vertex] = 1f;
                }

                return mask;
            }

            List<int> seeds = new List<int>(maskVertices);
            float[] field = graph.ComputeDistanceField(seeds, maskRadius, useHopCount);

            for (int vertex = 0; vertex < mask.Length; vertex++)
            {
                float distance = field[graph.weldMap[vertex]];

                if (float.IsInfinity(distance) == true)
                {
                    mask[vertex] = 0f;
                    continue;
                }

                float normalised = 0f;

                if (maskRadius > 0f)
                {
                    normalised = Mathf.Clamp01(distance / maskRadius);
                }

                mask[vertex] = Mathf.Clamp01(maskFalloff.Evaluate(normalised));
            }

            return mask;
        }
    }

    [Serializable]
    public class WeightLayerStack
    {
        public List<WeightLayer> layers = new List<WeightLayer>();

        public SkinWeightSet Evaluate(SkinWeightSet baseSet, TopologyGraph graph)
        {
            SkinWeightSet working = baseSet.Clone();

            for (int index = 0; index < layers.Count; index++)
            {
                layers[index].Apply(working, graph);
            }

            return working;
        }

        public WeightLayer AddLayer(WeightLayerType layerType)
        {
            WeightLayer layer = new WeightLayer();
            layer.layerType = layerType;
            layer.layerName = layerType.ToString() + " " + (layers.Count + 1).ToString();
            layers.Add(layer);
            return layer;
        }

        public void MoveLayer(int fromIndex, int toIndex)
        {
            if (fromIndex < 0 || fromIndex >= layers.Count)
            {
                return;
            }

            if (toIndex < 0 || toIndex >= layers.Count)
            {
                return;
            }

            WeightLayer moved = layers[fromIndex];
            layers.RemoveAt(fromIndex);
            layers.Insert(toIndex, moved);
        }
    }
}
