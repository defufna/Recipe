using System.Numerics;
using VeloxDB.ObjectInterface;

internal static class VectorHelper
{
	public static float CosineDistance(float[] first, DatabaseArray<float> second)
	{
		if (first.Length != second.Count)
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