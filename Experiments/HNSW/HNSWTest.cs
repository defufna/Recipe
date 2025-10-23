using NUnit.Framework;

namespace HNSW;

[TestFixture]
public class HNSWTest
{
    [Test]
    public void TestHSNW()
    {
        const int count = 100;
        HNSWCollection hnsw = new HNSWCollection(4);
        List<float[]> vectors = new List<float[]>(count);

        Random random = new Random();
        for (int i = 0; i < count; i++)
        {
            float[] vector = new float[256];
            for (int j = 0; j < vector.Length; j++)
            {
                vector[j] = random.NextSingle();
            }
            vectors.Add(vector);
            hnsw.Add(vector);
        }

        int randomIndex = random.Next(count);
        float[] queryVector = vectors[randomIndex];
        var neighbors = hnsw.Search(queryVector, 5, 10);

        vectors.Sort((a, b) =>
        {
            float distanceA = DistanceCalculator.CosineDistance(queryVector, a);
            float distanceB = DistanceCalculator.CosineDistance(queryVector, b);
            return Math.Sign(distanceA - distanceB);
        });

        int equalCount = 0;
        for (int i = 0; i < neighbors.Count; i++)
        {
            if (VectorEqual(vectors[i], neighbors[i]))
            {
                equalCount++;
            }
        }
        Console.WriteLine($"Equal neighbors: {equalCount} out of {neighbors.Count}");
    }

    private bool VectorEqual(float[] u, float[] v)
    {
        for (int i = 0; i < u.Length; i++)
        {
            if (Math.Abs(u[i] - v[i]) > 1e-6)
            {
                return false;
            }
        }

        return true;
    }
}
