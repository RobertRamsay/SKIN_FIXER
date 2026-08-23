using System;
using System.Collections.Generic;
using UnityEngine;

namespace SkinWeightTools
{
    public static class SkinWeightSolver
    {
        /// <summary>
        /// EVEN OUT. Redistributes influences so every vertex ends with at most
        /// settings.maxInfluences bones. Rather than a blind top N cull, each bone gets a
        /// topological distance field seeded from the vertices it already owns, and every
        /// vertex scores nearby bones through the falloff curve before keeping the best N.
        /// Pass a null mask to process every vertex.
        /// </summary>
        public static SkinWeightSet EvenOut(
            SkinWeightSet source,
            TopologyGraph graph,
            EvenOutSettings settings,
            bool[] vertexMask,
            Action<float, string> progress)
        {
            SkinWeightSet working = source.Clone();

            if (graph == null || graph.IsValid() == false)
            {
                Debug.LogError("SkinWeightSolver: topology graph is empty or has no edges. Falling back to a plain top N cull.");
                CullOnly(working, settings.maxInfluences, vertexMask);
                return working;
            }

            if (settings.smoothIterations > 0)
            {
                ReportProgress(progress, 0.02f, "Smoothing weights");
                SmoothWeights(working, graph, settings.smoothIterations, settings.smoothStrength, vertexMask);
            }

            ReportProgress(progress, 0.06f, "Collecting seeds");

            Dictionary<int, List<int>> seedsByBone = new Dictionary<int, List<int>>();
            Dictionary<int, List<int>> dominantByBone = new Dictionary<int, List<int>>();
            int[] dominantPerVertex = new int[working.vertexCount];

            for (int vertex = 0; vertex < working.vertexCount; vertex++)
            {
                int dominant = working.GetDominantBone(vertex);
                dominantPerVertex[vertex] = dominant;

                if (dominant >= 0)
                {
                    AddToBucket(dominantByBone, dominant, vertex);
                }

                List<BoneInfluence> list = working.influences[vertex];

                for (int slot = 0; slot < list.Count; slot++)
                {
                    if (list[slot].weight >= settings.seedThreshold)
                    {
                        AddToBucket(seedsByBone, list[slot].boneIndex, vertex);
                    }
                }
            }

            List<int> usedBones = working.CollectUsedBones(settings.weightThreshold);
            List<int> fieldBones = new List<int>(usedBones.Count);
            List<float[]> fieldData = new List<float[]>(usedBones.Count);
            Dictionary<int, int> boneToFieldSlot = new Dictionary<int, int>(usedBones.Count);

            for (int boneSlot = 0; boneSlot < usedBones.Count; boneSlot++)
            {
                int boneIndex = usedBones[boneSlot];
                List<int> seeds = null;
                bool hasSeeds = seedsByBone.TryGetValue(boneIndex, out seeds);

                if (hasSeeds == false && settings.useDominantFallbackSeeding == true)
                {
                    dominantByBone.TryGetValue(boneIndex, out seeds);
                }

                if (seeds == null || seeds.Count == 0)
                {
                    continue;
                }

                float[] field = graph.ComputeDistanceField(seeds, settings.searchRadius, settings.useHopCount);

                boneToFieldSlot.Add(boneIndex, fieldBones.Count);
                fieldBones.Add(boneIndex);
                fieldData.Add(field);

                float fraction = (float)boneSlot / Mathf.Max(1f, (float)usedBones.Count);
                ReportProgress(progress, 0.1f + fraction * 0.5f, "Distance field, bone " + boneIndex.ToString());
            }

            if (fieldBones.Count == 0)
            {
                Debug.LogWarning("SkinWeightSolver: no bone produced a distance field. Falling back to a plain top N cull.");
                CullOnly(working, settings.maxInfluences, vertexMask);
                return working;
            }

            List<BoneInfluence> candidates = new List<BoneInfluence>(fieldBones.Count);

            for (int vertex = 0; vertex < working.vertexCount; vertex++)
            {
                if (vertexMask != null && vertexMask[vertex] == false)
                {
                    continue;
                }

                if ((vertex & 2047) == 0)
                {
                    float fraction = (float)vertex / Mathf.Max(1f, (float)working.vertexCount);
                    ReportProgress(progress, 0.6f + fraction * 0.4f, "Solving vertices");
                }

                int group = graph.weldMap[vertex];
                int dominantBone = dominantPerVertex[vertex];
                candidates.Clear();

                bool dominantIncluded = false;

                for (int slot = 0; slot < fieldBones.Count; slot++)
                {
                    float distance = fieldData[slot][group];

                    if (float.IsInfinity(distance) == true)
                    {
                        continue;
                    }

                    int boneIndex = fieldBones[slot];
                    float normalised = 0f;

                    if (settings.searchRadius > 0f)
                    {
                        normalised = Mathf.Clamp01(distance / settings.searchRadius);
                    }

                    float falloffValue = Mathf.Max(0f, settings.falloff.Evaluate(normalised));
                    float originalWeight = working.GetWeight(vertex, boneIndex);
                    float bias = 1f;

                    if (originalWeight < settings.weightThreshold)
                    {
                        bias = settings.newBoneBias;
                    }

                    float score = Mathf.Lerp(originalWeight, falloffValue, settings.falloffBlend) * bias;

                    if (boneIndex == dominantBone)
                    {
                        dominantIncluded = true;

                        if (settings.preserveDominantBone == true)
                        {
                            score = Mathf.Max(score, originalWeight);
                        }
                    }

                    if (score <= 0f)
                    {
                        continue;
                    }

                    candidates.Add(new BoneInfluence(boneIndex, score));
                }

                bool needsDominant = settings.preserveDominantBone == true;
                needsDominant = needsDominant && dominantIncluded == false;
                needsDominant = needsDominant && dominantBone >= 0;

                if (needsDominant == true)
                {
                    float originalWeight = working.GetWeight(vertex, dominantBone);

                    if (originalWeight > 0f)
                    {
                        candidates.Add(new BoneInfluence(dominantBone, originalWeight));
                    }
                }

                if (candidates.Count == 0)
                {
                    working.CullVertex(vertex, settings.maxInfluences);
                    continue;
                }

                candidates.Sort(CompareInfluenceDescending);

                int keepCount = Mathf.Min(settings.maxInfluences, candidates.Count);
                float total = 0f;

                for (int slot = 0; slot < keepCount; slot++)
                {
                    total = total + candidates[slot].weight;
                }

                List<BoneInfluence> destination = working.influences[vertex];
                destination.Clear();

                if (total <= 0.00001f)
                {
                    destination.Add(new BoneInfluence(candidates[0].boneIndex, 1f));
                    continue;
                }

                for (int slot = 0; slot < keepCount; slot++)
                {
                    BoneInfluence entry = candidates[slot];
                    entry.weight = entry.weight / total;
                    destination.Add(entry);
                }
            }

            ReportProgress(progress, 1f, "Done");
            return working;
        }

        /// <summary>
        /// Laplacian smoothing across the welded graph. Each vertex mixes its own weights
        /// with the average of its neighbours, then renormalises.
        /// </summary>
        public static void SmoothWeights(
            SkinWeightSet target,
            TopologyGraph graph,
            int iterations,
            float strength,
            bool[] vertexMask)
        {
            if (graph == null || graph.IsValid() == false)
            {
                return;
            }

            float clampedStrength = Mathf.Clamp01(strength);
            Dictionary<int, float> accumulator = new Dictionary<int, float>(16);

            for (int pass = 0; pass < iterations; pass++)
            {
                SkinWeightSet snapshot = target.Clone();

                for (int vertex = 0; vertex < target.vertexCount; vertex++)
                {
                    if (vertexMask != null && vertexMask[vertex] == false)
                    {
                        continue;
                    }

                    accumulator.Clear();
                    List<BoneInfluence> own = snapshot.influences[vertex];

                    for (int slot = 0; slot < own.Count; slot++)
                    {
                        AddToAccumulator(accumulator, own[slot].boneIndex, own[slot].weight * (1f - clampedStrength));
                    }

                    int group = graph.weldMap[vertex];
                    int start = graph.NeighbourStart(group);
                    int end = graph.NeighbourEnd(group);
                    int neighbourCount = end - start;

                    if (neighbourCount > 0)
                    {
                        float share = clampedStrength / (float)neighbourCount;

                        for (int link = start; link < end; link++)
                        {
                            int neighbourGroup = graph.adjacencyList[link];
                            int neighbourVertex = graph.GetRepresentativeVertex(neighbourGroup);
                            List<BoneInfluence> other = snapshot.influences[neighbourVertex];

                            for (int slot = 0; slot < other.Count; slot++)
                            {
                                AddToAccumulator(accumulator, other[slot].boneIndex, other[slot].weight * share);
                            }
                        }
                    }

                    List<BoneInfluence> destination = target.influences[vertex];
                    destination.Clear();

                    foreach (KeyValuePair<int, float> pair in accumulator)
                    {
                        if (pair.Value > 0f)
                        {
                            destination.Add(new BoneInfluence(pair.Key, pair.Value));
                        }
                    }

                    target.Normalize(vertex);
                }
            }
        }

        private static void AddToBucket(Dictionary<int, List<int>> buckets, int key, int value)
        {
            List<int> bucket = null;
            bool found = buckets.TryGetValue(key, out bucket);

            if (found == false)
            {
                bucket = new List<int>(64);
                buckets.Add(key, bucket);
            }

            bucket.Add(value);
        }

        private static void AddToAccumulator(Dictionary<int, float> accumulator, int boneIndex, float amount)
        {
            float existing = 0f;
            bool found = accumulator.TryGetValue(boneIndex, out existing);

            if (found == true)
            {
                accumulator[boneIndex] = existing + amount;
                return;
            }

            accumulator.Add(boneIndex, amount);
        }

        private static void CullOnly(SkinWeightSet target, int maxInfluences, bool[] vertexMask)
        {
            for (int vertex = 0; vertex < target.vertexCount; vertex++)
            {
                if (vertexMask != null && vertexMask[vertex] == false)
                {
                    continue;
                }

                target.CullVertex(vertex, maxInfluences);
            }
        }

        private static int CompareInfluenceDescending(BoneInfluence left, BoneInfluence right)
        {
            return right.weight.CompareTo(left.weight);
        }

        private static void ReportProgress(Action<float, string> progress, float value, string label)
        {
            if (progress != null)
            {
                progress(value, label);
            }
        }
    }
}
