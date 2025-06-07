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
using System.Diagnostics;
using Terminal.Gui;
using System.Collections.ObjectModel;
using System.Threading.Channels;

namespace RecipeVectorSearch
{
    class Program
    {
        const string dbName = "recipes_db";

        private static InferenceSession onnxSession;
        private static BertTokenizer tokenizer;
        private static readonly string ModelPath = "minilm_onnx/model.onnx";
        private static readonly string TokenPath = "minilm_onnx/vocab.txt";

        private static string connectionString = "Host=localhost;Username=postgres;Password=pass123;Database=recipes_db";

        private static NpgsqlDataSource dataSource;
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
        private static bool TryGenerateEmbedding(string[] ingredients, out DenseTensor<float> embedding)
        {
            var inputText = string.Join(" ", ingredients); // Combine ingredients into a single string
            DenseTensor<long> inputTensor = Tokenize(inputText);
            DenseTensor<long> attentionMask = CreateAttentionMask(inputTensor);

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", inputTensor),
                NamedOnnxValue.CreateFromTensor("attention_mask", attentionMask)
            };

            try
            {
                using var results = onnxSession.Run(inputs);
                embedding = (DenseTensor<float>)results[1].Value;
                return true;
            }
            catch (OnnxRuntimeException ex)
            {
                File.AppendAllText("error.log", $"inputText: {inputText}\n {ex.ToString()}\n");
                embedding = null;
                return false;
            }
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

        class ConnectionPool : IDisposable
        {
            private readonly Channel<NpgsqlConnection> pool;
            private readonly int size;

            public ConnectionPool(NpgsqlDataSource dataSource, int size = 10)
            {
                this.size = size;
                pool = Channel.CreateBounded<NpgsqlConnection>(size);
                for (int i = 0; i < size; i++)
                {
                    var conn = dataSource.CreateConnection();
                    conn.Open();
                    pool.Writer.TryWrite(conn);
                }
            }

            public void Dispose()
            {
                int disposed = 0;
                while (pool.Reader.TryRead(out var conn))
                {
                    conn?.Close();
                    conn?.Dispose();
                    disposed++;
                }
                Debug.Assert(disposed == size, "Not all connections were returned to the pool.");
            }

            public async Task<NpgsqlConnection> GetConnectionAsync()
            {
                return await pool.Reader.ReadAsync();
            }

            public async Task ReturnConnection(NpgsqlConnection conn)
            {
                Debug.Assert(conn != null, "Connection cannot be null when returning to pool.");
                await pool.Writer.WriteAsync(conn);
            }
        }

        // Store recipes in PostgreSQL
        private static async Task<long> StoreRecipesInDb(IEnumerable<Recipe> recipes, NpgsqlDataSource dataSource, Action<long> progress = null)
        {
            const int parallelism = 16;
            long count = 0;

            using ConnectionPool connectionPool = new ConnectionPool(dataSource, parallelism);
            List<Task> tasks = new List<Task>();

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

            await Parallel.ForEachAsync(recipes, new ParallelOptions { MaxDegreeOfParallelism = parallelism }, async (recipe, token) =>
            {
                NpgsqlConnection conn = null;
                try
                {
                    conn = await connectionPool.GetConnectionAsync();
                    // Parse NER (ingredients) to generate embedding
                    var nerIngredients = JsonSerializer.Deserialize<string[]>(recipe.NER);
                    if (!TryGenerateEmbedding(nerIngredients, out var embedding))
                    {
                        return;
                    }

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

                    await cmd.ExecuteNonQueryAsync(token);
                }
                finally
                {
                    if (conn != null)
                    {
                        await connectionPool.ReturnConnection(conn);
                    }

                    Interlocked.Increment(ref count);
                }
            });

            cts.Cancel();
            await progressUpdater;

            return count;
        }

        // Search for similar recipes
        private static List<RecipeResult> SearchSimilarRecipes(
            string[] queryIngredients,
            NpgsqlConnection conn,
            int limit = 3)
        {
            if(!TryGenerateEmbedding(queryIngredients, out var queryEmbedding))
            {
                throw new ArgumentException("Failed to generate embedding for query ingredients.");
            }

            conn.Open();

            using var cmd = new NpgsqlCommand(
                @"SELECT title, 1 - (ingredient_embedding <=> @query_embedding) AS similarity, ingredients, directions
                  FROM recipes
                  ORDER BY ingredient_embedding <=> @query_embedding
                  LIMIT @limit",
                conn);

            cmd.Parameters.AddWithValue("query_embedding", new Vector(queryEmbedding.ToArray()));
            cmd.Parameters.AddWithValue("limit", limit);

            var results = new List<RecipeResult>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                string recipe =  $"Ingredients: \n\n{JsonToString(reader.GetString(2))}\n\nInstructions:\n\n{JsonToString(reader.GetString(3))}";
                results.Add(new RecipeResult(reader.GetString(0), (float)reader.GetDouble(1), recipe));
            }

            conn.Close();
            return results;
        }

        private static string JsonToString(string s)
        {
           string[] deserialized = JsonSerializer.Deserialize<string[]>(s) ?? Array.Empty<string>();
           return string.Join("\n", deserialized);
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
            Application.Init();

            try
            {
                // Initialize database connection
                var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
                dataSourceBuilder.UseVector();
                using (dataSource = dataSourceBuilder.Build())
                {
                    // Initialize ONNX model
                    if (File.Exists(ModelPath) && File.Exists(TokenPath))
                    {
                        SessionOptions options = new SessionOptions();
                        options.AppendExecutionProvider_OpenVINO("GPU");

                        onnxSession = new InferenceSession(ModelPath, options);
                        tokenizer = BertTokenizer.Create(TokenPath);
                    }

                    // Create and run the main window
                    var mainWindow = CreateMainWindow();
                    Application.Run(mainWindow);
                }
            }
            catch (Exception ex)
            {
                MessageBox.ErrorQuery("Error", $"Failed to initialize application: {ex.Message}", "OK");
            }
            finally
            {
                Application.Shutdown();
                dataSource?.Dispose();
                onnxSession?.Dispose();
            }
        }

        private static Window CreateMainWindow()
        {
            var window = new Window()
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill()
            };

            // Create menu bar
            var menuBar = new MenuBar();

            menuBar.Menus = new MenuBarItem[] {
                new MenuBarItem("_Database", new MenuItem[] {
                    new MenuItem("_Initialize Database", "", () => InitializeDatabaseDialog()),
                    new MenuItem("_Reset Database", "", () => ResetDatabaseDialog()),
                    new MenuItem("_Drop Database", "", () => DropDatabaseDialog()),
                    null, // Separator
                    new MenuItem("_Load Recipes", "", () => LoadRecipesDialog()),
                }),
                new MenuBarItem("_Search", new MenuItem[] {
                    new MenuItem("_Search Recipes", "", () => SearchRecipesDialog()),
                }),
                new MenuBarItem("Se_ttings", new MenuItem[] {
                    new MenuItem("_Connection String", "", () => ConnectionStringDialog()),
                }),
                new MenuBarItem("_Help", new MenuItem[] {
                    new MenuItem("_About", "", () => ShowAboutDialog()),
                })
            };

            window.Add(menuBar);

            // Create main content area
            var mainFrame = new FrameView()
            {
                X = 1,
                Y = 2,
                Width = Dim.Fill() - 1,
                Height = Dim.Fill() - 1,
                Title = "Welcome"
            };

            var welcomeText = new Label()
            {
                Text = "Recipe Vector Search Application\n\n" +
                                     "Use the menu above to:\n" +
                                     "• Initialize or manage the database\n" +
                                     "• Load recipes from CSV files\n" +
                                     "• Search for similar recipes\n\n" +
                                     "Press F10 to quit",
                X = 2,
                Y = 2,
                Width = Dim.Fill() - 4,
                Height = Dim.Fill() - 4
            };

            mainFrame.Add(welcomeText);
            window.Add(mainFrame);

            // Add status bar
            var statusBar = new StatusBar(new Shortcut[] {
                new Shortcut(Key.F1, "Help", () => ShowAboutDialog()),
                new Shortcut(Key.F3, "Search Recipes", () => SearchRecipesDialog()),
                new Shortcut(Key.F10, "Quit", () => Application.RequestStop()),
            });

            window.Add(statusBar);

            return window;
        }

        private static void ShowAboutDialog()
        {
            MessageBox.Query("About",
                "Recipe Vector Search TUI\n\n" +
                "A Terminal.GUI application for searching recipes\n" +
                "using vector similarity with BERT embeddings.\n\n" +
                "Features:\n" +
                "• PostgreSQL with pgvector extension\n" +
                "• ONNX runtime for embeddings\n" +
                "• Interactive TUI interface",
                "OK");
        }

        private static void ConnectionStringDialog()
        {
            var dialog = new Dialog()
            {
                Title = "Database Connection",
                Width = 60,
                Height = 8
            };
            
            var connLabel = new Label()
            {
                Text = "Connection String:",
                X = 1,
                Y = 1
            };
            
            var connField = new TextField()
            {
                Text = connectionString,
                X = 1,
                Y = 2,
                Width = Dim.Fill() - 2
            };

            var saveButton = new Button()
            {
                Text = "Save",
                X = 1,
                Y = 4
            };
            
            var cancelButton = new Button()
            {
                Text = "Cancel",
                X = Pos.Right(saveButton) + 3,
                Y = 4
            };

            saveButton.Accepting += (s,e) => {
                connectionString = connField.Text.ToString();
                try
                {
                    // Recreate data source with new connection string
                    dataSource?.Dispose();
                    var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
                    dataSourceBuilder.UseVector();
                    dataSource = dataSourceBuilder.Build();
                    
                    MessageBox.Query("Success", "Connection string updated!", "OK");
                    dialog.RequestStop();
                }
                catch (Exception ex)
                {
                    MessageBox.ErrorQuery("Error", $"Invalid connection string: {ex.Message}", "OK");
                }
            };

            cancelButton.Accepting += (s,e) => dialog.RequestStop();

            dialog.Add(connLabel, connField, saveButton, cancelButton);
            Application.Run(dialog);
        }


        private static void SearchRecipesDialog()
        {
            if (onnxSession == null || tokenizer == null)
            {
                MessageBox.ErrorQuery("Error", "ONNX model not loaded. Please ensure model.onnx and vocab.txt files exist.", "OK");
                return;
            }

            var dialog = new Dialog()
            {
                Title = "Search Recipes",
                Width = Dim.Fill() - 2,
                Height = Dim.Fill() - 2,
            };
            
            var ingredientsLabel = new Label()
            {
                Text = "Enter ingredients (comma-separated):",
                X = 1,
                Y = 1
            };
            
            var ingredientsField = new TextField()
            {
                X = 1,
                Y = 2,
                Width = Dim.Fill() - 2
            };

            var searchButton = new Button()
            {
                Text = "Search",
                X = 1,
                Y = 4
            };

            ingredientsField.KeyDownNotHandled += (s, key) =>
            {
                if (key == Key.Enter)
                {
                    searchButton.InvokeCommand(Command.Accept);
                }
            };            
            
            var cancelButton = new Button()
            {
                Text = "Cancel",
                X = Pos.Right(searchButton) + 3,
                Y = 4
            };

            bool isWideScreen = Application.Driver.Cols > Application.Driver.Rows * 1.8;
            var resultsFrame = new FrameView()
            {
                Text = "Search Results",
                X = 1,
                Y = 6,
                Width = isWideScreen ? Dim.Percent(50) : Dim.Fill() - 1,
                Height = isWideScreen ? Dim.Fill() : Dim.Percent(50) - 3
            };

            var directionsFrame = new FrameView()
            {
                Text = "Recipe Directions",
                X = isWideScreen ? Pos.Right(resultsFrame) : 1,
                Y = isWideScreen ? 6 : Pos.Bottom(resultsFrame),
                Width = isWideScreen ? Dim.Percent(50) - 2 : Dim.Fill() - 1,
                Height = isWideScreen ? Dim.Fill() : Dim.Percent(50) - 3
            };
            
            var resultsView = new ListView()
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill()
            };

            Label directionsLabel = new Label()
            {
                Text = "Directions will be shown here",
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
            };

            searchButton.Accepting += (s,e) => {
                var ingredientsText = ingredientsField.Text.ToString();
                if (string.IsNullOrWhiteSpace(ingredientsText))
                {
                    MessageBox.ErrorQuery("Error", "Please enter some ingredients!", "OK");
                    return;
                }

                try
                {
                    string[] queryIngredients = ingredientsText.Split(',')
                        .Select(s => s.Trim())
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .ToArray();

                    List<RecipeResult> similarRecipes = SearchSimilarRecipes(queryIngredients, dataSource.CreateConnection(), 20);
                    var resultsList = new ObservableCollection<RecipeResult>(similarRecipes);

                    resultsView.SetSource(resultsList);
                    resultsView.SelectedItem = 0; // Clear selection
                    resultsView.SetFocus();

                }
                catch (Exception ex)
                {
                    MessageBox.ErrorQuery("Error", $"Search failed: {ex.Message}", "OK");
                }
            };

            resultsView.SelectedItemChanged += (s, e) =>
            {
                resultsView.InvokeCommand(Command.Accept);
            };

            resultsView.OpenSelectedItem += (s, e) =>
            {
                if (e.Value is RecipeResult recipeResult)
                {
                    directionsLabel.Text = recipeResult.Recipe;
                }
            };

            cancelButton.Accepting += (s,e) => dialog.RequestStop();

            resultsFrame.Add(resultsView);
            directionsFrame.Add(directionsLabel);
            dialog.Add(ingredientsLabel, ingredientsField, searchButton, cancelButton, resultsFrame, directionsFrame);
            Application.Run(dialog);
        }

        private static void LoadRecipesDialog()
        {
            var dialog = new Dialog()
            {
                Title = "Load Recipes",
                Width = 60,
                Height = 12
            };
            
            var csvPathLabel = new Label()
            {
                Title = "CSV File Path:",
                X = 1,
                Y = 1
            };
            
            var csvPathField = new TextField()
            {
                Text = "recipes.csv",
                X = 1,
                Y = 2,
                Width = Dim.Fill() - 12
            };
            
            var browseButton = new Button()
            {
                Text = "Browse",
                X = Pos.Right(csvPathField) + 1,
                Y = 2,
                Width = 10
            };
            
            browseButton.Accepting += (s,e) => {
                var openDialog = new OpenDialog()
                {
                    Title = "Select CSV File",
                };
                openDialog.AllowedTypes = new() { new AllowedType("Recipe CSV File", ".csv") };
                Application.Run(openDialog);
                
                if (!openDialog.Canceled && !string.IsNullOrEmpty(openDialog.Path))
                {
                    csvPathField.Text = openDialog.Path;
                }
            };

            var progressLabel = new Label()
            {
                Text = "Loading recipes, please wait...",
                X = 1,
                Y = 3,
                Visible = false,
            };

            var progressBar = new ProgressBar()
            {
                X = 1,
                Y = 4,
                Width = Dim.Fill() - 2,
                ProgressBarStyle = ProgressBarStyle.MarqueeBlocks
            };

            var loadButton = new Button
            {
                Text = "Load",
                X = 1,
                Y = 6
            };
            
            var cancelButton = new Button
            {
                Text = "Cancel",
                X = Pos.Right(loadButton) + 3,
                Y = 6
            };

            loadButton.Accepting += async (s,e) => {
                loadButton.Enabled = false;
                cancelButton.Enabled = false;
                progressLabel.Visible = true;

                var csvPath = csvPathField.Text.ToString();
                if (!File.Exists(csvPath))
                {
                    MessageBox.ErrorQuery("Error", "CSV file not found!", "OK");
                    return;
                }

                try
                {
                    Stopwatch stopwatch = Stopwatch.StartNew();
                    var recipes = LoadRecipesFromCsv(csvPath);
                    long count = await StoreRecipesInDb(recipes, dataSource, progress: (currentCount) =>
                    {
                        Application.Invoke(() =>
                        {
                            progressBar.Pulse();
                            progressLabel.Text = $"Loaded {currentCount} recipes in {stopwatch.Elapsed.TotalSeconds:F0} seconds";
                        });
                    });

                    MessageBox.Query("Success", $"{count} recipes loaded successfully in {stopwatch.Elapsed.TotalSeconds:F2} seconds.", "OK");
                    dialog.RequestStop();
                }
                catch (Exception ex)
                {
                    MessageBox.ErrorQuery("Error", $"Failed to load recipes: {ex.Message}", "OK");
                }
                finally
                {
                    loadButton.Enabled = true;
                    cancelButton.Enabled = true;
                    progressLabel.Visible = false;
                    progressBar.Fraction = 0;
                }
            };

            cancelButton.Accepting += (s,e) => dialog.RequestStop();

            dialog.Add(csvPathLabel, csvPathField, browseButton, progressLabel, progressBar, loadButton, cancelButton);
            Application.Run(dialog);
        }
        private static void DropDatabaseDialog()
        {
            var result = MessageBox.Query("Drop Database", 
                "This will permanently delete all recipe data.\nAre you sure?", 
                "Yes", "No");
            
            if (result == 0)
            {
                try
                {
                    DropDatabase(connectionString);
                    MessageBox.Query("Success", "Database dropped successfully!", "OK");
                }
                catch (Exception ex)
                {
                    MessageBox.ErrorQuery("Error", $"Failed to drop database: {ex.Message}", "OK");
                }
            }
        }

        private static void ResetDatabaseDialog()
        {
            var result = MessageBox.Query("Reset Database", 
                "This will DROP all data and recreate the database.\nAre you sure?", 
                "Yes", "No");
            
            if (result == 0)
            {
                try
                {
                    DropDatabase(connectionString);
                    InitDatabase(connectionString);
                    MessageBox.Query("Success", "Database reset successfully!", "OK");
                }
                catch (Exception ex)
                {
                    MessageBox.ErrorQuery("Error", $"Failed to reset database: {ex.Message}", "OK");
                }
            }
        }

        private static void InitializeDatabaseDialog()
        {
            var result = MessageBox.Query("Initialize Database", 
                "This will create the recipes table and vector extension.\nContinue?", 
                "Yes", "No");
            
            if (result == 0)
            {
                try
                {
                    InitDatabase(connectionString);
                    MessageBox.Query("Success", "Database initialized successfully!", "OK");
                }
                catch (Exception ex)
                {
                    MessageBox.ErrorQuery("Error", $"Failed to initialize database: {ex.Message}", "OK");
                }
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

    internal record struct RecipeResult(string Title, float Similarity, string Recipe)
    {
        public override string ToString() => $"{Title} (Similarity: {Similarity:F3})";
    }
}