using System.Numerics;

static class DistanceCalculator
{

    public static float EuclideanDistance(float[] first, float[] second)
    {
        if (first.Length != second.Length)
            throw new ArgumentException("Vectors must be of the same length");

        float sum = 0;
        int i = 0;
        // Process vectors in chunks
        for (; i <= first.Length - Vector<float>.Count; i += Vector<float>.Count)
        {
            Vector<float> va = new(first, i);
            Vector<float> vb = new(second, i);
            sum += Vector.Dot(va - vb, va - vb); // Vectorized squared difference
        }

        // Process any remaining elements (tail)
        for (; i < first.Length; i++)
        {
            float diff = first[i] - second[i];
            sum += diff * diff;
        }
        return MathF.Sqrt(sum);
    }

    public static float CosineDistance(float[] first, float[] second)
    {
        if (first.Length != second.Length)
            throw new ArgumentException("Vectors must be of the same length");

        float dotProduct = 0;
        float normASquared = 0;
        float normBSquared = 0;

        int i = 0;
        // Process vectors in chunks
        for (; i <= first.Length - Vector<float>.Count; i += Vector<float>.Count)
        {
            Vector<float> va = new(first, i);
            Vector<float> vb = new(second, i);

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
