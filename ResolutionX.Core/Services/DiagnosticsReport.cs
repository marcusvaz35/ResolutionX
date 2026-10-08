using System.Text;
using ResolutionX.Core.Models;

namespace ResolutionX.Core.Services;

/// <summary>Monta o texto da tela "Diagnóstico" a partir do que foi lido do sistema.</summary>
public static class DiagnosticsReport
{
    public static string Build(IReadOnlyList<MonitorInfo> monitors, VirtualDisplayStatus virtualStatus)
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
            sb.AppendLine($"Modos suportados ({m.SupportedModes.Count}):");
            foreach (var mode in m.SupportedModes)
                sb.AppendLine($"  {mode}");
        }

        sb.AppendLine();
        sb.AppendLine("=== Recursos ===");
        sb.AppendLine("Resoluções personalizadas (fora da lista do driver): ainda não implementado (Fase 2)");
        sb.AppendLine($"Monitor virtual: {(virtualStatus.DriverInstalled ? "disponível" : "indisponível")} — {virtualStatus.Message}");
        sb.AppendLine("Escalonamento: ainda não implementado (Fase 6)");
        return sb.ToString();
    }
}
