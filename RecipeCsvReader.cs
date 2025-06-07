using CsvHelper;
using CsvHelper.Configuration;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace RecipeVectorSearch.Data
{
    public static class RecipeCsvReader
    {
        public static IEnumerable<Recipe> LoadRecipes(string csvPath)
        {
            var reader = new StreamReader(csvPath);
            var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
            });
            csv.Context.RegisterClassMap<RecipeMap>();
            // Using ToList() materializes the collection, ensuring the file stream is not
            // closed by the 'using' statement before all records are read.
            return csv.GetRecords<Recipe>();
        }
    }
}