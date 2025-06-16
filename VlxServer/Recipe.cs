using VeloxDB.ObjectInterface;
using VlxAPI;

namespace RecipesDB;

[DatabaseClass]
public abstract partial class Recipe : DatabaseObject
{
    [DatabaseProperty]
    public abstract string Title { get; set; }

    [DatabaseProperty]
    public abstract string IngredientsJson { get; set; }

    [DatabaseProperty]
    public abstract string DirectionsJson { get; set; }

    [DatabaseProperty]
    public abstract string Link { get; set; }

    [DatabaseProperty]
    public abstract string Source { get; set; }

    [DatabaseProperty]
    public abstract string NerJson { get; set; }

    [DatabaseProperty]
    public abstract DatabaseArray<float> IngredientEmbedding { get; set; }

    public static partial Recipe FromDTO(ObjectModel om, RecipeDTO dto, bool allowUpdate = false);

    public partial RecipeDTO ToDTO();
}