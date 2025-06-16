using CsvHelper.Configuration;

namespace RecipeVectorSearch
{
    // CSV mapping
    public class RecipeMap : ClassMap<Recipe>
    {
        public RecipeMap()
        {
            Map(m => m.Id).Name(""); // Assuming first column is unnamed
            Map(m => m.Title).Name("title");
            Map(m => m.Ingredients).Name("ingredients");
            Map(m => m.Directions).Name("directions");
            Map(m => m.Link).Name("link");
            Map(m => m.Source).Name("source");
            Map(m => m.NER).Name("NER");
        }
    }
}