namespace RecipeVectorSearch
{
    // Model for CSV data
    public class Recipe
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Ingredients { get; set; } // JSON string
        public string Directions { get; set; } // JSON string
        public string Link { get; set; }
        public string Source { get; set; }
        public string NER { get; set; } // JSON string
    }
}