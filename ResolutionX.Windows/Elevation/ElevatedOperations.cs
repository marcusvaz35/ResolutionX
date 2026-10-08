using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using ResolutionX.Core.Services;
using ResolutionX.Windows.Services;

namespace ResolutionX.Windows.Elevation;

/// <summary>Pedido enviado ao processo elevado. Só existe um tipo de operação: trocar o EDID substituto.</summary>
public sealed class ElevatedRequest
{
    public string MonitorInstanceId { get; set; } = "";

    /// <summary>EDID substituto completo em Base64; null remove o substituto (volta ao EDID do monitor).</summary>
    public string? OverrideBase64 { get; set; }

    /// <summary>Placa de vídeo a reiniciar para o driver reler o EDID; null para não reiniciar.</summary>
    public string? RestartDeviceId { get; set; }
}

public sealed class ElevatedResponse
{
    public bool Success { get; set; }
    public bool DriverRestarted { get; set; }
    public bool Cancelled { get; set; }
    public string Details { get; set; } = "";
}

/// <summary>
/// As únicas ações do ResolutionX que exigem administrador. Rodam num processo separado
/// (o próprio executável, iniciado com <see cref="Switch"/>), para que o aplicativo em si
/// continue sem privilégios. Tudo que chega pelo pedido é validado antes de ser usado.
/// </summary>
public static partial class ElevatedOperations
{
    public const string Switch = "--elevated";

    [GeneratedRegex(@"^DISPLAY\\[^\\/""]+\\[^\\/""]+$", RegexOptions.IgnoreCase)]
    private static partial Regex MonitorIdRegex();

    [GeneratedRegex(@"^[A-Za-z0-9_]+\\[^""/]+\\[^""/\\]+$")]
    private static partial Regex DeviceIdRegex();

    public static string ResponsePathFor(string requestPath) => requestPath + ".result";

    /// <summary>Ponto de entrada do processo elevado. Retorna o código de saída do processo.</summary>
    public static int RunFromFile(string requestPath)
    {
        ElevatedResponse response;
        try
        {
            var request = JsonSerializer.Deserialize<ElevatedRequest>(File.ReadAllText(requestPath))
                          ?? throw new InvalidDataException("Pedido vazio.");
            response = Execute(request);
        }
        catch (Exception ex)
        {
            response = new ElevatedResponse { Success = false, Details = ex.ToString() };
        }

        try
        {
            File.WriteAllText(ResponsePathFor(requestPath), JsonSerializer.Serialize(response));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 2;
        }

        return response.Success ? 0 : 1;
    }

    public static ElevatedResponse Execute(ElevatedRequest request)
    {
        if (!MonitorIdRegex().IsMatch(request.MonitorInstanceId))
            return Failed($"ID de monitor inválido: {request.MonitorInstanceId}");

        byte[]? edid = null;
        if (request.OverrideBase64 is not null)
        {
            edid = Convert.FromBase64String(request.OverrideBase64);
            if (!EdidParser.IsValid(edid))
                return Failed("O EDID substituto recebido não é válido (tamanho, cabeçalho ou checksum).");
        }

        var path = EdidService.DeviceParametersPath(request.MonitorInstanceId)!;
        using (var key = Registry.LocalMachine.OpenSubKey(path, writable: true))
        {
            if (key is null)
                return Failed($"Chave do monitor não encontrada no registro: HKLM\\{path}");

            key.DeleteSubKeyTree(EdidService.OverrideKeyName, throwOnMissingSubKey: false);

            if (edid is not null)
            {
                using var overrideKey = key.CreateSubKey(EdidService.OverrideKeyName, writable: true);
                for (var block = 0; block * EdidParser.BlockSize < edid.Length; block++)
                {
                    var bytes = edid.AsSpan(block * EdidParser.BlockSize, EdidParser.BlockSize).ToArray();
                    overrideKey.SetValue(block.ToString(), bytes, RegistryValueKind.Binary);
                }
            }
        }

        var response = new ElevatedResponse { Success = true, Details = "EDID_OVERRIDE atualizado." };

        if (request.RestartDeviceId is { } deviceId)
        {
            var (restarted, output) = RestartDevice(deviceId);
            response.DriverRestarted = restarted;
            response.Details += "\n" + output;
        }

        return response;
    }

    /// <summary>Reinicia o dispositivo com a ferramenta do próprio Windows (pnputil, Windows 10 2004+).</summary>
    private static (bool Restarted, string Output) RestartDevice(string deviceId)
    {
        if (!DeviceIdRegex().IsMatch(deviceId))
            return (false, $"ID de dispositivo inválido: {deviceId}");

        try
        {
            var pnputil = Path.Combine(Environment.SystemDirectory, "pnputil.exe");
            var startInfo = new ProcessStartInfo(pnputil)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("/restart-device");
            startInfo.ArgumentList.Add(deviceId);

            using var process = Process.Start(startInfo);
            if (process is null)
                return (false, "pnputil não pôde ser iniciado.");

            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            if (!process.WaitForExit(60_000))
            {
                process.Kill();
                return (false, "pnputil /restart-device não terminou em 60 segundos.");
            }

            return (process.ExitCode == 0,
                $"pnputil /restart-device \"{deviceId}\" terminou com código {process.ExitCode}.\n{output.Trim()}");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return (false, "Falha ao executar pnputil: " + ex.Message);
        }
    }

    private static ElevatedResponse Failed(string details) => new() { Success = false, Details = details };
}
