using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime.Tensors;
using VeloxDB.Client;
using VlxAPI;

namespace RecipeVectorSearch
{
    internal class VlxDBService : IDisposable, IDatabaseService
    {
        private string connectionString;
        private IRecipeApi api;

        public VlxDBService(string connectionString)
        {
            UpdateConnectionString(connectionString);
        }

        public string Name => "VeloxDB";

        public string ShortName => "vlx";

        public void Dispose()
        {
            
        }

        public async Task Drop()
        {
            await api.Reset();
        }

        public string GetConnectionString() => connectionString;
        

        public async Task Initialize()
        {
            await Drop();
        }

        public async Task Reset()
        {
            await Drop();
        }

        public async Task<List<RecipeResult>> SearchSimilarRecipes(float[] embedding, int limit = 20)
        {
            RecipeResultDTO[] dtos = await api.SemanticSearch(embedding, limit);

            List<RecipeResult> result = new(dtos.Length);

            foreach (RecipeResultDTO dto in dtos)
            {
                RecipeResult recipeResult = new RecipeResult(dto.Recipe.Title, dto.Similarity, RecipeFormatter.ToString(dto.Recipe.IngredientsJson, dto.Recipe.DirectionsJson));
                result.Add(recipeResult);
            }

            return result;
        }

        public async Task StoreRecipe(Recipe recipe, DenseTensor<float> embeddingTensor, CancellationToken token)
        {
            RecipeDTO recipeDTO = new RecipeDTO()
            {
                DirectionsJson = recipe.Directions,
                IngredientEmbedding = embeddingTensor.ToArray(),
                IngredientsJson = recipe.Ingredients,
                Link = recipe.Link,
                NerJson = recipe.NER,
                Source = recipe.Source,
                Title = recipe.Title
            };

            await api.CreateRecipe(recipeDTO);
        }

        public void UpdateConnectionString(string newConnectionString)
        {
            connectionString = newConnectionString;
            api = ConnectionFactory.Get<IRecipeApi>(newConnectionString);
        }
    }
}