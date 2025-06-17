using Microsoft.ML.OnnxRuntime.Tensors;

namespace RecipeVectorSearch
{
    internal interface IDatabaseService
    {
        void Dispose();
        Task Drop();
        string GetConnectionString();
        Task Initialize();
        Task Reset();
        Task<List<RecipeResult>> SearchSimilarRecipes(float[] embedding, int limit = 20);
        Task StoreRecipe(Recipe recipe, DenseTensor<float> embeddingTensor, CancellationToken token);
        void UpdateConnectionString(string newConnectionString);

        string Name { get; }

        string ShortName { get; }
    }
}
