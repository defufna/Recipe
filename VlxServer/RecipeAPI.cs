using System.Numerics;
using VeloxDB.ObjectInterface;
using VeloxDB.Protocol;
using VlxAPI;

namespace RecipesDB;

[DbAPI(Name = "RecipeApi")] // Name must match the contract interface [27]
public class RecipeApi
{
    [DbAPIOperation(OperationType = DbAPIOperationType.ReadWrite)]
    public long CreateRecipe(ObjectModel om, RecipeDTO newRecipe)
    {
        var recipe = Recipe.FromDTO(om, newRecipe);

        return recipe.Id;;
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

    [DbAPIOperation(OperationType = DbAPIOperationType.ReadWrite)]
    public void UpdateRecipe(ObjectModel om, RecipeDTO updatedRecipe) // Use void as return type for async method [26]
    {
        Recipe.FromDTO(om, updatedRecipe, allowUpdate: true);
    }

    [DbAPIOperation(OperationType = DbAPIOperationType.ReadWrite)]
    public void Reset(ObjectModel om)
    {
        foreach (Recipe recipe in om.GetAllObjects<Recipe>())
        {
            recipe.Delete();
        }
    }

    [DbAPIOperation(OperationType = DbAPIOperationType.Read)]
    public RecipeResultDTO[] SemanticSearch(ObjectModel om, float[] embedding, int limit = -1)
    {
        IEnumerable<Recipe> recipes = om.GetAllObjects<Recipe>();

        List<RecipeResultDTO> result = new();

        foreach (Recipe recipe in recipes)
        {
            float similarity = VectorHelper.CosineDistance(embedding, recipe.IngredientEmbedding);
            result.Add(new RecipeResultDTO{Recipe = recipe.ToDTO(), Similarity = similarity });
        }

        result.Sort((r1, r2) => MathF.Sign(r1.Similarity - r2.Similarity));

        if (limit == -1)
            return result.ToArray();

        return result.Take(limit).ToArray();
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