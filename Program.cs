using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CsvHelper;
using CsvHelper.Configuration;
using Npgsql;
using System.Text.Json;
using System.Globalization;
using System.ComponentModel.DataAnnotations.Schema;
using Pgvector;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

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

    class Program
    {
        const string dbName = "recipes_db";

        private static InferenceSession onnxSession;
        private static BertTokenizer tokenizer;
        private static readonly string ModelPath = "minilm_onnx/model.onnx";
        private static readonly string TokenPath = "minilm_onnx/vocab.txt";
        // Simple tokenizer (for demo purposes; replace with proper tokenizer for production)
        
        private static DenseTensor<long> Tokenize(string text)
        {
            IReadOnlyList<int> encoded = tokenizer.EncodeToIds(text);
            DenseTensor<long> result = new([1, encoded.Count]);

            for (int i = 0; i < encoded.Count; i++)
            {
                result[0, i] = encoded[i];
            }

            return result;
        }

        // Generate embedding using MiniLM ONNX model
        private static DenseTensor<float> GenerateEmbedding(string[] ingredients)
        {
            var inputText = string.Join(" ", ingredients); // Combine ingredients into a single string
            DenseTensor<long> inputTensor = Tokenize(inputText);
            DenseTensor<long> attentionMask = CreateAttentionMask(inputTensor);

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", inputTensor),
                NamedOnnxValue.CreateFromTensor("attention_mask", attentionMask)
            };

            using var results = onnxSession.Run(inputs);
            return (DenseTensor<float>)results[1].Value;
        }

        private static DenseTensor<long> CreateAttentionMask(DenseTensor<long> inputTensor)
        {
            // Create attention mask: 1 for real tokens, 0 for padding
            var attentionMask = new DenseTensor<long>(inputTensor.Dimensions);
            for (int i = 0; i < inputTensor.Length; i++)
            {
                attentionMask[0, i] = inputTensor[0, i] > 0 ? 1 : 0; // Assuming non-zero tokens are real
            }
            return attentionMask;
        }

        // Load recipes from CSV
        private static IEnumerable<Recipe> LoadRecipesFromCsv(string csvPath)
        {
            var reader = new StreamReader(csvPath);
            var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true
            });
            csv.Context.RegisterClassMap<RecipeMap>();
            return csv.GetRecords<Recipe>();
        }

        // Store recipes in PostgreSQL
        private static long StoreRecipesInDb(IEnumerable<Recipe> recipes, NpgsqlDataSource dataSource)
        {
            using var conn = dataSource.CreateConnection();
            conn.Open();
            long count = 0;

            foreach (var recipe in recipes)
            {
                // Parse NER (ingredients) to generate embedding
                var nerIngredients = JsonSerializer.Deserialize<string[]>(recipe.NER);
                var embedding = GenerateEmbedding(nerIngredients);

                using var cmd = new NpgsqlCommand(
                    @"INSERT INTO recipes (title, ingredients, directions, link, source, ner, ingredient_embedding)
                      VALUES (@title, @ingredients, @directions, @link, @source, @ner, @embedding)",
                    conn);

                cmd.Parameters.AddWithValue("title", recipe.Title);
                cmd.Parameters.AddWithValue("ingredients", NpgsqlTypes.NpgsqlDbType.Jsonb, recipe.Ingredients);
                cmd.Parameters.AddWithValue("directions", NpgsqlTypes.NpgsqlDbType.Jsonb, recipe.Directions);
                cmd.Parameters.AddWithValue("link", recipe.Link ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("source", recipe.Source ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("ner", NpgsqlTypes.NpgsqlDbType.Jsonb, recipe.NER);
                cmd.Parameters.AddWithValue("embedding", new Vector(embedding.ToArray()));

                cmd.ExecuteNonQuery();
                count++;
            }

            conn.Close();

            return count;
        }

        // Search for similar recipes
        private static List<(string Title, float Similarity)> SearchSimilarRecipes(
            string[] queryIngredients, 
            NpgsqlConnection conn, 
            int limit = 3)
        {
            var queryEmbedding = GenerateEmbedding(queryIngredients);
            conn.Open();

            using var cmd = new NpgsqlCommand(
                @"SELECT title, 1 - (ingredient_embedding <=> @query_embedding) AS similarity
                  FROM recipes
                  ORDER BY ingredient_embedding <=> @query_embedding
                  LIMIT @limit",
                conn);

            cmd.Parameters.AddWithValue("query_embedding", new Vector(queryEmbedding.ToArray()));
            cmd.Parameters.AddWithValue("limit", limit);

            var results = new List<(string Title, float Similarity)>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                results.Add((reader.GetString(0), (float)reader.GetDouble(1)));
            }

            conn.Close();
            return results;
        }

        private static void InitDatabase(string connectionString)
        {
            // Connect to default 'postgres' database to create/check recipes_db
            var baseConnString = connectionString.Replace($"Database={dbName}", "Database=postgres");
            using var conn = new NpgsqlConnection(baseConnString);
            conn.Open();

            // Check if database exists, create if it doesn't
            using (var cmd = new NpgsqlCommand(
                $"SELECT 1 FROM pg_database WHERE datname = @dbName", conn))
            {
                cmd.Parameters.AddWithValue("dbName", dbName);
                var exists = cmd.ExecuteScalar() != null;
                if (!exists)
                {
                    using var createCmd = new NpgsqlCommand(
                        $"CREATE DATABASE {dbName}", conn);
                    createCmd.ExecuteNonQuery();
                    Console.WriteLine($"Created database {dbName}.");
                }
            }
            conn.Close();

            // Connect to recipes_db to create schema
            using var dbConn = new NpgsqlConnection(connectionString);
            dbConn.Open();

            // Create pgvector extension, table, and index
            var schemaSql = @"
                CREATE EXTENSION IF NOT EXISTS vector;

                CREATE TABLE IF NOT EXISTS recipes (
                    id SERIAL PRIMARY KEY,
                    title TEXT NOT NULL,
                    ingredients JSONB NOT NULL,
                    directions JSONB NOT NULL,
                    link TEXT,
                    source TEXT,
                    ner JSONB NOT NULL,
                    ingredient_embedding VECTOR(384)
                );

                CREATE INDEX IF NOT EXISTS recipes_embedding_idx 
                ON recipes USING hnsw (ingredient_embedding vector_cosine_ops);
            ";

            using var schemaCmd = new NpgsqlCommand(schemaSql, dbConn);
            schemaCmd.ExecuteNonQuery();
            Console.WriteLine("Database schema initialized.");

            dbConn.Close();
        }

        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Usage: RecipeVectorSearch <command>");
                return;
            }

            string connectionString = "Host=localhost;Username=postgres;Password=pass123;Database=recipes_db";
            var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
            dataSourceBuilder.UseVector();
            NpgsqlDataSource dataSource = dataSourceBuilder.Build();
            string csvPath = "recipes.csv";
            
            // Initialize ONNX model
            onnxSession = new InferenceSession(ModelPath);
            tokenizer = BertTokenizer.Create(TokenPath);

            if (args[0] == "load")
            {
                Console.WriteLine("Loading recipes from CSV...");

                if(args.Length == 2 && File.Exists(args[1]))
                {
                    csvPath = args[1];
                }

                // Step 1: Load recipes from CSV
                var recipes = LoadRecipesFromCsv(csvPath);

                // Step 2: Store recipes in PostgreSQL
                long count = StoreRecipesInDb(recipes, dataSource);
                Console.WriteLine($"{count} recipes stored in PostgreSQL.");

            }
            else if (args[0] == "initdb")
            {
                Console.WriteLine("Initializing database...");
                InitDatabase(connectionString);
            }
            else if (args[0] == "dropdb")
            {
                DropDatabase(connectionString);
            }
            else if (args[0] == "resetdb")
            {
                Console.WriteLine("Resetting database...");
                DropDatabase(connectionString);
                InitDatabase(connectionString);
            }
            else if (args[0] == "search")
            {
                if (args.Length < 2)
                {
                    Console.WriteLine("Usage: RecipeVectorSearch search <ingredient1,ingredient2,...>");
                    return;
                }

                string[] queryIngredients = args.Skip(1).SelectMany(arg => arg.Split(',')).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
                var similarRecipes = SearchSimilarRecipes(queryIngredients, dataSource.CreateConnection());

                Console.WriteLine("\nTop similar recipes:");
                foreach (var (title, similarity) in similarRecipes)
                {
                    Console.WriteLine($"Title: {title}, Similarity: {similarity:F3}");
                }
            }
            else
            {
                Console.WriteLine("Unknown command. Use 'load', 'initdb', or 'search'.");
                return;
            }

        }

        private static void DropDatabase(string connectionString)
        {
            Console.WriteLine("Dropping database...");
            var baseConnString = connectionString.Replace($"Database={dbName}", "Database=postgres");
            using var conn = new NpgsqlConnection(baseConnString);
            conn.Open();
            using var cmd = new NpgsqlCommand("DROP DATABASE IF EXISTS recipes_db", conn);
            cmd.ExecuteNonQuery();
            Console.WriteLine("Database dropped.");
            conn.Close();
        }
    }
}