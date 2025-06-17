using RecipeVectorSearch.Data;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using Terminal.Gui;

namespace RecipeVectorSearch.UI
{
    /// <summary>
    /// Manages the Terminal User Interface for the Recipe Vector Search application.
    /// </summary>
    internal class RecipeTUI
    {
        private readonly DatabaseServiceCollection dbsCollection;
        private IDatabaseService dbService;
        private readonly EmbeddingService embeddingService;

        public RecipeTUI(DatabaseServiceCollection dbsCollection, EmbeddingService embeddingService)
        {
            this.dbsCollection = dbsCollection ?? throw new ArgumentNullException(nameof(dbsCollection));
            this.embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));

            dbService = dbsCollection.Default;
        }

        /// <summary>
        /// Initializes and runs the main application loop.
        /// </summary>
        public void Run()
        {
            Application.Init();

            var mainWindow = CreateMainWindow();
            Application.Run(mainWindow);
            Application.Shutdown();
        }

        private Window CreateMainWindow()
        {
            //File.AppendAllText("debug.txt", string.Join(',', ThemeManager.Themes.Keys)+ Environment.NewLine);
            Label dbLabel = null;
            Shortcut dbStatus = null;
            
            var window = new Window()
            {
                Title = "Recipe Vector Search",
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill()
            };

            var menuBar = new MenuBar()
            {
                Menus = new MenuBarItem[] {
                    new MenuBarItem("_Database", new MenuItem[] {
                        new MenuItem("_Initialize", "", () => HandleAction(dbService.Initialize, "Database initialized successfully!")),
                        new MenuItem("_Reset", "", () => HandleActionWithConfirm("This will DROP all data and recreate the database.\nAre you sure?", dbService.Reset, "Database reset successfully!")),
                        new MenuItem("_Drop", "", () => HandleActionWithConfirm("This will permanently delete all recipe data.\nAre you sure?", dbService.Drop, "Database dropped successfully!")),
                        null, // Separator
                        new MenuItem("_Load Recipes", "", LoadRecipesDialog),
                    }),
                    new MenuBarItem("_Search", new MenuItem[] {
                        new MenuItem("_Search Recipes", "", SearchRecipesDialog),
                    }),
                    new MenuBarItem("_Benchmark", new MenuItem[] {
                        new MenuItem("Embedding _Benchmark", "", EmbeddingBenchmarkDialog),
                    }),
                    new MenuBarItem("Se_ttings", new MenuItem[] {
                        new MenuItem("_Database", "", ShowDBSettingsDialog),
                    }),
                    new MenuBarItem("_Help", new MenuItem[] {
                        new MenuItem("_About", "", ShowAboutDialog),
                    })
                }
            };

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
                Height = Dim.Auto()
            };

            var connectedLabel = new Label()
            {
                Text = "Connected to: ",
                X = Pos.Left(welcomeText),
                Y = Pos.Bottom(welcomeText),
            };

            dbLabel = new Label()
            {
                Text = dbService.Name,
                X = Pos.Right(connectedLabel),
                Y = Pos.Top(connectedLabel)
            };

#if DEBUG
            welcomeText.Text += $"\n\nDebug Mode: PID = {Environment.ProcessId}";
#endif
            mainFrame.Add(welcomeText);
            mainFrame.Add(connectedLabel);
            mainFrame.Add(dbLabel);
            window.Add(mainFrame);

            window.Add(menuBar);

            dbStatus = new Shortcut() { Title = dbService.Name, Action = ShowDBSettingsDialog};
            var statusBar = new StatusBar(new Shortcut[] {
                new Shortcut(Key.F1, "Help", ShowAboutDialog),
                new Shortcut(Key.F3, "Search Recipes", SearchRecipesDialog),
                new Shortcut(Key.F10, "Quit", () => Application.RequestStop()),
                dbStatus
            });

            statusBar.AlignmentModes = AlignmentModes.IgnoreFirstOrLast;
            window.Add(statusBar);

            return window;

            void UpdateLabel()
            {
                dbLabel.Text = dbService.Name;
                dbStatus.Title = dbService.Name;
            }

            void ShowDBSettingsDialog()
            {
                DBSettingsDialog(UpdateLabel);
            }
        }

        private IEnumerable<int> GetParallelismOptions()
        {
            int i = 1;
            while (true)
            {
                yield return i;
                i *= 2;
            }
        }

        private void EmbeddingBenchmarkDialog()
        {
            var dialog = new Dialog()
            {
                Title = "Embedding Benchmark",
                Width = Dim.Percent(80), // Use percentage for better responsiveness
                Height = Dim.Percent(80)
            };

            Pos parallelismLabelY = 1;

#if OPENVINO

            // 1. Dropdown for CPU/GPU/NPU selection
            var hardwareLabel = new Label
            {
                Text = "Hardware:",
                X = 1,
                Y = 1
            };

            dialog.Add(hardwareLabel);

            var hardwareRadioGroup = new RadioGroup()
            {
                X = Pos.Right(hardwareLabel) + 2,
                Y = Pos.Top(hardwareLabel),
                Width = Dim.Fill(),
                Height = 1,
                SelectedItem = 0, // Default to CPU
                RadioLabels = ["CPU", "GPU", "NPU"],
                Orientation = Orientation.Horizontal
            };

            dialog.Add(hardwareRadioGroup);
            parallelismLabelY = Pos.Bottom(hardwareRadioGroup) + 1;
#endif

            // Add paralelism slider
            var parallelismLabel = new Label
            {
                Text = "Parallelism:",
                X = 1,
                Y = parallelismLabelY
            };
            dialog.Add(parallelismLabel);
            var parallelismSlider = new Slider<int>([.. GetParallelismOptions().Take(8)])
            {
                X = Pos.Right(parallelismLabel) + 2,
                Y = Pos.Top(parallelismLabel),
                Width = Dim.Fill() - 4,
                Orientation = Orientation.Horizontal,
                Type = SliderType.Single,
                Title = "Parallelism Level"
            };
            dialog.Add(parallelismSlider);

            // 2. GraphView for displaying benchmark results
            var graphView = new GraphView()
            {
                X = 1,
                Y = Pos.Bottom(parallelismSlider) + 1,
                Width = Dim.Fill() - 2,
                Height = Dim.Fill() - 5, // Make space for the button and status
            };

            var stiple = new GraphCellToRender(Glyphs.Stipple);

            BarSeries series = new()
            {
                Orientation = Orientation.Vertical,
                Bars = new()
                {
                   new(" ", stiple, 100)
                }
            };

            graphView.Series.Add(series);
            dialog.Add(graphView);

            // Status Label
            var statusLabel = new Label
            {
                X = 1,
                Y = Pos.Bottom(graphView) + 1,
                Width = Dim.Fill() - 2
            };
            dialog.Add(statusLabel);

            // 3. Run/Stop Button
            var runButton = new Button
            {
                Text = "Run",
                X = Pos.Center(),
                Y = Pos.Bottom(statusLabel) + 1,
                IsDefault = true // Make it the default focused button
            };
            dialog.Add(runButton);

            Benchmark benchmark = null;

            runButton.Accepting += (s, e) =>
            {
                if (runButton.Text == "Stop")
                {
                    Debug.Assert(benchmark != null, "Benchmark should not be null when stopping");
                    benchmark.Stop();
                    benchmark = null;
                    runButton.Text = "Run";
                    statusLabel.Text = "Benchmark stopped.";
                    return;
                }

                runButton.Text = "Stop";
                statusLabel.Text = "Running benchmark...";
                var random = new Random();
                float max = 0;

#if OPENVINO
                ExecutionProvider provider = hardwareRadioGroup.SelectedItem switch
                {
                    0 => ExecutionProvider.CPU,
                    1 => ExecutionProvider.GPU,
                    2 => ExecutionProvider.NPU,
                    _ => throw new InvalidOperationException("Invalid hardware selection")
                };
#else
                ExecutionProvider provider = ExecutionProvider.CPU; // Default to CPU if not using OpenVINO
#endif

                benchmark = Benchmark.Run(provider, (count) =>
                {
                    Application.Invoke(() =>
                    {
                        max = Math.Max(max, count);
                        statusLabel.Text = $"Embeddings/second: {count:F2}";
                        graphView.CellSize = new(1, Math.Max(1, max / (graphView.GetContentSize().Height - 2)));
                        series.Bars.Add(new(null, stiple, count));
                        graphView.SetNeedsDraw();
                    });
                }, parallelismSlider.Options[parallelismSlider.GetSetOptions().FirstOrDefault(1)].Data);
            };

            dialog.Closing += (s, e) =>
            {
                if (benchmark != null)
                {
                    benchmark.Stop();
                    benchmark = null;
                }
            };

            Application.Run(dialog);

        }

        #region Action Handlers & Dialogs

        private async void HandleAction(Func<Task> action, string successMessage)
        {
            try
            {
                await action();
                MessageBox.Query("Success", successMessage, "OK");
            }
            catch (Exception ex)
            {
                MessageBox.ErrorQuery("Error", $"Operation failed: {ex.Message}", "OK");
            }
        }

        private void HandleActionWithConfirm(string confirmMessage, Func<Task> action, string successMessage)
        {
            var result = MessageBox.Query("Confirm", confirmMessage, "Yes", "No");
            if (result == 0)
            {
                HandleAction(action, successMessage);
            }
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

        private void DBSettingsDialog(Action onChanged)
        {
            var dialog = new Dialog()
            {
                Title = "Database Settings",
                Width = 60,
                Height = 10
            };

            var dbLabel = new Label
            {
                Text = "Select Database:",
                X = 1,
                Y = 1,
            };

            var dbGroup = new RadioGroup()
            {
                X = Pos.Right(dbLabel) + 2,
                Y = 1,
                Width = Dim.Fill(),
                Height = 1,
                RadioLabels = dbsCollection.Databases.Select(dbs => dbs.Name).ToArray(),
                SelectedItem = dbsCollection.Index(dbService),
                Orientation = Orientation.Horizontal
            };

            var connField = new TextField()
            {
                Text = dbService.GetConnectionString(),
                X = 1,
                Y = Pos.Bottom(dbGroup)+1,
                Width = Dim.Fill() - 2
            };

            var saveButton = new Button()
            {
                Text = "Save",
                X = 1,
                Y = Pos.Bottom(connField) + 1,
                IsDefault = true
            };

            var cancelButton = new Button()
            {
                Text = "Cancel",
                X = Pos.Right(saveButton) + 3,
                Y = Pos.Top(saveButton)
            };

            saveButton.Accepting += (s, e) =>
            {
                try
                {
                    dbService = dbsCollection.Databases[dbGroup.SelectedItem];
                    dbService.UpdateConnectionString(connField.Text.ToString());
                    MessageBox.Query("Success", "Database settings updated!", "OK");
                    Application.RequestStop(dialog);
                    onChanged();
                }
                catch (Exception ex)
                {
                    MessageBox.ErrorQuery("Error", $"Invalid connection string: {ex.Message}", "OK");
                }
            };
            cancelButton.Accepting += (s, e) =>
            {
                e.Cancel = true;
                dialog.RequestStop();
            };

            dbGroup.SelectedItemChanged += (s, e) =>
            {
                connField.Text = dbsCollection.Databases[e.SelectedItem].GetConnectionString();                
            };

            dialog.Add(
                dbLabel,
                dbGroup,
                new Label()
                {
                    Text = "Connection String:",
                    X = 1,
                    Y = Pos.Bottom(dbGroup),
                },
                connField,
                saveButton,
                cancelButton);

            Application.Run(dialog);
        }

        private void SearchRecipesDialog()
        {
            var dialog = new Dialog { Title = "Search Recipes", Width = Dim.Fill() - 2, Height = Dim.Fill() - 2 };
            var ingredientsLabel = new Label { Text = "Enter ingredients (comma-separated):", X = 1, Y = 1 };
            var ingredientsField = new TextField { X = 1, Y = 2, Width = Dim.Fill() - 2 };

            var searchButton = new Button { Text = "Search", X = 1, Y = 4 };

            ingredientsField.KeyDownNotHandled += (s, key) =>
            {
                if (key == Key.Enter)
                {
                    searchButton.InvokeCommand(Command.Accept);
                }
            };

            var cancelButton = new Button { Text = "Cancel", X = Pos.Right(searchButton) + 3, Y = 4 };

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

            var resultsView = new ListView { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };

            Label directionsLabel = new Label() { Text = "Directions will be shown here", X = 0, Width = Dim.Fill(), Height = Dim.Fill() };

            searchButton.Accepting += async (s, e) =>
            {
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

                    List<RecipeResult> similarRecipes = await SearchSimilarRecipes(queryIngredients, 20);
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

            cancelButton.Accepting += (s, e) => dialog.RequestStop();

            resultsFrame.Add(resultsView);
            directionsFrame.Add(directionsLabel);
            dialog.Add(ingredientsLabel, ingredientsField, searchButton, cancelButton, resultsFrame, directionsFrame);
            Application.Run(dialog);
        }

        private async Task<List<RecipeResult>> SearchSimilarRecipes(string[] queryIngredients, int limit = 20)
        {
            if (!embeddingService.TryGenerateEmbedding(queryIngredients, out var queryEmbeddingTensor) || queryEmbeddingTensor == null)
            {
                throw new ArgumentException("Failed to generate embedding for query ingredients.");
            }

            return await dbService.SearchSimilarRecipes(queryEmbeddingTensor.ToArray(), limit);
        }

        private void LoadRecipesDialog()
        {
            CancellationTokenSource cts = new CancellationTokenSource();

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

            browseButton.Accepting += (s, e) =>
            {
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

            loadButton.Accepting += async (s, e) =>
            {
                loadButton.Enabled = false;
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
                    var recipes = RecipeCsvReader.LoadRecipes(csvPath);
                    long count = await StoreRecipes(recipes, progress: (currentCount) =>
                    {
                        Application.Invoke(() =>
                        {
                            progressBar.Pulse();
                            progressLabel.Text = $"Loaded {currentCount} recipes in {stopwatch.Elapsed.TotalSeconds:F0} seconds";
                        });
                    }, cts.Token);

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
                    progressLabel.Visible = false;
                    progressBar.Fraction = 0;
                }
            };

            cancelButton.Accepting += (s, e) =>
            {
                cts.Cancel();
                dialog.RequestStop();
            };

            dialog.Add(csvPathLabel, csvPathField, browseButton, progressLabel, progressBar, loadButton, cancelButton);
            Application.Run(dialog);
        }
        
        private async Task<long> StoreRecipes(IEnumerable<Recipe> recipes, Action<long> progress, CancellationToken token)
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

            try
            {

                await Parallel.ForEachAsync(recipes, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = token }, async (recipe, token) =>
                {
                    var nerIngredients = JsonSerializer.Deserialize<string[]>(recipe.NER);
                    if (nerIngredients == null || nerIngredients.Length == 0) return;

                    if (!embeddingService.TryGenerateEmbedding(nerIngredients, out var embeddingTensor) || embeddingTensor == null)
                    {
                        return; // Skip if embedding fails
                    }

                    await dbService.StoreRecipe(recipe, embeddingTensor, token);
                    Interlocked.Increment(ref count);
                });
            }
            finally
            {
                cts.Cancel();
                await progressUpdater;
            }

            return count;
        }


        #endregion
    }
}