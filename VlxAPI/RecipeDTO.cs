﻿namespace VlxAPI;

public class RecipeDTO
{
    public long Id { get; set; }
    public string? Title { get; set; }
    public string? IngredientsJson { get; set; }
    public string? DirectionsJson { get; set; }
    public string? NerJson { get; set; }
    public string? Link { get; set; }
    public string? Source { get; set; }
    public float[]? IngredientEmbedding { get; set; }
}

public class RecipeResultDTO
{
    public float Similarity { get; set; }
    public RecipeDTO? Recipe { get; set; }
}