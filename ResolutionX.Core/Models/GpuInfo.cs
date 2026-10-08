namespace ResolutionX.Core.Models;

public enum GpuVendor
{
    Unknown,
    Intel,
    Amd,
    Nvidia,
    Qualcomm,
    /// <summary>Microsoft Basic Display Adapter: driver genérico, sem recursos do fabricante.</summary>
    MicrosoftBasic,
    /// <summary>Outros adaptadores da Microsoft (Hyper-V, Remote Desktop, etc.).</summary>
    Microsoft,
    Other
}

/// <summary>Adaptador de vídeo e driver responsáveis por um monitor.</summary>
public sealed record GpuInfo(
    string Name,
    GpuVendor Vendor,
    string? DriverProvider,
    string? DriverVersion,
    string? DriverDate,
    string DeviceId)
{
    public string VendorName => Vendor switch
    {
        GpuVendor.Intel => "Intel",
        GpuVendor.Amd => "AMD",
        GpuVendor.Nvidia => "NVIDIA",
        GpuVendor.Qualcomm => "Qualcomm",
        GpuVendor.MicrosoftBasic => "Microsoft Basic Display Adapter",
        GpuVendor.Microsoft => "Microsoft",
        GpuVendor.Other => "Outro",
        _ => "Desconhecido"
    };

    /// <summary>O driver básico da Microsoft não aceita modos além dos que o firmware expõe.</summary>
    public bool IsBasicDriver => Vendor == GpuVendor.MicrosoftBasic;
}
