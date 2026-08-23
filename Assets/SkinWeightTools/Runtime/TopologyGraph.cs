using System.Collections.Generic;
using UnityEngine;

namespace SkinWeightTools
{
    /// <summary>
    /// Welded vertex adjacency graph. Import splits vertices at UV, normal and material
    /// seams, which would break topological distance, so coincident positions are merged
    /// into groups before adjacency is built. Distance fields are computed per group and
    /// read back per original vertex through weldMap.
    /// </summary>
    public class TopologyGraph
    {
        public int vertexCount = 0;
        public int groupCount = 0;
        public int[] weldMap = new int[0];
        public Vector3[] groupPositions = new Vector3[0];
        public int[] adjacencyStart = new int[0];
        public int[] adjacencyList = new int[0];
        public float[] adjacencyLength = new float[0];
        public float averageEdgeLength = 0f;
        public float shortestEdgeLength = 0f;

        private int[] representativeVertex = new int[0];

        public static TopologyGraph Build(Mesh mesh, float weldTolerance)
        {
            TopologyGraph graph = new TopologyGraph();

            if (mesh == null)
            {
                return graph;
            }

            Vector3[] positions = mesh.vertices;
            graph.vertexCount = positions.Length;
            graph.weldMap = new int[graph.vertexCount];

            float safeTolerance = Mathf.Max(weldTolerance, 0.0000001f);
            float toleranceSquared = safeTolerance * safeTolerance;

            Dictionary<Vector3Int, List<int>> cellBuckets = new Dictionary<Vector3Int, List<int>>(graph.vertexCount);
            List<Vector3> groupPositionList = new List<Vector3>(graph.vertexCount);

            for (int vertex = 0; vertex < graph.vertexCount; vertex++)
            {
                Vector3 position = positions[vertex];
                Vector3Int cell = new Vector3Int(
                    Mathf.FloorToInt(position.x / safeTolerance),
                    Mathf.FloorToInt(position.y / safeTolerance),
                    Mathf.FloorToInt(position.z / safeTolerance));

                int matchedGroup = FindNearbyGroup(cellBuckets, groupPositionList, cell, position, toleranceSquared);

                if (matchedGroup < 0)
                {
                    matchedGroup = groupPositionList.Count;
                    groupPositionList.Add(position);

                    List<int> ownBucket = null;
                    bool hasBucket = cellBuckets.TryGetValue(cell, out ownBucket);

                    if (hasBucket == false)
                    {
                        ownBucket = new List<int>(4);
                        cellBuckets.Add(cell, ownBucket);
                    }

                    ownBucket.Add(matchedGroup);
                }

                graph.weldMap[vertex] = matchedGroup;
            }

            graph.groupCount = groupPositionList.Count;
            graph.groupPositions = groupPositionList.ToArray();

            HashSet<long> edgeKeys = new HashSet<long>();
            List<int>[] neighbours = new List<int>[graph.groupCount];

            for (int group = 0; group < graph.groupCount; group++)
            {
                neighbours[group] = new List<int>(6);
            }

            for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
            {
                if (mesh.GetTopology(subMesh) != MeshTopology.Triangles)
                {
                    continue;
                }

                int[] triangles = mesh.GetTriangles(subMesh);

                for (int index = 0; index + 2 < triangles.Length; index = index + 3)
                {
                    int groupA = graph.weldMap[triangles[index]];
                    int groupB = graph.weldMap[triangles[index + 1]];
                    int groupC = graph.weldMap[triangles[index + 2]];

                    AddEdge(neighbours, edgeKeys, groupA, groupB);
                    AddEdge(neighbours, edgeKeys, groupB, groupC);
                    AddEdge(neighbours, edgeKeys, groupC, groupA);
                }
            }

            graph.adjacencyStart = new int[graph.groupCount + 1];
            int runningTotal = 0;

            for (int group = 0; group < graph.groupCount; group++)
            {
                graph.adjacencyStart[group] = runningTotal;
                runningTotal = runningTotal + neighbours[group].Count;
            }

            graph.adjacencyStart[graph.groupCount] = runningTotal;
            graph.adjacencyList = new int[runningTotal];
            graph.adjacencyLength = new float[runningTotal];

            int cursor = 0;
            float lengthTotal = 0f;
            float shortest = float.PositiveInfinity;

            for (int group = 0; group < graph.groupCount; group++)
            {
                List<int> list = neighbours[group];

                for (int slot = 0; slot < list.Count; slot++)
                {
                    int other = list[slot];
                    float length = Vector3.Distance(graph.groupPositions[group], graph.groupPositions[other]);
                    graph.adjacencyList[cursor] = other;
                    graph.adjacencyLength[cursor] = length;
                    lengthTotal = lengthTotal + length;

                    if (length > 0f && length < shortest)
                    {
                        shortest = length;
                    }

                    cursor = cursor + 1;
                }
            }

            if (runningTotal > 0)
            {
                graph.averageEdgeLength = lengthTotal / (float)runningTotal;
            }

            if (float.IsInfinity(shortest) == false)
            {
                graph.shortestEdgeLength = shortest;
            }

            graph.representativeVertex = new int[graph.groupCount];

            for (int vertex = graph.vertexCount - 1; vertex >= 0; vertex--)
            {
                graph.representativeVertex[graph.weldMap[vertex]] = vertex;
            }

            return graph;
        }

        /// <summary>
        /// Searches the twenty seven cells around a position. A single cell lookup misses
        /// pairs that straddle a cell boundary, which would leave seams unwelded.
        /// </summary>
        private static int FindNearbyGroup(
            Dictionary<Vector3Int, List<int>> cellBuckets,
            List<Vector3> groupPositionList,
            Vector3Int cell,
            Vector3 position,
            float toleranceSquared)
        {
            for (int offsetX = -1; offsetX <= 1; offsetX++)
            {
                for (int offsetY = -1; offsetY <= 1; offsetY++)
                {
                    for (int offsetZ = -1; offsetZ <= 1; offsetZ++)
                    {
                        Vector3Int probe = new Vector3Int(cell.x + offsetX, cell.y + offsetY, cell.z + offsetZ);
                        List<int> bucket = null;
                        bool found = cellBuckets.TryGetValue(probe, out bucket);

                        if (found == false)
                        {
                            continue;
                        }

                        for (int slot = 0; slot < bucket.Count; slot++)
                        {
                            int candidate = bucket[slot];
                            Vector3 offset = groupPositionList[candidate] - position;

                            if (offset.sqrMagnitude <= toleranceSquared)
                            {
                                return candidate;
                            }
                        }
                    }
                }
            }

            return -1;
        }

        private static void AddEdge(List<int>[] neighbours, HashSet<long> edgeKeys, int groupA, int groupB)
        {
            if (groupA == groupB)
            {
                return;
            }

            int low = Mathf.Min(groupA, groupB);
            int high = Mathf.Max(groupA, groupB);
            long key = ((long)low << 32) | (uint)high;

            if (edgeKeys.Add(key) == false)
            {
                return;
            }

            neighbours[groupA].Add(groupB);
            neighbours[groupB].Add(groupA);
        }

        /// <summary>
        /// Multi source shortest path over the welded graph. Returns one distance per group,
        /// PositiveInfinity where the group is unreachable or beyond maxDistance.
        /// </summary>
        public float[] ComputeDistanceField(List<int> sourceVertices, float maxDistance, bool useHopCount)
        {
            float[] distances = new float[groupCount];

            for (int group = 0; group < groupCount; group++)
            {
                distances[group] = float.PositiveInfinity;
            }

            if (sourceVertices == null || sourceVertices.Count == 0 || groupCount == 0)
            {
                return distances;
            }

            MinHeap heap = new MinHeap(sourceVertices.Count * 4);

            for (int slot = 0; slot < sourceVertices.Count; slot++)
            {
                int vertex = sourceVertices[slot];

                if (vertex < 0 || vertex >= vertexCount)
                {
                    continue;
                }

                int group = weldMap[vertex];

                if (distances[group] > 0f)
                {
                    distances[group] = 0f;
                    heap.Push(group, 0f);
                }
            }

            while (heap.Count > 0)
            {
                HeapNode current = heap.Pop();

                if (current.cost > distances[current.node])
                {
                    continue;
                }

                if (current.cost > maxDistance)
                {
                    continue;
                }

                int start = adjacencyStart[current.node];
                int end = adjacencyStart[current.node + 1];

                for (int link = start; link < end; link++)
                {
                    int other = adjacencyList[link];
                    float step = 1f;

                    if (useHopCount == false)
                    {
                        step = adjacencyLength[link];
                    }

                    float candidate = current.cost + step;

                    if (candidate > maxDistance)
                    {
                        continue;
                    }

                    if (candidate < distances[other])
                    {
                        distances[other] = candidate;
                        heap.Push(other, candidate);
                    }
                }
            }

            return distances;
        }

        public int NeighbourStart(int group)
        {
            return adjacencyStart[group];
        }

        public int NeighbourEnd(int group)
        {
            return adjacencyStart[group + 1];
        }

        /// <summary>
        /// Any original vertex in a welded group shares the group's position, so one
        /// standing representative is enough for adjacency reads.
        /// </summary>
        public int GetRepresentativeVertex(int group)
        {
            if (group < 0 || group >= representativeVertex.Length)
            {
                return 0;
            }

            return representativeVertex[group];
        }

        public bool IsValid()
        {
            if (groupCount <= 0)
            {
                return false;
            }

            if (adjacencyList.Length <= 0)
            {
                return false;
            }

            return true;
        }

        private struct HeapNode
        {
            public int node;
            public float cost;
        }

        private class MinHeap
        {
            private List<HeapNode> items = new List<HeapNode>();

            public MinHeap(int capacity)
            {
                items = new List<HeapNode>(Mathf.Max(capacity, 8));
            }

            public int Count
            {
                get { return items.Count; }
            }

            public void Push(int node, float cost)
            {
                HeapNode entry = new HeapNode();
                entry.node = node;
                entry.cost = cost;
                items.Add(entry);

                int child = items.Count - 1;

                while (child > 0)
                {
                    int parent = (child - 1) / 2;

                    if (items[parent].cost <= items[child].cost)
                    {
                        break;
                    }

                    HeapNode swap = items[parent];
                    items[parent] = items[child];
                    items[child] = swap;
                    child = parent;
                }
            }

            public HeapNode Pop()
            {
                HeapNode root = items[0];
                int last = items.Count - 1;
                items[0] = items[last];
                items.RemoveAt(last);

                int parent = 0;

                while (true)
                {
                    int left = parent * 2 + 1;
                    int right = left + 1;
                    int smallest = parent;

                    if (left < items.Count && items[left].cost < items[smallest].cost)
                    {
                        smallest = left;
                    }

                    if (right < items.Count && items[right].cost < items[smallest].cost)
                    {
                        smallest = right;
                    }

                    if (smallest == parent)
                    {
                        break;
                    }

                    HeapNode swap = items[parent];
                    items[parent] = items[smallest];
                    items[smallest] = swap;
                    parent = smallest;
                }

                return root;
            }
        }
    }
}
