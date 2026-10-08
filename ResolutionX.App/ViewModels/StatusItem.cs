namespace ResolutionX.App.ViewModels;

/// <summary>Uma linha do painel STATUS. Level é "Ok", "Warning" ou "Error".</summary>
public sealed record StatusItem(string Level, string Text)
{
    public string Glyph => Level switch
    {
        "Ok" => "✓",
        "Warning" => "!",
        _ => "✕"
    };

    public static StatusItem Ok(string text) => new("Ok", text);
    public static StatusItem Warning(string text) => new("Warning", text);
    public static StatusItem Error(string text) => new("Error", text);
}
