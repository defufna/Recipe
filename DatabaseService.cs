using Npgsql;
using Pgvector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RecipeVectorSearch
{

    /// <summary>
    /// Manages database connections, schema, and recipe data operations.
    /// </summary>
    internal class DatabaseService : IDisposable
    {
        private const string DbName = "recipes_db";
        private readonly EmbeddingService _embeddingService;
        private NpgsqlDataSource _dataSource;
        private string _connectionString;

        public DatabaseService(string connectionString, EmbeddingService embeddingService)
        {
            _connectionString = connectionString;
            _embeddingService = embeddingService;
            _dataSource = BuildDataSource(connectionString);
        }

        private static NpgsqlDataSource BuildDataSource(string connectionString)
        {
            var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
            dataSourceBuilder.UseVector();
            return dataSourceBuilder.Build();
        }

        public void UpdateConnectionString(string newConnectionString)
        {
            _dataSource?.Dispose();
            _connectionString = newConnectionString;
            _dataSource = BuildDataSource(newConnectionString);
        }

        public string GetConnectionString() => _connectionString;

        public void Initialize()
        {
            var baseConnString = _connectionString.Replace($"Database={DbName}", "Database=postgres");
            using (var conn = new NpgsqlConnection(baseConnString))
            {
                conn.Open();
                using var cmd = new NpgsqlCommand($"SELECT 1 FROM pg_database WHERE datname = @dbName", conn);
                cmd.Parameters.AddWithValue("dbName", DbName);
                if (cmd.ExecuteScalar() == null)
                {
                    using var createCmd = new NpgsqlCommand($"CREATE DATABASE \"{DbName}\"", conn);
                    createCmd.ExecuteNonQuery();
                }
            }

            using var dbConn = _dataSource.CreateConnection();
            dbConn.Open();
            const string schemaSql = @"
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
                ON recipes USING hnsw (ingredient_embedding vector_cosine_ops);";
            using var schemaCmd = new NpgsqlCommand(schemaSql, dbConn);
            schemaCmd.ExecuteNonQuery();
        }

        public void Drop()
        {
            var baseConnString = _connectionString.Replace($"Database={DbName}", "Database=postgres");
            using var conn = new NpgsqlConnection(baseConnString);
            conn.Open();
            // Terminate connections before dropping to avoid errors.
            using var terminateCmd = new NpgsqlCommand($"SELECT pg_terminate_backend(pg_stat_activity.pid) FROM pg_stat_activity WHERE pg_stat_activity.datname = '{DbName}' AND pid <> pg_backend_pid();", conn);
            terminateCmd.ExecuteNonQuery();
            using var dropCmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{DbName}\" WITH (FORCE)", conn);
            dropCmd.ExecuteNonQuery();
        }

        public void Reset()
        {
            Drop();
            Initialize();
        }

        public async Task<long> StoreRecipes(IEnumerable<Recipe> recipes, Action<long> progress, CancellationToken token)
        {
            long count = 0;

            CancellationTokenSource cts = new CancellationTokenSource();

            Task progressUpdater = Task.Run(async () =>
            {
                if (progress == null)
                {
                    return;
                }

                while (!cts.Token.IsCancellationRequested)
                {
                    progress?.Invoke(count);
                    await Task.Delay(1000);
                }
            }, cts.Token);


            await Parallel.ForEachAsync(recipes, new ParallelOptions { MaxDegreeOfParallelism = 16, CancellationToken = token }, async (recipe, token) =>
            {
                var nerIngredients = JsonSerializer.Deserialize<string[]>(recipe.NER);
                if (nerIngredients == null || nerIngredients.Length == 0) return;

                if (!_embeddingService.TryGenerateEmbedding(nerIngredients, out var embeddingTensor) || embeddingTensor == null)
                {
                    return; // Skip if embedding fails
                }

                using var conn = await _dataSource.OpenConnectionAsync(token);
                using var cmd = new NpgsqlCommand(
                    @"INSERT INTO recipes (title, ingredients, directions, link, source, ner, ingredient_embedding)
                      VALUES (@title, @ingredients, @directions, @link, @source, @ner, @embedding)", conn);

                cmd.Parameters.AddWithValue("title", recipe.Title);
                cmd.Parameters.AddWithValue("ingredients", NpgsqlTypes.NpgsqlDbType.Jsonb, recipe.Ingredients);
                cmd.Parameters.AddWithValue("directions", NpgsqlTypes.NpgsqlDbType.Jsonb, recipe.Directions);
                cmd.Parameters.AddWithValue("link", recipe.Link ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("source", recipe.Source ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("ner", NpgsqlTypes.NpgsqlDbType.Jsonb, recipe.NER);
                cmd.Parameters.AddWithValue("embedding", new Vector(embeddingTensor.ToArray()));

                await cmd.ExecuteNonQueryAsync(token);

                Interlocked.Increment(ref count);
            });

            cts.Cancel();
            await progressUpdater;

            return count;
        }

        public List<RecipeResult> SearchSimilarRecipes(string[] queryIngredients, int limit = 20)
        {
            if (!_embeddingService.TryGenerateEmbedding(queryIngredients, out var queryEmbeddingTensor) || queryEmbeddingTensor == null)
            {
                throw new ArgumentException("Failed to generate embedding for query ingredients.");
            }

            var queryVector = new Vector(queryEmbeddingTensor.ToArray());
            using var conn = _dataSource.CreateConnection();
            conn.Open();

            using var cmd = new NpgsqlCommand(
                @"SELECT title, 1 - (ingredient_embedding <=> @query_embedding) AS similarity, ingredients, directions
                  FROM recipes
                  ORDER BY ingredient_embedding <=> @query_embedding
                  LIMIT @limit", conn);

            cmd.Parameters.AddWithValue("query_embedding", queryVector);
            cmd.Parameters.AddWithValue("limit", limit);

            var results = new List<RecipeResult>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var recipeText = $"Ingredients: \n\n{JsonToString(reader.GetString(2))}\n\nInstructions:\n\n{JsonToString(reader.GetString(3))}";
                results.Add(new RecipeResult(reader.GetString(0), (float)reader.GetDouble(1), recipeText));
            }
            return results;
        }

        private static string JsonToString(string s)
        {
            string[]? deserialized = JsonSerializer.Deserialize<string[]>(s);
            return string.Join("\n", deserialized ?? Array.Empty<string>());
        }

        public void Dispose()
        {
            _dataSource?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}