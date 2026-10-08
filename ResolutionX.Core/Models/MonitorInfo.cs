namespace ResolutionX.Core.Models;

/// <summary>Um monitor ativo na área de trabalho do Windows, com tudo que o sistema informa sobre ele.</summary>
public sealed class MonitorInfo
{
    /// <summary>Nome GDI usado pelas APIs do Windows, por exemplo <c>\\.\DISPLAY1</c>.</summary>
    public required string DeviceName { get; init; }

    /// <summary>Número do monitor, o mesmo mostrado nas Configurações do Windows.</summary>
    public required int Index { get; init; }

    /// <summary>Nome amigável (vem do EDID quando disponível, senão do driver do monitor).</summary>
    public required string FriendlyName { get; init; }

    /// <summary>Código PnP de três letras do fabricante (ex.: "SAM", "LGD"), quando disponível.</summary>
    public string? ManufacturerId { get; init; }

    /// <summary>Código de produto do EDID em hexadecimal, quando disponível.</summary>
    public string? ProductCode { get; init; }

    /// <summary>Tipo de conexão: HDMI, DisplayPort, tela interna, etc.</summary>
    public required string Connection { get; init; }

    public bool IsPrimary { get; init; }

    public required DisplayMode CurrentMode { get; init; }

    /// <summary>Maior resolução entre os modos que o driver expõe para este monitor.</summary>
    public required DisplayMode MaxMode { get; init; }

    public required IReadOnlyList<DisplayMode> SupportedModes { get; init; }

    public required string Orientation { get; init; }

    /// <summary>Posição do canto superior esquerdo na área de trabalho virtual, em pixels.</summary>
    public int PositionX { get; init; }
    public int PositionY { get; init; }

    public int? Dpi { get; init; }

    /// <summary>Escala do Windows em porcentagem (100, 125, 150...).</summary>
    public int? ScalePercent { get; init; }

    public required GpuInfo Gpu { get; init; }

    public string Title => $"Monitor {Index} — {FriendlyName}";

    public string ResolutionText => $"{CurrentMode.Width} × {CurrentMode.Height}";

    public string Summary
    {
        get
        {
            var parts = new List<string> { $"{CurrentMode.RefreshRate} Hz", Connection };
            if (ScalePercent is int scale) parts.Add($"Escala {scale}%");
            if (IsPrimary) parts.Add("Principal");
            return string.Join("  ·  ", parts);
        }
    }

    public bool Supports(DisplayMode mode) => SupportedModes.Contains(mode);
}
