using System.Diagnostics;
using System.Numerics;
using System.Text;
using NUnit.Framework;

namespace HNSW;

public class Node
{
    public int Id { get; private set; }
    public float[] Vector { get; set; }

    public List<List<Node>> Neighbors { get; set; }

    public Node(float[] vector, int level, int id)
    {
        this.Id = id;
        this.Vector = vector;
        Neighbors = new();
        for (int i = 0; i < level + 1; i++)
            Neighbors.Add(new List<Node>());
    }

    public IEnumerable<Node> GetNeighbors(int level)
    {
        if (level > Neighbors.Count - 1)
        {
            return Enumerable.Empty<Node>();
        }

        return Neighbors[level];
    }
}

static class DistanceCalculator
{

    public static float CosineDistance(float[] first, float[] second)
    {
        if (first.Length != second.Length)
            throw new ArgumentException("Vectors must be of the same length");

        float dotProduct = 0;
        float normASquared = 0;
        float normBSquared = 0;

        float[] temp = new float[Vector<float>.Count];

        int i = 0;
        // Process vectors in chunks
        for (; i <= first.Length - Vector<float>.Count; i += Vector<float>.Count)
        {
            Vector<float> va = new(first, i);

            for (int j = 0; j < Vector<float>.Count; j++)
            {
                temp[j] = second[i + j];
            }

            Vector<float> vb = new(temp);

            dotProduct += Vector.Dot(va, vb); // Vectorized dot product part
            normASquared += Vector.Dot(va, va); // Vectorized squared norm part
            normBSquared += Vector.Dot(vb, vb);
        }

        // Process any remaining elements (tail)
        for (; i < first.Length; i++)
        {
            dotProduct += first[i] * second[i];
            normASquared += first[i] * first[i];
            normBSquared += second[i] * second[i];
        }

        if (normASquared == 0 || normBSquared == 0)
            return 1;

        return 1 - (dotProduct / MathF.Sqrt(normASquared * normBSquared));
    }
}

public class HNSWCollection
{

    public List<List<Node>> Nodes { get; set; }
    public int NumNeighbors { get; private set; }

    public int NumNeighbors0 { get; private set; }

    public int EfConstruction { get; private set; }

    private Random random = new Random(13);
    private Node? entryPoint = null;
    private int maxId = 1;

    public HNSWCollection(int numNeighbors, int numNeighbors0 = -1, int efConstruction = 10)
    {
        Nodes = new();
        this.NumNeighbors = numNeighbors;
        this.NumNeighbors0 = (numNeighbors0 != -1) ? numNeighbors0 : numNeighbors * 2;
        this.EfConstruction = efConstruction;
    }

    public int Count => Nodes[0].Count;

    public string ToDot(int level)
    {
        if (level < 0 || level >= Nodes.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(level), "Level must be within the range of existing levels.");
        }

        StringBuilder stringBuilder = new StringBuilder();

        Dictionary<Node, string> nodeIds = new Dictionary<Node, string>();

        stringBuilder.AppendLine("graph HNSW {");

        for (int i = 0; i < Nodes[level].Count; i++)
        {
            Node node = Nodes[level][i];
            string id = node.Id.ToString("X");
            nodeIds[node] = id;
        }

        HashSet<(Node, Node)> edges = new HashSet<(Node, Node)>();

        foreach (var node in Nodes[level])
        {
            foreach (var neighbor in node.Neighbors[level])
            {
                if (edges.Contains((neighbor, node)) || edges.Contains((node, neighbor)))
                {
                    continue; // Avoid duplicate edges
                }
                edges.Add((node, neighbor));
                stringBuilder.AppendLine($"    \"{nodeIds[node]}\" -- \"{nodeIds[neighbor]}\" [label=\"{DistanceCalculator.CosineDistance(node.Vector, neighbor.Vector):F2}\"];");
            }
        }

        stringBuilder.AppendLine("}");
        return stringBuilder.ToString();
    }

    public void Add(float[] vector)
    {
        Debug.Assert(this.entryPoint != null || Nodes.Count == 0);

        int level = (int)(-MathF.Log2(random.NextSingle()) * 0.3);
        var node = new Node(vector, level, maxId++);


        NodeDistanceSet nearest = new NodeDistanceSet(vector);
        int topLayer = Nodes.Count - 1;

        Node? entryPoint = this.entryPoint;

        for (int i = topLayer; i > level + 1; i--)
        {
            Debug.Assert(entryPoint != null);
            nearest = SearchLayer(vector, entryPoint, EfConstruction, i);
            Debug.Assert(nearest.Nearest != null, "This can't be null if for loop is executing, because it means that the HNSW is not empty");
            entryPoint = nearest.Nearest.Node;
        }

        for (int i = Math.Min(topLayer, level); i >= 0 && entryPoint != null; i--)
        {
            int numNeighbors = i == 0 ? NumNeighbors0 : NumNeighbors;
            NodeDistanceSet candidates = SearchLayer(vector, entryPoint, EfConstruction, i);
            List<NodeWithDistance> neighbors = SelectNeighborsSimple(vector, candidates, numNeighbors); //SelectNeighbors(vector, candidates, numNeighbors, i, true, false);

            //List<NodeWithDistance> neighbors = SelectNeighbors(vector, candidates, numNeighbors, i, true, false);
            foreach (var neighbor in neighbors)
            {
                neighbor.Node.Neighbors[i].Add(node);
                node.Neighbors[i].Add(neighbor.Node);

                if (neighbor.Node.Neighbors[i].Count > numNeighbors)
                {
                    RefreshNeighborConnections(neighbor, i, numNeighbors);
                }
            }

            Debug.Assert(candidates.Nearest != null);
            entryPoint = candidates.Nearest.Node;
        }

        if (level > Nodes.Count - 1)
        {
            this.entryPoint = node;
        }

        for (int i = 0; i < level + 1; i++)
        {
            if (i >= Nodes.Count)
                Nodes.Add(new List<Node>());

            Nodes[i].Add(node);
        }
    }

    private void RefreshNeighborConnections(NodeWithDistance node, int level, int numNeighbors)
    {
        Debug.Assert(!node.Node.Neighbors[level].Contains(node.Node), "Node should not be in its own neighbors list");
        foreach (var neighbor in node.Node.Neighbors[level])
        {
            neighbor.Neighbors[level].Remove(node.Node);
        }

        NodeDistanceSet set = new NodeDistanceSet(node.Node.Vector, node.Node.Neighbors[level]);
        //List<Node> newNeighbors = SelectNeighbors(node.Node.Vector, set, numNeighbors, level, true, false, node.Node).Select(x => x.Node).ToList();
        List<Node> newNeighbors = SelectNeighborsSimple(node.Node.Vector, set, numNeighbors).Select(x => x.Node).ToList();

        Debug.Assert(newNeighbors.Count != 0);

        for (int i = 0; i < newNeighbors.Count; i++)
        {
            set.Remove(new NodeWithDistance(newNeighbors[i], 0));
        }

        bool IsFarther(NodeWithDistance first, Node second, float distance)
        {
            bool firstEmpty = first.Node.Neighbors[level].Count < numNeighbors;
            bool secondEmpty = second.Neighbors[level].Count < numNeighbors;

            if (firstEmpty == secondEmpty)
            {
                return first.Distance > distance;
            }

            return !firstEmpty;
        }

        // Find better place for removed connections, this is to prevent islands formation
        foreach (var removed in set)
        {
            Debug.Assert(removed.Node.Id != node.Node.Id);

            removed.Node.Neighbors[level].Remove(node.Node);
            NodeWithDistance? nearest = null;
            foreach (var neighbor in newNeighbors)
            {
                Debug.Assert(neighbor.Id != node.Node.Id);
                float distance = DistanceCalculator.CosineDistance(neighbor.Vector, removed.Node.Vector);

                if (nearest == null)
                {
                    nearest = new NodeWithDistance(neighbor, distance);
                    continue;
                }

                if (IsFarther(nearest, neighbor, distance))
                {
                    nearest.Distance = distance;
                    nearest.Node = neighbor;
                }
            }

            Debug.Assert(nearest != null);
            nearest.Node.Neighbors[level].Add(removed.Node);
            removed.Node.Neighbors[level].Add(nearest.Node);
        }

        node.Node.Neighbors[level] = newNeighbors;

        foreach (var neighbor in node.Node.Neighbors[level])
        {
            Debug.Assert(neighbor.Id != node.Node.Id);
            neighbor.Neighbors[level].Add(node.Node);
        }
    }

    private class NodeWithDistance : IEquatable<NodeWithDistance>
    {
        public Node Node { get; set; }
        public float Distance { get; set; }

        public NodeWithDistance(Node node, float distance)
        {
            Node = node;
            Distance = distance;
        }

        public override bool Equals(object? obj)
        {
            if (obj is NodeWithDistance other)
            {
                return Equals(other);
            }
            return false;
        }

        public bool Equals(NodeWithDistance? other)
        {
            return other != null && Node.Equals(other.Node);
        }

        public override int GetHashCode()
        {
            return Node.GetHashCode();
        }
    }

    private class NodeDistanceSet : ICollection<NodeWithDistance>
    {
        private readonly HashSet<NodeWithDistance> candidates;
        private readonly float[] q;

        public NodeWithDistance? Furthest { get; private set; } = null;
        public NodeWithDistance? Nearest { get; private set; } = null;

        public int Count => candidates.Count;

        public bool IsReadOnly => false;

        public NodeDistanceSet(float[] q) : this(q, Enumerable.Empty<Node>())
        {
        }

        public NodeDistanceSet(float[] q, IEnumerable<Node> nodes)
        {
            this.q = q;

            if (nodes is IReadOnlyCollection<Node> c)
            {
                candidates = new HashSet<NodeWithDistance>(c.Count);
            }
            else
            {
                candidates = new HashSet<NodeWithDistance>();
            }

            foreach (var node in nodes)
            {
                Add(node);
            }
        }

        public void Add(Node node)
        {
            float distance = DistanceCalculator.CosineDistance(q, node.Vector);
            NodeWithDistance nodeWithDistance = new NodeWithDistance(node, distance);
            Add(nodeWithDistance);
        }

        public void Add(NodeWithDistance item)
        {
            if (candidates.Add(item))
            {
                if (Furthest == null || item.Distance > Furthest.Distance)
                {
                    Furthest = item;
                }

                if (Nearest == null || item.Distance < Nearest.Distance)
                {
                    Nearest = item;
                }
            }
        }

        public bool Remove(NodeWithDistance item)
        {
            if (candidates.Remove(item))
            {
                if (Furthest != null && item.Equals(Furthest))
                {
                    Furthest = candidates.MaxBy(c => c.Distance);
                }

                if (Nearest != null && item.Equals(Nearest))
                {
                    Nearest = candidates.MinBy(c => c.Distance);
                }
                return true;
            }
            return false;
        }

        public NodeWithDistance? PopFurthest()
        {
            if (Furthest == null)
            {
                return null;
            }

            NodeWithDistance result = Furthest;
            Remove(result);
            return result;
        }

        public NodeWithDistance? PopNearest()
        {
            if (Nearest == null)
            {
                return null;
            }

            NodeWithDistance result = Nearest;
            Remove(result);
            return result;
        }

        public void Clear()
        {
            candidates.Clear();
            Furthest = null;
            Nearest = null;
        }

        public bool Contains(NodeWithDistance item) => candidates.Contains(item);
        public bool Contains(Node node) => candidates.Contains(new NodeWithDistance(node, 0));

        public void CopyTo(NodeWithDistance[] array, int arrayIndex) => candidates.CopyTo(array, arrayIndex);

        public IEnumerator<NodeWithDistance> GetEnumerator() => candidates.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => candidates.GetEnumerator();
    }
  
    public List<float[]> SearchExact(float[] q, int k)
    {
        IComparer<float> comparer = Comparer<float>.Create((x, y) => Math.Sign(y - x));
        PriorityQueue<NodeWithDistance, float> pq = new PriorityQueue<NodeWithDistance, float>(comparer);
        foreach (Node node in Nodes[0])
        {
            float distance = DistanceCalculator.CosineDistance(q, node.Vector);

            if (pq.Count == k)
            {
                if (distance >= pq.Peek().Distance)
                    continue;

                pq.Dequeue();
            }
            pq.Enqueue(new NodeWithDistance(node, distance), distance);
        }

        List<float[]> result = new List<float[]>(k);
        for (int i = 0; i < k && pq.Count > 0; i++)
        {
            var nearest = pq.Dequeue();
            
            result.Add(nearest.Node.Vector);
        }

        result.Reverse();
        
        return result;
    }

    public List<float[]> Search(float[] q, int k, int ef)
    {
        if (this.entryPoint == null)
        {
            return new();
        }

        Node entryPoint = this.entryPoint;
        NodeDistanceSet result = new NodeDistanceSet(q);

        for (int l = Nodes.Count - 1; l > 0; l--)
        {
            result = SearchLayer(q, entryPoint, 1, l);
            Debug.Assert(result.Nearest != null, "Nearest should not be null if we are searching in a non-empty HNSW");
            entryPoint = result.Nearest.Node;
        }

        result = SearchLayer(q, entryPoint, ef, 0);
        return result.OrderBy(r => r.Distance)
                    .Take(k)
                    .Select(r => r.Node.Vector)
                    .ToList();
    }

    public bool Verify()
    {
        Queue<(Node node, Node? origin)> toVisit = new();
        HashSet<Node> visited = new HashSet<Node>();

        List<string> errors = new List<string>();
        for (int level = 0; level < Nodes.Count; level++)
        {
            visited.Clear();
            toVisit.Clear();
            int maxNeighbors = (level == 0) ? NumNeighbors0 : NumNeighbors;
            toVisit.Enqueue((Nodes[level][0], null));

            while (toVisit.Count > 0)
            {
                (Node node, Node? origin) = toVisit.Dequeue();
                if (visited.Contains(node))
                    continue;

                visited.Add(node);

                if (node.Neighbors[level].Count > maxNeighbors)
                {
                    errors.Add($"Node {node.Id} at level {level} has {node.Neighbors[level].Count} neighbors, exceeding the limit of {maxNeighbors}.");
                }

                bool hasOrigin = origin == null;
                foreach (var neighbor in node.Neighbors[level])
                {
                    if (!visited.Contains(neighbor))
                    {
                        toVisit.Enqueue((neighbor, node));
                    }

                    if (!hasOrigin && neighbor == origin)
                    {
                        hasOrigin = true;
                    }
                }

                if (!hasOrigin)
                {
                    errors.Add($"Node {node.Id} at level {level} does not have a back-reference to its origin node {origin?.Id}.");
                }

            }

            if (visited.Count != Nodes[level].Count)
            {
                errors.Add($"Level {level} is not fully connected. Visited {visited.Count} out of {Nodes[level].Count} nodes.");
            }
        }

        foreach (var error in errors)
        {
            Console.WriteLine(error);
        }
        return errors.Count == 0;

    }

    private NodeDistanceSet SearchLayer(float[] q, Node entryPoint, int ef, int l)
    {
        NodeDistanceSet result = new(q);
        
        if (entryPoint == null)
        {
            return result;
        }

        result.Add(entryPoint);
        HashSet<Node> visited = new HashSet<Node>();


        NodeDistanceSet candidates = new(q);
        candidates.Add(entryPoint);
        visited.Add(entryPoint);

        while (candidates.Count > 0)
        {
            NodeWithDistance? min = candidates.PopNearest();
            NodeWithDistance? furthestResult = result.Furthest;

            Debug.Assert(min != null, "Min should not be null if candidates is not empty");

            if (furthestResult != null && min.Distance > furthestResult.Distance)
            {
                break;
            }

            foreach (var neighbor in min.Node.GetNeighbors(l))
            {
                if (visited.Contains(neighbor)) continue;
                visited.Add(neighbor);
                float d = DistanceCalculator.CosineDistance(q, neighbor.Vector);

                if (d < furthestResult?.Distance || result.Count < ef)
                {
                    NodeWithDistance nodeWithDistance = new NodeWithDistance(neighbor, d);
                    candidates.Add(nodeWithDistance);
                    result.Add(nodeWithDistance);

                    if (result.Count > ef)
                    {
                        result.PopFurthest();
                    }
                }
            }
        }

        return result;
    }

    private List<NodeWithDistance> SelectNeighborsSimple(float[] q, NodeDistanceSet candidates, int m)
    {
        int count = Math.Min(candidates.Count, m);
        List<NodeWithDistance> result = candidates.OrderBy(r => r.Distance).Take(count).ToList();
        return result;
    }

    private List<NodeWithDistance> SelectNeighbors(float[] q, NodeDistanceSet candidates, int m, int level,
                                                   bool extendCandidates, bool keepPrunedConnections,
                                                   Node? exclude = null)
    {
        NodeDistanceSet result = new(q);
        NodeDistanceSet w = new(q, candidates.Select(c => c.Node));
        if (extendCandidates)
        {
            foreach (var node in candidates)
            {
                foreach (var neighbor in node.Node.GetNeighbors(level))
                {
                    if (neighbor == exclude)
                        continue;
                    w.Add(neighbor);
                }
            }
        }
        NodeDistanceSet discarded = new(q);

        while (w.Count > 0 && result.Count < m)
        {
            NodeWithDistance? nearest = w.PopNearest();

            Debug.Assert(nearest != null, "Nearest should not be null if w is not empty");

            bool add = true;
            foreach (var r in result)
            {
                float distance = DistanceCalculator.CosineDistance(nearest.Node.Vector, r.Node.Vector);
                if (distance < nearest.Distance)
                {
                    add = false;
                    break;
                }
            }

            if (add)
            {
                result.Add(nearest);
            }
            else
            {
                discarded.Add(nearest);
            }
        }

        if (keepPrunedConnections)
        {
            while (discarded.Count > 0 && result.Count < m)
            {
                NodeWithDistance? node = discarded.PopNearest();
                Debug.Assert(node != null, "Node should not be null if discarded is not empty");
                result.Add(node);
            }
        }

        return result.ToList();
    }
}