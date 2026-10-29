namespace UrHmi.Domain.Robot;

public sealed record RtdeRecipe(
    string RecipeId,
    IReadOnlyList<string> OutputKeys);
