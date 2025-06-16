namespace RecipeVectorSearch
{
    internal record struct RecipeResult(string Title, float Similarity, string Recipe)
    {
        public override string ToString() => $"{Title} (Similarity: {Similarity:F3})";
    }
}