using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;

namespace ResolutionX.Windows.Elevation;

/// <summary>
/// Executa um <see cref="ElevatedRequest"/> com privilégio de administrador. Se o aplicativo já
/// estiver elevado, executa direto; senão inicia uma cópia elevada de si mesmo, o que faz o
/// Windows mostrar o pedido de permissão (UAC) apenas neste momento.
/// </summary>
internal static class ElevationLauncher
{
    private const int ERROR_CANCELLED = 1223;
    private const int TimeoutMilliseconds = 120_000;

    public static bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public static ElevatedResponse Run(ElevatedRequest request)
    {
        if (IsElevated)
        {
            try
            {
                return ElevatedOperations.Execute(request);
            }
            catch (Exception ex)
            {
                return new ElevatedResponse { Success = false, Details = ex.ToString() };
            }
        }

        var executable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executable))
            return new ElevatedResponse { Success = false, Details = "Caminho do executável não encontrado." };

        var requestPath = Path.Combine(Path.GetTempPath(), $"ResolutionX-{Guid.NewGuid():N}.json");
        var responsePath = ElevatedOperations.ResponsePathFor(requestPath);

        try
        {
            File.WriteAllText(requestPath, JsonSerializer.Serialize(request));

            var startInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
                Arguments = $"{ElevatedOperations.Switch} \"{requestPath}\""
            };

            using var process = Process.Start(startInfo);
            if (process is null)
                return new ElevatedResponse { Success = false, Details = "O processo elevado não pôde ser iniciado." };

            if (!process.WaitForExit(TimeoutMilliseconds))
                return new ElevatedResponse { Success = false, Details = "O processo elevado não respondeu a tempo." };

            if (!File.Exists(responsePath))
            {
                return new ElevatedResponse
                {
                    Success = false,
                    Details = $"O processo elevado terminou com código {process.ExitCode} sem deixar resposta."
                };
            }

            return JsonSerializer.Deserialize<ElevatedResponse>(File.ReadAllText(responsePath))
                   ?? new ElevatedResponse { Success = false, Details = "Resposta vazia do processo elevado." };
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ERROR_CANCELLED)
        {
            return new ElevatedResponse { Success = false, Cancelled = true, Details = "Permissão de administrador recusada." };
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException or JsonException)
        {
            return new ElevatedResponse { Success = false, Details = ex.ToString() };
        }
        finally
        {
            TryDelete(requestPath);
            TryDelete(responsePath);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
