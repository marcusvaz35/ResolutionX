namespace ResolutionX.Core.Models;

/// <summary>Informações de identificação e capacidade lidas do EDID de um monitor.</summary>
public sealed class EdidInfo
{
    /// <summary>Código PnP de três letras do fabricante (ex.: "SAM").</summary>
    public required string ManufacturerId { get; init; }

    /// <summary>Código do produto em hexadecimal.</summary>
    public required string ProductCode { get; init; }

    public string? MonitorName { get; init; }

    /// <summary>Número de série em texto quando o monitor informa; senão o numérico, se não for zero.</summary>
    public string? SerialNumber { get; init; }

    public int? ManufactureYear { get; init; }

    public required string Version { get; init; }

    /// <summary>Resolução nativa: o primeiro "Detailed Timing" do EDID.</summary>
    public DisplayMode? NativeMode { get; init; }

    public int? MaxRefreshRate { get; init; }

    public required IReadOnlyList<VideoTiming> DetailedTimings { get; init; }

    public int ExtensionCount { get; init; }

    /// <summary>Identificação curta do monitor, ex.: "SAM0F99".</summary>
    public string MonitorId => ManufacturerId + ProductCode;
}
