using RecipeVectorSearch.UI;
using System;

namespace RecipeVectorSearch
{
    class Program
    {
        // --- Configuration ---
        private static readonly string ModelPath = "minilm_onnx/model.onnx";
        private static readonly string TokenPath = "minilm_onnx/vocab.txt";
        private static readonly string ConnectionString = "Host=localhost;Username=postgres;Password=pass123;Database=recipes_db";

        static void Main(string[] args)
        {
            try
            {
                // 1. Initialize services
                // The EmbeddingService is created once and passed to the DatabaseService.
                using var embeddingService = new EmbeddingService(ModelPath, TokenPath, ExecutionProvider.GPU);
                using var databaseService = new DatabaseService(ConnectionString, embeddingService);

                // 2. Initialize and run the UI, injecting the required services.
                var tui = new RecipeTUI(databaseService, embeddingService);
                tui.Run();
            }
            catch (Exception ex)
            {
                // Fallback for critical initialization errors (e.g., model file not found)
                Console.WriteLine($"A critical error occurred during startup:\n {ex}");
                Console.WriteLine("Please ensure all configuration files (model, vocab) are in place.");
                Console.WriteLine("Press any key to exit.");
                Console.ReadKey();
            }
        }
    }
}