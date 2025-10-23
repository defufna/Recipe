using System.Numerics;
using VeloxDB.ObjectInterface;
using VeloxDB.Protocol;
using VeloxDB.VectorIndex;
using VlxAPI;

namespace RecipesDB;

[DbAPI(Name = "RecipeApi")] // Name must match the contract interface [27]
public class RecipeApi
{
    private const string IndexName = "RecipeIndex";

    [DbAPIOperation(OperationType = DbAPIOperationType.ReadWrite)]
    [DbAPIOperationError(typeof(APINotInitializedException))]
    [DbAPIOperationError(typeof(APIArgumentException))]
    public long CreateRecipe(ObjectModel om, RecipeDTO? newRecipe)
    {
        if(newRecipe == null || !newRecipe.Valid())
        {
            throw new APIArgumentException("Recipe is not valid");
        }

        HNSW<Recipe>? index = om.GetVectorIndex<Recipe>(IndexName);
        if (index == null)
            throw new APINotInitializedException();

        var recipe = Recipe.FromDTO(om, newRecipe);
        index.Add(recipe, newRecipe.IngredientEmbedding);
        return recipe.Id; ;
    }

    public long[] CreateRecipes(ObjectModel om, RecipeDTO[] recipes)
    {
        long[] result = new long[recipes.Length];

        for (int i = 0; i < recipes.Length; i++)
        {
            RecipeDTO recipeDTO = recipes[i];
            result[i] = CreateRecipe(om, recipeDTO);
        }

        return result;
    }

    [DbAPIOperation(OperationType = DbAPIOperationType.Read)]
    public RecipeDTO? GetRecipe(ObjectModel om, long id)
    {
        var recipe = om.GetObject<Recipe>(id);

        if (recipe == null)
        {
            return null;
        }

        return recipe.ToDTO();
    }

    [DbAPIOperation(OperationType = DbAPIOperationType.Read)]
    public List<RecipeDTO> GetAllRecipes(ObjectModel om)
    {
        List<RecipeDTO> recipes = new();

        foreach (Recipe recipe in om.GetAllObjects<Recipe>())
        {
            recipes.Add(recipe.ToDTO());
        }
        return recipes;
    }

    [DbAPIOperation(OperationType = DbAPIOperationType.ReadWrite)]
    public void UpdateRecipe(ObjectModel om, RecipeDTO updatedRecipe) // Use void as return type for async method [26]
    {
        Recipe.FromDTO(om, updatedRecipe, allowUpdate: true);
    }

    [DbAPIOperation(OperationType = DbAPIOperationType.ReadWrite)]
    public void Reset(ObjectModel om)
    {
        if (om.GetVectorIndex<Recipe>(IndexName) != null)
            om.DeleteVectorIndex(IndexName);

        foreach (Recipe recipe in om.GetAllObjects<Recipe>())
        {
            recipe.Delete();
        }
        om.CreateVectorIndex<Recipe>(IndexName, 384, 16, efConstruction: 40, distanceFunction: DistanceCalculator.CosineDistance);
    }

    [DbAPIOperation(OperationType = DbAPIOperationType.Read)]
    [DbAPIOperationError(typeof(APINotInitializedException))]
    public RecipeResultDTO[] SemanticSearch(ObjectModel om, float[] embedding, int limit = -1, bool exact = false)
    {
        if (!exact)
        {
            HNSW<Recipe>? index = om.GetVectorIndex<Recipe>(IndexName);
            if (index == null)
                throw new APINotInitializedException();

            int k = Math.Max(limit, 50);
            Recipe[] searchResults = index.Search(embedding, k);

            RecipeResultDTO[] result = new RecipeResultDTO[searchResults.Length];
            for (int i = 0; i < searchResults.Length; i++)
            {
                Recipe r = searchResults[i];
                result[i] = new RecipeResultDTO() { Recipe = r.ToDTO(), Similarity = VectorHelper.CosineDistance(embedding, r.IngredientEmbedding) };
            }
            
            return result;
        }
        else
        {
            List<RecipeResultDTO> result = new();
            IEnumerable<Recipe> recipes = om.GetAllObjects<Recipe>();

            foreach (Recipe recipe in recipes)
            {
                float similarity = VectorHelper.CosineDistance(embedding, recipe.IngredientEmbedding);
                result.Add(new RecipeResultDTO { Recipe = recipe.ToDTO(), Similarity = similarity });
            }

            result.Sort((r1, r2) => MathF.Sign(r1.Similarity - r2.Similarity));

            if (limit == -1)
                return result.ToArray();

            return result.Take(limit).ToArray();
        }
    }

    [DbAPIOperation(OperationType = DbAPIOperationType.ReadWrite)]
    public void DeleteRecipe(ObjectModel om, long id)
    {
        var recipe = om.GetObject<Recipe>(id);
        if (recipe != null)
        {
            recipe.Delete();
        }
    }
}