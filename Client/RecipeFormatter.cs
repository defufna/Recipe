using System.Text.Json;

namespace RecipeVectorSearch
{
    internal static class RecipeFormatter
    {
        public static string ToString(string ingredientsJson, string directionsJson)
        {
            return $"Ingredients: \n\n{JsonToString(ingredientsJson)}\n\nInstructions:\n\n{JsonToString(directionsJson)}";
        }

        private static string JsonToString(string s)
        {
            string[] deserialized = JsonSerializer.Deserialize<string[]>(s);
            return string.Join("\n", deserialized ?? Array.Empty<string>());
        }

    }
}