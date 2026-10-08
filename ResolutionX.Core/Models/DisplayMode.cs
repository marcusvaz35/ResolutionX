namespace ResolutionX.Core.Models;

/// <summary>Um modo de vídeo: largura e altura em pixels e taxa de atualização em Hz.</summary>
public sealed record DisplayMode(int Width, int Height, int RefreshRate)
{
    public override string ToString() => $"{Width} × {Height} @ {RefreshRate} Hz";
}
