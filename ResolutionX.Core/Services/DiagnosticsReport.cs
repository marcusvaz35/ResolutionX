using System.Text;
using ResolutionX.Core.Models;

namespace ResolutionX.Core.Services;

/// <summary>Monta o texto da tela "Diagnóstico" a partir do que foi lido do sistema.</summary>
public static class DiagnosticsReport
{
    public static string Build(
        IReadOnlyList<MonitorInfo> monitors,
        VirtualDisplayStatus virtualStatus,
        Func<MonitorInfo, EdidInfo?> readEdid,
        Func<MonitorInfo, IReadOnlyList<DisplayMode>> readCustomModes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ResolutionX — Diagnóstico");
        sb.AppendLine($"Gerado em: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Sistema: {Environment.OSVersion.VersionString}");
        sb.AppendLine($"Monitores ativos: {monitors.Count}");

        foreach (var m in monitors)
        {
            sb.AppendLine();
            sb.AppendLine($"=== Monitor {m.Index}{(m.IsPrimary ? " (principal)" : "")} ===");
            sb.AppendLine($"GPU: {m.Gpu.Name}");
            sb.AppendLine($"Fabricante da GPU: {m.Gpu.VendorName}");
            sb.AppendLine($"Driver: {m.Gpu.DriverVersion ?? "não informado"}");
            sb.AppendLine($"Fornecedor do driver: {m.Gpu.DriverProvider ?? "não informado"}");
            sb.AppendLine($"Data do driver: {m.Gpu.DriverDate ?? "não informada"}");
            sb.AppendLine($"ID da GPU: {m.Gpu.DeviceId}");
            sb.AppendLine($"Monitor: {m.FriendlyName}");
            sb.AppendLine($"Fabricante do monitor: {m.ManufacturerId ?? "não informado"}");
            sb.AppendLine($"Código do produto: {m.ProductCode ?? "não informado"}");
            sb.AppendLine($"Conexão: {m.Connection}");
            sb.AppendLine($"Dispositivo: {m.DeviceName}");
            sb.AppendLine($"Resolução atual: {m.CurrentMode.Width} × {m.CurrentMode.Height}");
            sb.AppendLine($"Refresh: {m.CurrentMode.RefreshRate} Hz");
            sb.AppendLine($"Resolução máxima: {m.MaxMode}");
            sb.AppendLine($"Orientação: {m.Orientation}");
            sb.AppendLine($"DPI: {(m.Dpi is int dpi ? dpi.ToString() : "não informado")}");
            sb.AppendLine($"Escala do Windows: {(m.ScalePercent is int s ? $"{s}%" : "não informada")}");
            sb.AppendLine("Modo: Monitor físico");
            sb.AppendLine($"ID do dispositivo: {m.InstanceId ?? "não informado"}");

            if (readEdid(m) is { } edid)
            {
                sb.AppendLine($"EDID: versão {edid.Version}, {edid.ExtensionCount} extensão(ões)");
                sb.AppendLine($"EDID identificação: {edid.MonitorId}");
                sb.AppendLine($"EDID modelo: {edid.MonitorName ?? "não informado"}");
                sb.AppendLine($"EDID número de série: {edid.SerialNumber ?? "não informado"}");
                sb.AppendLine($"EDID ano de fabricação: {(edid.ManufactureYear is int y ? y.ToString() : "não informado")}");
                sb.AppendLine($"EDID resolução nativa: {(edid.NativeMode is { } n ? n.ToString() : "não informada")}");
                sb.AppendLine($"EDID taxa máxima: {(edid.MaxRefreshRate is int r ? $"{r} Hz" : "não informada")}");
            }
            else
            {
                sb.AppendLine("EDID: não disponível");
            }

            var custom = readCustomModes(m);
            sb.AppendLine($"Resoluções personalizadas ({custom.Count}):");
            foreach (var mode in custom)
                sb.AppendLine($"  {mode}{(m.Supports(mode) ? "" : "  (não listada pelo driver)")}");

            sb.AppendLine($"Modos suportados ({m.SupportedModes.Count}):");
            foreach (var mode in m.SupportedModes)
                sb.AppendLine($"  {mode}");
        }

        sb.AppendLine();
        sb.AppendLine("=== Recursos ===");
        sb.AppendLine("Resoluções personalizadas: disponível para monitores físicos (EDID substituto)");
        sb.AppendLine($"Monitor virtual: {(virtualStatus.DriverInstalled ? "disponível" : "indisponível")} — {virtualStatus.Message}");
        sb.AppendLine("Escalonamento: ainda não implementado (Fase 6)");
        return sb.ToString();
    }
}
