using ResolutionX.Core.Models;

namespace ResolutionX.Core.Interfaces;

public interface IEdidService
{
    /// <summary>EDID original informado pelo monitor, ou null se o Windows não o tiver.</summary>
    byte[]? ReadEdid(MonitorInfo monitor);

    /// <summary>EDID substituto ativo para o monitor (EDID_OVERRIDE), ou null se não houver.</summary>
    byte[]? ReadOverride(MonitorInfo monitor);

    /// <summary>Fabricante, modelo, série, resolução nativa e taxa máxima lidos do EDID original.</summary>
    EdidInfo? GetInfo(MonitorInfo monitor);
}
