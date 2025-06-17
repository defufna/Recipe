using Npgsql;
using Pgvector;

namespace RecipeVectorSearch
{
    /// <summary>
    /// Manages database connections, schema, and recipe data operations.
    /// </summary>
    internal class PSQLDBService : IDisposable, IDatabaseService
    {
        private const string DbName = "recipes_db";
        private NpgsqlDataSource dataSource;
        private string connectionString;

        public string Name => "PostgreSQL";

        public string ShortName => "pg";

        public PSQLDBService(string connectionString)
        {
            this.connectionString = connectionString;
            dataSource = BuildDataSource(connectionString);
        }

        private static NpgsqlDataSource BuildDataSource(string connectionString)
        {
            var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
            dataSourceBuilder.UseVector();
            return dataSourceBuilder.Build();
        }

        public void UpdateConnectionString(string newConnectionString)
        {
            dataSource?.Dispose();
            connectionString = newConnectionString;
            dataSource = BuildDataSource(newConnectionString);
        }

        public string GetConnectionString() => connectionString;

        public async Task Initialize()
        {
            var baseConnString = connectionString.Replace($"Database={DbName}", "Database=postgres");
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

            using var dbConn = dataSource.CreateConnection();
            await dbConn.OpenAsync();
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
            await schemaCmd.ExecuteNonQueryAsync();
            UpdateConnectionString(connectionString);
        }

        public async Task Drop()
        {
            var baseConnString = connectionString.Replace($"Database={DbName}", "Database=postgres");
            using var conn = new NpgsqlConnection(baseConnString);
            conn.Open();
            // Terminate connections before dropping to avoid errors.
            using var terminateCmd = new NpgsqlCommand($"SELECT pg_terminate_backend(pg_stat_activity.pid) FROM pg_stat_activity WHERE pg_stat_activity.datname = '{DbName}' AND pid <> pg_backend_pid();", conn);
            terminateCmd.ExecuteNonQuery();
            using var dropCmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{DbName}\" WITH (FORCE)", conn);
            await dropCmd.ExecuteNonQueryAsync();
        }

        public async Task Reset()
        {
            await Drop();
            await Initialize();
        }

        public async Task StoreRecipe(Recipe recipe, Microsoft.ML.OnnxRuntime.Tensors.DenseTensor<float> embeddingTensor, CancellationToken token)
        {
            using NpgsqlConnection conn = await dataSource.OpenConnectionAsync(token);
            using NpgsqlCommand cmd = new NpgsqlCommand(
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
        }

        public async Task<List<RecipeResult>> SearchSimilarRecipes(float[] embedding, int limit = 20)
        {
            var queryVector = new Vector(embedding);
            using var conn = dataSource.CreateConnection();
            await conn.OpenAsync();

            using var cmd = new NpgsqlCommand(
                @"SELECT title, (ingredient_embedding <=> @query_embedding) AS similarity, ingredients, directions
                  FROM recipes
                  ORDER BY ingredient_embedding <=> @query_embedding
                  LIMIT @limit", conn);

            cmd.Parameters.AddWithValue("query_embedding", queryVector);
            cmd.Parameters.AddWithValue("limit", limit);

            var results = new List<RecipeResult>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var recipeText = RecipeFormatter.ToString(reader.GetString(2), reader.GetString(3));
                results.Add(new RecipeResult(reader.GetString(0), (float)reader.GetDouble(1), recipeText));
            }
            return results;
        }

        public void Dispose()
        {
            dataSource?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}