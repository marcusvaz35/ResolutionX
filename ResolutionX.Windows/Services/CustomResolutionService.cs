using System.Text.Json;
using ResolutionX.Core.Interfaces;
using ResolutionX.Core.Models;
using ResolutionX.Core.Services;
using ResolutionX.Windows.Elevation;
using ResolutionX.Windows.WindowsApi;

namespace ResolutionX.Windows.Services;

/// <summary>
/// Resoluções personalizadas para monitores físicos via EDID substituto (EDID_OVERRIDE).
///
/// Como funciona: o Windows permite guardar no registro um EDID que substitui o informado pelo
/// monitor. Este serviço parte do EDID original, acrescenta um "Detailed Timing" para cada
/// resolução criada e reinicia o driver de vídeo, que então passa a listar a resolução nova.
/// Funciona com qualquer fabricante de GPU porque quem lê o EDID é o Windows, não um painel do driver.
///
/// O que NÃO faz: obrigar o monitor a aceitar o sinal. Por isso a resolução criada só é
/// adicionada à lista; exibi-la continua passando pelo teste de 15 segundos.
/// </summary>
public sealed class CustomResolutionService : ICustomResolutionService
{
    /// <summary>O que foi criado para um monitor e o estado a que se deve voltar ao remover tudo.</summary>
    private sealed class MonitorRecord
    {
        public string InstanceId { get; set; } = "";
        /// <summary>EDID sobre o qual as resoluções são acrescentadas.</summary>
        public string BaseEdidBase64 { get; set; } = "";
        /// <summary>EDID substituto que já existia antes do ResolutionX (ex.: criado por outra ferramenta).</summary>
        public string? PreviousOverrideBase64 { get; set; }
        public List<DisplayMode> Modes { get; set; } = [];
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly IEdidService _edidService;
    private readonly string _filePath;
    private readonly object _gate = new();
    private readonly List<MonitorRecord> _records;

    public CustomResolutionService(IEdidService edidService, string? filePath = null)
    {
        _edidService = edidService;
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ResolutionX",
            "custom-resolutions.json");
        _records = Load();
    }

    public IReadOnlyList<DisplayMode> GetCustomResolutions(MonitorInfo monitor)
    {
        lock (_gate)
            return FindRecord(monitor)?.Modes.ToList() ?? [];
    }

    public OperationResult CheckCanCreate(MonitorInfo monitor, DisplayMode mode)
    {
        lock (_gate)
            return Prepare(monitor, mode, out _, out _);
    }

    public OperationResult CreateResolution(MonitorInfo monitor, DisplayMode mode)
    {
        lock (_gate)
        {
            var check = Prepare(monitor, mode, out var record, out var edid);
            if (!check.Success)
                return check;

            var written = WriteOverride(monitor, edid);
            if (!written.Success)
                return written;

            record!.Modes.Add(mode);
            if (!_records.Contains(record))
                _records.Add(record);
            Save();

            return written;
        }
    }

    public OperationResult DeleteResolution(MonitorInfo monitor, DisplayMode mode)
    {
        lock (_gate)
        {
            var record = FindRecord(monitor);
            if (record is null || !record.Modes.Contains(mode))
                return OperationResult.Fail("Esta resolução personalizada não foi encontrada.");

            var remaining = record.Modes.Where(m => m != mode).ToList();

            byte[]? edid;
            if (remaining.Count == 0)
            {
                // Sem resoluções restantes, volta exatamente ao estado anterior ao ResolutionX.
                edid = record.PreviousOverrideBase64 is { } previous ? Convert.FromBase64String(previous) : null;
            }
            else
            {
                edid = BuildEdid(Convert.FromBase64String(record.BaseEdidBase64), remaining);
                if (edid is null)
                    return OperationResult.Fail(
                        "Não foi possível remover esta resolução.",
                        "O EDID do monitor não pôde ser remontado sem ela.");
            }

            var written = WriteOverride(monitor, edid);
            if (!written.Success)
                return written;

            record.Modes = remaining;
            if (remaining.Count == 0)
                _records.Remove(record);
            Save();

            return written;
        }
    }

    /// <summary>
    /// Faz todas as verificações que não exigem administrador e monta o EDID resultante.
    /// </summary>
    private OperationResult Prepare(MonitorInfo monitor, DisplayMode mode, out MonitorRecord? record, out byte[]? edid)
    {
        record = null;
        edid = null;

        if (monitor.Gpu.IsBasicDriver)
        {
            return OperationResult.Fail(
                "Não é possível criar resoluções com o driver básico da Microsoft.",
                "Esse driver ignora resoluções personalizadas. Instale o driver do fabricante da placa de vídeo " +
                "ou utilize o Monitor Virtual.");
        }

        if (monitor.InstanceId is null)
        {
            return OperationResult.Fail(
                "Não é possível criar resoluções para este monitor.",
                "O Windows não informou a identificação do dispositivo deste monitor.");
        }

        if (monitor.Supports(mode))
            return OperationResult.Fail("Esta resolução já está disponível para este monitor.");

        if (mode.Width is < 640 or > 4095 || mode.Height is < 480 or > 4095 || mode.RefreshRate is < 20 or > 500)
        {
            return OperationResult.Fail(
                "Esta resolução está fora dos limites aceitos.",
                "Use largura entre 640 e 4095, altura entre 480 e 4095 e taxa entre 20 e 500 Hz.");
        }

        var timing = CvtTimingCalculator.ReducedBlanking(mode.Width, mode.Height, mode.RefreshRate);
        if (!EdidEditor.FitsDetailedTiming(timing))
        {
            return OperationResult.Fail(
                "Esta resolução exige um sinal rápido demais para ser descrita ao Windows.",
                "Reduza a resolução ou a taxa de atualização.",
                $"Pixel clock calculado: {timing.PixelClockMHz:F2} MHz (limite do formato: 655,35 MHz).");
        }

        record = FindRecord(monitor);
        if (record is null)
        {
            var original = _edidService.ReadEdid(monitor);
            var existingOverride = _edidService.ReadOverride(monitor);
            var baseEdid = EdidParser.IsValid(existingOverride) ? existingOverride : original;

            if (!EdidParser.IsValid(baseEdid))
            {
                return OperationResult.Fail(
                    "Não é possível criar resoluções para este monitor.",
                    "O monitor não informa um EDID válido ao Windows, e é nele que a resolução seria adicionada. " +
                    "Você pode utilizar o Monitor Virtual.",
                    $"EDID lido de HKLM\\{EdidService.DeviceParametersPath(monitor.InstanceId)}: " +
                    (baseEdid is null ? "ausente" : $"{baseEdid.Length} bytes, inválido"));
            }

            record = new MonitorRecord
            {
                InstanceId = monitor.InstanceId,
                BaseEdidBase64 = Convert.ToBase64String(baseEdid!),
                PreviousOverrideBase64 = EdidParser.IsValid(existingOverride)
                    ? Convert.ToBase64String(existingOverride!)
                    : null
            };
        }

        if (record.Modes.Contains(mode))
        {
            return OperationResult.Fail(
                "Esta resolução já foi criada para este monitor, mas o driver ainda não a oferece.",
                "Reinicie o computador. Se ela continuar sem aparecer, o driver de vídeo não aceita esta " +
                "resolução para este monitor; exclua-a em Resoluções personalizadas.");
        }

        edid = BuildEdid(Convert.FromBase64String(record.BaseEdidBase64), [.. record.Modes, mode]);
        if (edid is null)
        {
            return OperationResult.Fail(
                "Não há mais espaço para resoluções personalizadas neste monitor.",
                record.Modes.Count > 0
                    ? "Exclua uma das resoluções personalizadas existentes para liberar espaço."
                    : "O EDID deste monitor não tem espaço livre para uma resolução adicional.");
        }

        return OperationResult.Ok();
    }

    private static byte[]? BuildEdid(byte[] baseEdid, IEnumerable<DisplayMode> modes)
    {
        byte[]? edid = baseEdid;
        foreach (var mode in modes)
        {
            var timing = CvtTimingCalculator.ReducedBlanking(mode.Width, mode.Height, mode.RefreshRate);
            edid = EdidEditor.AddDetailedTiming(edid, timing);
            if (edid is null)
                return null;
        }

        return edid;
    }

    /// <summary>Grava (ou remove, se null) o EDID substituto com elevação e confere o resultado.</summary>
    private OperationResult WriteOverride(MonitorInfo monitor, byte[]? edid)
    {
        var gpuInstanceId = DeviceTree.GetParentInstanceId(monitor.InstanceId!);

        var response = ElevationLauncher.Run(new ElevatedRequest
        {
            MonitorInstanceId = monitor.InstanceId!,
            OverrideBase64 = edid is null ? null : Convert.ToBase64String(edid),
            RestartDeviceId = gpuInstanceId
        });

        if (response.Cancelled)
        {
            return OperationResult.Fail(
                "A permissão de administrador foi recusada.",
                "Criar ou remover uma resolução personalizada altera uma configuração do sistema e precisa dessa " +
                "permissão. Nada foi alterado.",
                failure: OperationFailure.ElevationDeclined);
        }

        if (!response.Success)
        {
            return OperationResult.Fail(
                "Não foi possível gravar a resolução personalizada no Windows.",
                "Nada foi alterado. Veja os detalhes para a causa.",
                response.Details);
        }

        // Não confia no processo elevado: lê o registro de volta e compara byte a byte.
        var stored = _edidService.ReadOverride(monitor);
        var matches = edid is null ? stored is null : stored is not null && stored.AsSpan().SequenceEqual(edid);
        if (!matches)
        {
            return OperationResult.Fail(
                "O Windows não guardou a resolução personalizada.",
                "A gravação foi executada, mas o conteúdo lido em seguida é diferente do esperado.",
                $"EDID_OVERRIDE esperado: {edid?.Length ?? 0} bytes; lido: {stored?.Length ?? 0} bytes.\n{response.Details}");
        }

        return response.DriverRestarted
            ? OperationResult.Ok()
            : OperationResult.Ok("O driver de vídeo não pôde ser reiniciado automaticamente. " +
                                 "Reinicie o computador para concluir.");
    }

    private MonitorRecord? FindRecord(MonitorInfo monitor)
        => monitor.InstanceId is null
            ? null
            : _records.FirstOrDefault(r => string.Equals(r.InstanceId, monitor.InstanceId, StringComparison.OrdinalIgnoreCase));

    private List<MonitorRecord> Load()
    {
        try
        {
            return File.Exists(_filePath)
                ? JsonSerializer.Deserialize<List<MonitorRecord>>(File.ReadAllText(_filePath)) ?? []
                : [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(_records, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A resolução já está gravada no Windows; perder o registro local só impede listá-la aqui.
        }
    }
}
