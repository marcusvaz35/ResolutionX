namespace ResolutionX.Core.Models;

/// <summary>Uma resolução salva. As embutidas (720p, 1080p...) não podem ser excluídas.</summary>
public sealed record ResolutionPreset(string Name, int Width, int Height, int RefreshRate, bool IsBuiltIn = false)
{
    public DisplayMode ToMode() => new(Width, Height, RefreshRate);

    public string ModeText => ToMode().ToString();
}
