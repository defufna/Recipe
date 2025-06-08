using RecipeVectorSearch.Data;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Terminal.Gui;

namespace RecipeVectorSearch.UI
{
    /// <summary>
    /// Manages the Terminal User Interface for the Recipe Vector Search application.
    /// </summary>
    internal class RecipeTUI
    {
        private readonly DatabaseService dbService;

        public RecipeTUI(DatabaseService dbService)
        {
            this.dbService = dbService ?? throw new ArgumentNullException(nameof(dbService));
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
                    new MenuBarItem("Se_ttings", new MenuItem[] {
                        new MenuItem("_Connection String", "", ConnectionStringDialog),
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
                Height = Dim.Fill() - 4
            };

#if DEBUG
            welcomeText.Text += $"\n\nDebug Mode: PID = {Environment.ProcessId}";
#endif
            mainFrame.Add(welcomeText);
            window.Add(mainFrame);

            window.Add(menuBar);
            
            var statusBar = new StatusBar(new Shortcut[] {
                new Shortcut(Key.F1, "Help", ShowAboutDialog),
                new Shortcut(Key.F3, "Search Recipes", SearchRecipesDialog),
                new Shortcut(Key.F10, "Quit", () => Application.RequestStop()),
            });
            window.Add(statusBar);
            
            return window;
        }

        #region Action Handlers & Dialogs

        private void HandleAction(Action action, string successMessage)
        {
            try
            {
                action();
                MessageBox.Query("Success", successMessage, "OK");
            }
            catch (Exception ex)
            {
                MessageBox.ErrorQuery("Error", $"Operation failed: {ex.Message}", "OK");
            }
        }

        private void HandleActionWithConfirm(string confirmMessage, Action action, string successMessage)
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

        private void ConnectionStringDialog()
        {
            var dialog = new Dialog()
            {
                Title = "Database Connection",
                Width = 60,
                Height = 8
            };

            var connField = new TextField() 
            { 
                Text = dbService.GetConnectionString(),
                X = 1, 
                Y = 2, 
                Width = Dim.Fill() - 2 
            };
            
            var saveButton = new Button() 
            { 
                Text = "Save",
                X = 1, 
                Y = 4,
                IsDefault = true
            };
            
            var cancelButton = new Button() 
            { 
                Text = "Cancel",
                X = Pos.Right(saveButton) + 3, 
                Y = 4 
            };

            saveButton.Accepting += (s, e) => {
                try
                {
                    dbService.UpdateConnectionString(connField.Text.ToString());
                    MessageBox.Query("Success", "Connection string updated!", "OK");
                    Application.RequestStop(dialog);
                }
                catch (Exception ex)
                {
                    MessageBox.ErrorQuery("Error", $"Invalid connection string: {ex.Message}", "OK");
                }
            };
            cancelButton.Accepting += (s, e) => Application.RequestStop(dialog);
            
            dialog.Add(
                new Label()
                {
                    Text = "Connection String:",
                    X = 1,
                    Y = 1
                },
                connField,
                saveButton,
                cancelButton);
                
            Application.Run(dialog);
        }

        private void SearchRecipesDialog()
        {
            var dialog = new Dialog{Title = "Search Recipes", Width = Dim.Fill() - 2, Height = Dim.Fill() - 2};
            var ingredientsLabel = new Label{Text = "Enter ingredients (comma-separated):", X = 1, Y = 1};
            var ingredientsField = new TextField{X = 1, Y = 2, Width = Dim.Fill() - 2};

            var searchButton = new Button{Text = "Search",X = 1,Y = 4};

            ingredientsField.KeyDownNotHandled += (s, key) =>
            {
                if (key == Key.Enter)
                {
                    searchButton.InvokeCommand(Command.Accept);
                }
            };            
            
            var cancelButton = new Button{Text = "Cancel",X = Pos.Right(searchButton) + 3,Y = 4};

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
            
            var resultsView = new ListView{X = 0,Y = 0,Width = Dim.Fill(),Height = Dim.Fill()};

            Label directionsLabel = new Label(){Text = "Directions will be shown here",X = 0,Width = Dim.Fill(),Height = Dim.Fill()};

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

                    List<RecipeResult> similarRecipes = dbService.SearchSimilarRecipes(queryIngredients, 20);
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
                    long count = await dbService.StoreRecipes(recipes, progress: (currentCount) =>
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

        #endregion
    }
}