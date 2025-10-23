using VeloxDB.Client;
using VlxAPI;

internal class Node
{
    public Node()
    {
        Children = Array.Empty<Node>();
        Center = Array.Empty<float>();
    }
    public Node(Node[] group)
    {
        float[] center = new float[group[0].Center.Length];
        float radius = 0f;
        foreach (Node node in group)
        {
            for (int i = 0; i < center.Length; i++)
            {
                center[i] += node.Center[i];
            }
        }

        for (int i = 0; i < center.Length; i++)
        {
            center[i] /= group.Length;
        }

        foreach (Node node in group)
        {
            float distance = DistanceCalculator.EuclideanDistance(center, node.Center) + node.Radius;
            if (distance > radius)
            {
                radius = distance;
            }
        }

        Children = group;
        Center = center;
        Radius = radius;
    }

    public float Radius { get; protected set; }
    public float[] Center { get; protected set; }

    public Node[] Children { get; set; } = Array.Empty<Node>();

    public virtual bool IsLeaf => false;
}

internal class LeafNode : Node
{

    public LeafNode(float[][] neighbors)
    {
        this.Vectors = neighbors;
        float[] center = new float[neighbors[0].Length];
        for (int i = 0; i < neighbors.Length; i++)
        {
            float[] vector = neighbors[i];
            for (int j = 0; j < vector.Length; j++)
            {
                center[j] += vector[j];
            }
        }

        for (int i = 0; i < center.Length; i++)
        {
            center[i] /= neighbors.Length;
        }

        Center = center;

        foreach (float[] vector in neighbors)
        {
            float distance = DistanceCalculator.EuclideanDistance(center, vector);
            if (distance > Radius)
            {
                Radius = distance;
            }
        }
    }

    public float[][] Vectors { get; set; }

    public override bool IsLeaf => true;
}



internal class Tree
{
    private Node root;

    public Tree(Node root)
    {
        this.root = root;
    }

    internal static IReadOnlyCollection<T[]> Group<T>(IList<T> items, int groupSize, Func<T, T, float> distanceFunc)
    {
        List<T> vectorList = new List<T>(items);
        List<T[]> groups = new List<T[]>();

        PriorityQueue<int, float> queue = new(groupSize, Comparer<float>.Create((x, y) => y.CompareTo(x)));
        while (vectorList.Count > 0)
        {
            T currentVector = vectorList[0];
            queue.Clear();
            queue.Enqueue(0, 0f);

            for (int i = 1; i < vectorList.Count; i++)
            {
                float distance = distanceFunc(currentVector, vectorList[i]);
                if (queue.Count < groupSize)
                {
                    queue.Enqueue(i, distance);
                }
                else if (queue.TryPeek(out var _, out var maxDistance))
                {
                    if (distance < maxDistance)
                    {
                        queue.DequeueEnqueue(i, distance);
                    }
                }
            }

            T[] neighbors = new T[queue.Count];
            int[] indexes = new int[queue.Count];
            int index = 0;
            foreach ((int Element, float Priority) item in queue.UnorderedItems)
            {
                indexes[index] = item.Element;
                neighbors[index] = vectorList[item.Element];
                index++;
            }

            Array.Sort(indexes, Comparer<int>.Create((x, y) => Math.Sign(y - x)));

            for (int i = 0; i < indexes.Length; i++)
            {
                vectorList[indexes[i]] = vectorList[^1];
                vectorList.RemoveAt(vectorList.Count - 1);
            }

            groups.Add(neighbors);
        }
        return groups;
    }

    public static Tree Create(float[][] vectors, int maxNeighbors = 8)
    {
        IReadOnlyCollection<float[][]> grouped = Group(vectors, maxNeighbors, DistanceCalculator.EuclideanDistance);
        List<Node> nodes = new List<Node>();
        foreach (var group in grouped)
        {
            LeafNode leafNode = new LeafNode(group);
            nodes.Add(leafNode);
        }

        while (nodes.Count > 1)
        {
            IReadOnlyCollection<Node[]> groupedNodes = Group(nodes, maxNeighbors, (x, y) => DistanceCalculator.EuclideanDistance(x.Center, y.Center));
            nodes.Clear();
            foreach (var group in groupedNodes)
            {
                Node node = new Node(group);
                nodes.Add(node);
            }
        }

        return new Tree(nodes[0]);
    }

    public IEnumerable<float[]> NearestNeighbors(float[] vector, int count, out int childrenVisited)
    {
        childrenVisited = 0;
        if (root.IsLeaf)
        {
            LeafNode leafNode = (LeafNode)root;
            float[][] res = leafNode.Vectors.ToArray();
            Array.Sort(res, (x, y) => DistanceCalculator.EuclideanDistance(vector, x).CompareTo(DistanceCalculator.EuclideanDistance(vector, y)));
            return res.Take(count);
        }

        PriorityQueue<float[], float> result = new PriorityQueue<float[], float>(count, Comparer<float>.Create((x, y) => y.CompareTo(x)));
        Stack<Node> stack = new Stack<Node>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            Node currentNode = stack.Pop();

            if (result.TryPeek(out var _, out var maxDistance) && DistanceCalculator.EuclideanDistance(vector, currentNode.Center) - currentNode.Radius > maxDistance)
            {
                continue;
            }

            if (currentNode.IsLeaf)
            {
                LeafNode leafNode = (LeafNode)currentNode;

                foreach (float[] neighbor in leafNode.Vectors)
                {
                    float distance = DistanceCalculator.EuclideanDistance(vector, neighbor);
                    //Console.WriteLine(distance);
                    childrenVisited++;
                    if (result.Count < count)
                    {
                        result.Enqueue(neighbor, distance);
                    }
                    else if (result.TryPeek(out var _, out maxDistance))
                    {
                        if (distance < maxDistance)
                        {
                            result.DequeueEnqueue(neighbor, distance);
                        }
                    }
                }
            }
            else
            {
                (float distance, Node child)[] childrenWithDistances = new (float, Node)[currentNode.Children.Length];
                for (int i = 0; i < currentNode.Children.Length; i++)
                {
                    Node child = currentNode.Children[i];
                    float distance = DistanceCalculator.EuclideanDistance(vector, child.Center);
                    childrenWithDistances[i] = (distance, child);
                }

                Array.Sort(childrenWithDistances, (x, y) => y.distance.CompareTo(x.distance));

                foreach ((float distance, Node child) in childrenWithDistances)
                {
                    stack.Push(child);
                }
            }
        }

        float[][] resultArray = new float[result.Count][];
        int index = result.Count - 1;
        while (result.Count > 0)
        {
            if (!result.TryDequeue(out var v, out var _))
                throw new InvalidOperationException("Failed to dequeue from the priority queue.");
            resultArray[index--] = v;
        }

        return resultArray;
    }
}

internal class Program
{
    const int count = 10000;
    static readonly int[] dimensions = [2, 3, 8, 16, 64, 512];
    private static async Task Main(string[] args)
    {
        if (args.Length == 0)
        {
            Random random = new Random(222);

            foreach (int dimension in dimensions)
            {
                int childrenVisited = TestChildrenVisited(random, dimension);
                Console.WriteLine($"{dimension}, {childrenVisited}");
            }
        }
        else if (args.Length == 1 && args[0] == "vlx")
        {
            await TestWithRecipes();

        }
    }

    private static async Task TestWithRecipes()
    {
        IRecipeApi api = ConnectionFactory.Get<IRecipeApi>("address=127.0.0.1:7568;");
        List<RecipeDTO> recipes = await api.GetAllRecipes();

        Tree tree = Tree.Create(recipes.Select(r => r.IngredientEmbedding!).ToArray(), 512);
        float[] vanillaQuery = new double[] { 0.12642533, -0.6746977, 0.19500737, 0.30904445, 0.6286499, -0.346952, 0.645033, 0.16926591, -0.17738096, -0.35295796, 0.110270046, -0.07824657,
        -0.38124013, -0.60636634, -0.045498062, 0.07291939, 0.57844037, 0.29215947, -0.037318736, -0.016705235, -0.35300756, 0.0074023604, -0.04274619, 0.39105237, -0.2859217, 0.3225388, 0.15859778,
        -0.05765826, 0.45667005, -1.3033922, -0.008305018, 0.06877253, 0.30186257, -0.25137836, -0.73825645, 0.20581995, -0.40205267, -0.16268794, 0.052079797, -0.57959193, -0.17618243, -0.3797737,
        -0.25023258, 0.21715687, -0.218699, -0.28601763, -0.09313295, -0.050123256, 0.30751833, 0.47410384, 0.4239559, -0.4671588, -0.22660707, 0.22413476, 0.042180166, 0.10453809, -0.7620558,
        0.1551552, 0.57322097, 0.395653, -0.5010125, 0.17402875, -0.15835194, 0.45979533, 0.83058697, 0.08046592, 0.012936205, -0.16718872, -0.17786206, -0.5359171, -0.24992764, 0.14403145,
        0.12423518, 0.23032176, -0.04032344, 0.42841884, -0.098854005, -0.68091005, -0.32279864, 0.35326672, -0.1298061, 0.4573697, -0.17253788, -0.33874658, -0.101423234, 0.038578074, 0.11972776,
        0.24012978, 0.1723008, 0.087995596, -0.17973082, 0.56641465, 0.21986546, 0.41405487, -0.4345814, 0.5378811, 0.4289693, -0.109877564, 0.17153364, 1.6194515, -0.11058527, 0.15683301, -0.69787055,
        -0.015530288, 0.09387546, -0.37053427, -0.2553803, -0.30115548, 0.026744535, 0.33790135, 0.025639666, -0.4981281, -0.0837416, -0.3885412, 0.1022251, -0.79422396, 0.3141874, 0.16392879,
        0.15657963, 0.028795877, 0.06925777, 0.31241056, 0.27237236, 0.14327155, -0.4373549, -0.55454594, 0.63856035, -3.545028E-32, -0.35210648, -0.12185067, 0.0009938305, 0.09070688, 0.9644429,
        -0.044008385, -0.04560442, 0.12766576, -0.35447526, 0.46979538, 0.004867842, 0.012778102, -0.84531355, 0.11660645, 0.8403675, 0.2706524, 0.367949, 0.18561085, 0.5634566, -0.12148621, -0.741883,
        0.6882947, 0.18045987, 0.97550327, -0.83374405, -0.085240535, -0.14093034, -0.22965719, -0.02051188, -0.05276145, -0.08200064, -0.26167735, 0.0925503, 0.06346899, 0.27423808, -0.0004918377,
        0.281615, -0.41300213, -0.4789919, 0.018917026, -0.39233342, 0.18119021, -0.32523707, -0.06093697, -0.45293662, 0.2736486, 0.18694937, 0.27305874, -0.5128533, 0.027884364, -0.41876057,
        -0.0073040524, 0.015692802, 0.20057078, -0.6136889, -0.29474106, -0.21129985, 0.12334418, 0.43955216, 0.01908747, -0.013856247, 0.5407035, -0.09445002, -0.38520932, 0.1110211, -0.28524008,
        -0.0642266, -0.009225383, 0.8261222, -0.28996372, -0.50592893, 0.29387757, 0.025781414, 0.4859024, 0.24128656, -0.038524672, -0.11941546, -0.06312566, 0.4769968, -0.061071187, -0.48621598,
        0.1265557, -0.5231693, 0.6074524, 0.11599827, -0.045108855, -0.8708051, 0.25451073, 0.43357846, 0.20913382, -0.7810426, -0.32307652, 0.3224481, 0.15456921, -0.364315, 3.1458686E-32,
        -0.048053104, -0.42006674, -0.42886254, 1.1269585, 0.26825327, 0.012464583, -0.49450815, 0.24789278, -0.053070113, -0.38748577, 0.49593672, -0.35672224, 0.60545963, 0.5917385, 0.06736076,
        0.90130836, 0.095436536, 0.24455549, -0.049611587, 0.41778407, -0.5646871, 0.40163347, -1.1175466, 0.27006355, -0.07545054, 0.91678005, -0.47134876, 0.6015784, -0.4970365, 0.41176853,
        0.34408176, 0.16304506, 0.5128923, -0.21477671, 0.28392345, 0.48844376, 0.34696212, 0.47469154, -0.20385335, 0.33763644, -0.019550508, 0.029102415, 0.00067538023, 0.30379394, 0.060633004,
        0.2968605, 0.23106529, -0.09026655, 0.6834435, 0.6711693, -0.0078079104, 0.18827282, -0.43408403, -0.5403779, -0.11168406, -0.49864998, -0.49239293, -0.043256104, 0.20264716, 0.10712567,
        0.19599022, 0.22746055, -0.34615, -0.20114195, -0.23369603, 0.3019004, -0.5784537, -0.2860446, -0.31115058, -0.15064219, 0.39216956, -0.010072594, 0.20288296, 0.08992692, -0.0065440736,
        -0.661202, 0.47563946, -0.06890877, -0.17847712, -0.2787135, -0.010994285, 0.21053584, -0.22381884, -0.04267935, 0.14543621, 0.41468462, 0.0011907121, -0.08629965, -0.038190167, -0.20406775,
        0.064313434, 0.3106057, -0.4598885, 0.21378928, 0.24143374, -9.0835236E-08, -0.038789704, -0.20827024, 0.03327738, 0.38777363, 0.75397754, 0.74056864, 0.065039985, 0.03770045, -0.27190092,
        0.18289918, -0.13951111, -0.0077695227, 0.10473711, 0.23093657, 0.3932532, 0.22150196, -0.36956468, 0.29317078, -0.41575098, -0.070947304, -0.25201505, 0.090427935, 0.3206649, -0.58351415,
        -0.22311725, -0.61619663, 0.23836483, -0.32728827, 0.6124147, 0.44785073, 0.5061579, 0.639906, -0.55041546, -0.197528, -0.058990076, 0.031002829, -0.030038392, -0.16444394, -0.29736158,
        -0.14896232, -0.3705779, 0.056921452, 0.027400047, -0.71126866, -0.48309258, -0.71736306, 0.22917648, -0.43323025, 0.004760454, 0.13911782, 0.25172192, -0.04349631, -0.0870259, 0.18204655,
        1.1518462, -0.91903967, 0.30912253, 0.10177193, -0.6559594, -0.6938789, 0.46748042, -0.11972367, 1.038476, -0.84680223 }.Select(d => (float)(d)).ToArray();
        IEnumerable<float[]> neighbors = tree.NearestNeighbors(vanillaQuery, 10, out int childrenVisited);
        Console.WriteLine($"Children visited: {childrenVisited}");
    }

    private static int TestChildrenVisited(Random random, int dimension)
    {
        float[][] vectors = RandomVectors(random, count, dimension);
        Tree graph = Tree.Create(vectors);

        float[] queryVector = new float[dimension];

        for (int i = 0; i < dimension; i++)
        {
            queryVector[i] = random.NextSingle();
        }

        IEnumerable<float[]> neighbors = graph.NearestNeighbors(queryVector, 10, out int childrenVisited);

        float[] distances = new float[vectors.Length];
        for (int i = 0; i < vectors.Length; i++)
        {
            distances[i] = DistanceCalculator.EuclideanDistance(queryVector, vectors[i]);
        }

        Array.Sort(distances);

        bool allEqual = true;
        int index = 0;
        foreach (float[] neighbor in neighbors)
        {
            float distance = DistanceCalculator.EuclideanDistance(queryVector, neighbor);
            allEqual = allEqual && distance == distances[index++];
        }

        if (!allEqual)
            throw new InvalidOperationException("Distances do not match the expected values.");

        return childrenVisited;
    }

    private static float[][] RandomVectors(Random random, int count, int dimension)
    {
        float[][] vectors = new float[count][];
        for (int i = 0; i < count; i++)
        {
            vectors[i] = new float[dimension];
            for (int j = 0; j < dimension; j++)
            {
                vectors[i][j] = random.NextSingle();
            }
        }
        return vectors;
    }
}