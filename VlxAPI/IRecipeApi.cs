using VeloxDB.Client;
using VeloxDB.Protocol;

namespace VlxAPI;

[DbAPI(Name = "RecipeApi")]
public interface IRecipeApi
{
    [DbAPIOperation(OperationType = DbAPIOperationType.ReadWrite)]
    [DbAPIOperationError(typeof(APINotInitializedException))]
    [DbAPIOperationError(typeof(APIArgumentException))]
    DatabaseTask<long> CreateRecipe(RecipeDTO? newRecipe);
    [DbAPIOperation(OperationType = DbAPIOperationType.Read)]
    DatabaseTask<RecipeDTO?> GetRecipe(long id);
    [DbAPIOperation(OperationType = DbAPIOperationType.Read)]
    DatabaseTask<List<RecipeDTO>> GetAllRecipes();
    [DbAPIOperation(OperationType = DbAPIOperationType.ReadWrite)]
    DatabaseTask UpdateRecipe(RecipeDTO updatedRecipe);
    [DbAPIOperation(OperationType = DbAPIOperationType.ReadWrite)]
    DatabaseTask Reset();
    [DbAPIOperation(OperationType = DbAPIOperationType.Read)]
    [DbAPIOperationError(typeof(APINotInitializedException))]
    DatabaseTask<RecipeResultDTO[]> SemanticSearch(float[] embedding, int limit, bool exact);
    [DbAPIOperation(OperationType = DbAPIOperationType.ReadWrite)]
    DatabaseTask DeleteRecipe(long id);
}
