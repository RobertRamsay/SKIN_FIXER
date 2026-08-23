using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace SkinWeightTools
{
    [Serializable]
    public struct BoneInfluence
    {
        public int boneIndex;
        public float weight;

        public BoneInfluence(int newBoneIndex, float newWeight)
        {
            boneIndex = newBoneIndex;
            weight = newWeight;
        }
    }

    /// <summary>
    /// Editable, variable length skin weight container. One list per vertex so influence
    /// counts can grow and shrink during solving without reallocating a flattened buffer.
    /// Converted back to Mesh format on write.
    /// </summary>
    public class SkinWeightSet
    {
        public int vertexCount = 0;
        public int boneCount = 0;
        public List<BoneInfluence>[] influences = new List<BoneInfluence>[0];

        public SkinWeightSet(int newVertexCount, int newBoneCount)
        {
            vertexCount = newVertexCount;
            boneCount = newBoneCount;
            influences = new List<BoneInfluence>[newVertexCount];

            for (int vertex = 0; vertex < newVertexCount; vertex++)
            {
                influences[vertex] = new List<BoneInfluence>(4);
            }
        }

        public static SkinWeightSet FromMesh(Mesh mesh)
        {
            if (mesh == null)
            {
                return new SkinWeightSet(0, 0);
            }

            int boneTotal = mesh.bindposes.Length;
            SkinWeightSet result = new SkinWeightSet(mesh.vertexCount, boneTotal);

            NativeArray<byte> perVertexCounts = mesh.GetBonesPerVertex();
            NativeArray<BoneWeight1> flatWeights = mesh.GetAllBoneWeights();

            if (perVertexCounts.Length == 0 || flatWeights.Length == 0)
            {
                Debug.LogWarning("SkinWeightSet: mesh '" + mesh.name + "' carries no skin weights.");
                return result;
            }

            int cursor = 0;

            for (int vertex = 0; vertex < result.vertexCount; vertex++)
            {
                int count = perVertexCounts[vertex];
                List<BoneInfluence> target = result.influences[vertex];

                for (int slot = 0; slot < count; slot++)
                {
                    BoneWeight1 entry = flatWeights[cursor + slot];
                    target.Add(new BoneInfluence(entry.boneIndex, entry.weight));
                }

                cursor = cursor + count;
            }

            return result;
        }

        public void WriteToMesh(Mesh mesh)
        {
            if (mesh == null)
            {
                return;
            }

            int totalInfluences = 0;

            for (int vertex = 0; vertex < vertexCount; vertex++)
            {
                totalInfluences = totalInfluences + influences[vertex].Count;
            }

            NativeArray<byte> perVertexCounts = new NativeArray<byte>(vertexCount, Allocator.Temp);
            NativeArray<BoneWeight1> flatWeights = new NativeArray<BoneWeight1>(totalInfluences, Allocator.Temp);

            int cursor = 0;

            for (int vertex = 0; vertex < vertexCount; vertex++)
            {
                List<BoneInfluence> source = influences[vertex];
                perVertexCounts[vertex] = (byte)Mathf.Clamp(source.Count, 0, 255);

                for (int slot = 0; slot < source.Count; slot++)
                {
                    BoneWeight1 entry = new BoneWeight1();
                    entry.boneIndex = source[slot].boneIndex;
                    entry.weight = source[slot].weight;
                    flatWeights[cursor] = entry;
                    cursor = cursor + 1;
                }
            }

            mesh.SetBoneWeights(perVertexCounts, flatWeights);

            perVertexCounts.Dispose();
            flatWeights.Dispose();
        }

        public SkinWeightSet Clone()
        {
            SkinWeightSet copy = new SkinWeightSet(vertexCount, boneCount);

            for (int vertex = 0; vertex < vertexCount; vertex++)
            {
                copy.influences[vertex].AddRange(influences[vertex]);
            }

            return copy;
        }

        public float GetWeight(int vertex, int boneIndex)
        {
            List<BoneInfluence> list = influences[vertex];

            for (int slot = 0; slot < list.Count; slot++)
            {
                if (list[slot].boneIndex == boneIndex)
                {
                    return list[slot].weight;
                }
            }

            return 0f;
        }

        public int GetDominantBone(int vertex)
        {
            List<BoneInfluence> list = influences[vertex];
            int bestBone = -1;
            float bestWeight = -1f;

            for (int slot = 0; slot < list.Count; slot++)
            {
                if (list[slot].weight > bestWeight)
                {
                    bestWeight = list[slot].weight;
                    bestBone = list[slot].boneIndex;
                }
            }

            return bestBone;
        }

        public void SortDescending(int vertex)
        {
            influences[vertex].Sort(CompareInfluenceDescending);
        }

        private static int CompareInfluenceDescending(BoneInfluence left, BoneInfluence right)
        {
            return right.weight.CompareTo(left.weight);
        }

        public void Normalize(int vertex)
        {
            List<BoneInfluence> list = influences[vertex];
            float total = 0f;

            for (int slot = 0; slot < list.Count; slot++)
            {
                total = total + list[slot].weight;
            }

            if (total <= 0.00001f)
            {
                return;
            }

            for (int slot = 0; slot < list.Count; slot++)
            {
                BoneInfluence entry = list[slot];
                entry.weight = entry.weight / total;
                list[slot] = entry;
            }
        }

        public void NormalizeAll()
        {
            for (int vertex = 0; vertex < vertexCount; vertex++)
            {
                Normalize(vertex);
            }
        }

        /// <summary>Sorts, keeps the highest N influences, then renormalises.</summary>
        public void CullVertex(int vertex, int maxInfluences)
        {
            SortDescending(vertex);
            List<BoneInfluence> list = influences[vertex];

            if (list.Count > maxInfluences)
            {
                list.RemoveRange(maxInfluences, list.Count - maxInfluences);
            }

            Normalize(vertex);
        }

        public List<int> CollectUsedBones(float threshold)
        {
            HashSet<int> found = new HashSet<int>();

            for (int vertex = 0; vertex < vertexCount; vertex++)
            {
                List<BoneInfluence> list = influences[vertex];

                for (int slot = 0; slot < list.Count; slot++)
                {
                    if (list[slot].weight >= threshold)
                    {
                        found.Add(list[slot].boneIndex);
                    }
                }
            }

            List<int> ordered = new List<int>(found);
            ordered.Sort();
            return ordered;
        }

        public int MaxInfluenceCount()
        {
            int highest = 0;

            for (int vertex = 0; vertex < vertexCount; vertex++)
            {
                if (influences[vertex].Count > highest)
                {
                    highest = influences[vertex].Count;
                }
            }

            return highest;
        }

        public float AverageInfluenceCount()
        {
            if (vertexCount == 0)
            {
                return 0f;
            }

            int total = 0;

            for (int vertex = 0; vertex < vertexCount; vertex++)
            {
                total = total + influences[vertex].Count;
            }

            return (float)total / (float)vertexCount;
        }

        /// <summary>
        /// Total absolute weight change per vertex against another set. Used by the window
        /// to report how far a solve moved the rig from its source.
        /// </summary>
        public float[] MeasureDeltaAgainst(SkinWeightSet other)
        {
            float[] deltas = new float[vertexCount];

            if (other == null || other.vertexCount != vertexCount)
            {
                return deltas;
            }

            HashSet<int> seenBones = new HashSet<int>();

            for (int vertex = 0; vertex < vertexCount; vertex++)
            {
                seenBones.Clear();
                float total = 0f;

                List<BoneInfluence> mine = influences[vertex];

                for (int slot = 0; slot < mine.Count; slot++)
                {
                    int boneIndex = mine[slot].boneIndex;
                    seenBones.Add(boneIndex);
                    total = total + Mathf.Abs(mine[slot].weight - other.GetWeight(vertex, boneIndex));
                }

                List<BoneInfluence> theirs = other.influences[vertex];

                for (int slot = 0; slot < theirs.Count; slot++)
                {
                    int boneIndex = theirs[slot].boneIndex;

                    if (seenBones.Contains(boneIndex) == false)
                    {
                        total = total + Mathf.Abs(theirs[slot].weight);
                    }
                }

                deltas[vertex] = total;
            }

            return deltas;
        }
    }
}
