namespace VlxAPI;

public class RecipeDTO
{
    // VeloxDB IDs are 64-bit integers (long in C#) [7]
    public long Id { get; set; }

    public string? Title { get; set; }

    // JSONB fields are stored as C# string?s in VeloxDB [8].
    // Your application code (outside the DB API) will handle JSON serialization/deserialization.
    public string? IngredientsJson { get; set; }
    public string? DirectionsJson { get; set; }
    public string? NerJson { get; set; }

    public string? Link { get; set; }
    public string? Source { get; set; }

    // VECTOR(384) is mapped to a C# List<float> or float[] in DTOs [9, 10].
    public float[]? IngredientEmbedding { get; set; }
}
