using ResolutionX.Core.Interfaces;
using ResolutionX.Core.Models;
using ResolutionX.Windows.WindowsApi;

namespace ResolutionX.Windows.Services;

/// <summary>
/// Troca a resolução de monitores físicos via ChangeDisplaySettingsEx.
/// Nenhuma chamada é dada como certa: o modo é validado com CDS_TEST antes,
/// e depois de aplicar o modo atual é lido de volta e comparado com o pedido.
/// </summary>
public sealed class ResolutionService : IResolutionService
{
    private sealed record Snapshot(string DeviceName, DEVMODE Mode);

    private readonly object _gate = new();
    private Snapshot? _previous;

    public bool HasPendingChange
    {
        get { lock (_gate) return _previous is not null; }
    }

    public OperationResult CheckMode(MonitorInfo monitor, DisplayMode mode)
    {
        lock (_gate)
        {
            if (!TryReadCurrent(monitor.DeviceName, out var current))
                return CannotReadMonitor(monitor);

            var requested = BuildRequest(current, mode);
            var code = NativeMethods.ChangeDisplaySettingsEx(
                monitor.DeviceName, ref requested, IntPtr.Zero, NativeMethods.CDS_TEST, IntPtr.Zero);

            return code == NativeMethods.DISP_CHANGE_SUCCESSFUL
                ? OperationResult.Ok()
                : Describe(code, monitor, mode, "CDS_TEST");
        }
    }

    public OperationResult ApplyTemporary(MonitorInfo monitor, DisplayMode mode)
    {
        lock (_gate)
        {
            if (!TryReadCurrent(monitor.DeviceName, out var current))
                return CannotReadMonitor(monitor);

            var requested = BuildRequest(current, mode);

            var testCode = NativeMethods.ChangeDisplaySettingsEx(
                monitor.DeviceName, ref requested, IntPtr.Zero, NativeMethods.CDS_TEST, IntPtr.Zero);
            if (testCode != NativeMethods.DISP_CHANGE_SUCCESSFUL)
                return Describe(testCode, monitor, mode, "CDS_TEST");

            // Uma troca ainda não confirmada continua valendo como "anterior":
            // restaurar deve sempre voltar ao último estado que o usuário aceitou.
            var snapshot = _previous is { } pending && pending.DeviceName == monitor.DeviceName
                ? pending
                : new Snapshot(monitor.DeviceName, current);

            // CDS_DYNAMIC (0): troca só a sessão atual, sem gravar no registro.
            var applyCode = NativeMethods.ChangeDisplaySettingsEx(
                monitor.DeviceName, ref requested, IntPtr.Zero, NativeMethods.CDS_DYNAMIC, IntPtr.Zero);
            if (applyCode != NativeMethods.DISP_CHANGE_SUCCESSFUL)
            {
                RestoreSnapshot(snapshot);
                return Describe(applyCode, monitor, mode, "CDS_DYNAMIC");
            }

            if (!TryReadCurrent(monitor.DeviceName, out var after) || !Matches(after, mode))
            {
                RestoreSnapshot(snapshot);
                var actual = after.dmPelsWidth > 0
                    ? $"{after.dmPelsWidth}x{after.dmPelsHeight}@{after.dmDisplayFrequency}"
                    : "ilegível";
                return OperationResult.Fail(
                    "Não foi possível aplicar esta resolução.",
                    "O Windows aceitou o pedido, mas o monitor não passou a usar a resolução solicitada. " +
                    "A configuração anterior foi restaurada.",
                    $"ChangeDisplaySettingsEx({monitor.DeviceName}, {Format(mode)}, CDS_DYNAMIC) retornou " +
                    $"DISP_CHANGE_SUCCESSFUL, mas o modo ativo lido em seguida foi {actual}.");
            }

            _previous = snapshot;
            return OperationResult.Ok();
        }
    }

    public OperationResult ConfirmCurrent(MonitorInfo monitor, bool persist)
    {
        lock (_gate)
        {
            if (persist)
            {
                if (!TryReadCurrent(monitor.DeviceName, out var current))
                    return CannotReadMonitor(monitor);

                var code = NativeMethods.ChangeDisplaySettingsEx(
                    monitor.DeviceName, ref current, IntPtr.Zero, NativeMethods.CDS_UPDATEREGISTRY, IntPtr.Zero);
                if (code != NativeMethods.DISP_CHANGE_SUCCESSFUL)
                {
                    return OperationResult.Fail(
                        "A resolução está ativa, mas não pôde ser salva no Windows.",
                        "Ela continuará em uso até você reiniciar o computador. Tente aplicar novamente.",
                        $"ChangeDisplaySettingsEx({monitor.DeviceName}, CDS_UPDATEREGISTRY) retornou {CodeName(code)} ({code}).");
                }
            }

            if (_previous?.DeviceName == monitor.DeviceName)
                _previous = null;

            return OperationResult.Ok();
        }
    }

    public OperationResult RestorePrevious()
    {
        lock (_gate)
        {
            if (_previous is null)
                return OperationResult.Ok();

            var snapshot = _previous;
            var result = RestoreSnapshot(snapshot);
            if (result.Success)
                _previous = null;
            return result;
        }
    }

    private static OperationResult RestoreSnapshot(Snapshot snapshot)
    {
        var mode = snapshot.Mode;
        var code = NativeMethods.ChangeDisplaySettingsEx(
            snapshot.DeviceName, ref mode, IntPtr.Zero, NativeMethods.CDS_DYNAMIC, IntPtr.Zero);

        var restored = code == NativeMethods.DISP_CHANGE_SUCCESSFUL &&
                       TryReadCurrent(snapshot.DeviceName, out var now) &&
                       now.dmPelsWidth == mode.dmPelsWidth &&
                       now.dmPelsHeight == mode.dmPelsHeight;
        if (restored)
            return OperationResult.Ok();

        // Último recurso: com DEVMODE nulo o Windows recarrega o modo gravado no registro,
        // que as trocas temporárias nunca alteram.
        var fallbackCode = NativeMethods.ChangeDisplaySettingsEx(
            snapshot.DeviceName, IntPtr.Zero, IntPtr.Zero, NativeMethods.CDS_DYNAMIC, IntPtr.Zero);
        if (fallbackCode == NativeMethods.DISP_CHANGE_SUCCESSFUL)
            return OperationResult.Ok();

        return OperationResult.Fail(
            "Não foi possível restaurar a resolução anterior.",
            "Reinicie o computador: a resolução de teste não foi salva e o Windows voltará à configuração anterior.",
            $"ChangeDisplaySettingsEx({snapshot.DeviceName}) ao restaurar retornou {CodeName(code)} ({code}); " +
            $"a restauração pelo registro retornou {CodeName(fallbackCode)} ({fallbackCode}).");
    }

    private static bool TryReadCurrent(string deviceName, out DEVMODE mode)
    {
        mode = NativeMethods.NewDevMode();
        return NativeMethods.EnumDisplaySettingsEx(deviceName, NativeMethods.ENUM_CURRENT_SETTINGS, ref mode, 0);
    }

    /// <summary>Parte do modo atual e altera apenas largura, altura e frequência.</summary>
    private static DEVMODE BuildRequest(DEVMODE current, DisplayMode mode)
    {
        current.dmPelsWidth = (uint)mode.Width;
        current.dmPelsHeight = (uint)mode.Height;
        current.dmDisplayFrequency = (uint)mode.RefreshRate;
        current.dmFields = NativeMethods.DM_PELSWIDTH | NativeMethods.DM_PELSHEIGHT | NativeMethods.DM_DISPLAYFREQUENCY;
        return current;
    }

    /// <summary>
    /// Tolerância de 1 Hz: o Windows trata 59,94 Hz e 60 Hz como o mesmo modo
    /// e pode reportar 59 quando foi pedido 60 (e vice-versa).
    /// </summary>
    private static bool Matches(in DEVMODE actual, DisplayMode expected)
        => actual.dmPelsWidth == (uint)expected.Width &&
           actual.dmPelsHeight == (uint)expected.Height &&
           Math.Abs((int)actual.dmDisplayFrequency - expected.RefreshRate) <= 1;

    private static string Format(DisplayMode mode) => $"{mode.Width}x{mode.Height}@{mode.RefreshRate}";

    private static OperationResult CannotReadMonitor(MonitorInfo monitor)
        => OperationResult.Fail(
            "Não foi possível ler a configuração atual deste monitor.",
            "Verifique se o monitor continua conectado e clique em Atualizar.",
            $"EnumDisplaySettingsEx({monitor.DeviceName}, ENUM_CURRENT_SETTINGS) retornou FALSE.");

    private static OperationResult Describe(int code, MonitorInfo monitor, DisplayMode mode, string flags)
    {
        var technical =
            $"ChangeDisplaySettingsEx({monitor.DeviceName}, {Format(mode)}, {flags}) retornou {CodeName(code)} ({code}).\n" +
            $"GPU: {monitor.Gpu.Name} | Driver: {monitor.Gpu.DriverVersion ?? "?"} | Monitor: {monitor.FriendlyName}";

        return code switch
        {
            NativeMethods.DISP_CHANGE_BADMODE => OperationResult.Fail(
                "Esta resolução não pode ser aplicada diretamente ao monitor físico. " +
                "Você pode utilizar o modo Monitor Virtual.",
                monitor.Gpu.IsBasicDriver
                    ? "O Windows está usando o driver básico de vídeo da Microsoft, que só aceita as resoluções " +
                      "informadas pelo próprio hardware. Instalar o driver do fabricante da placa de vídeo " +
                      "normalmente libera mais opções."
                    : $"O driver {monitor.Gpu.VendorName} não oferece {mode} para este monitor. " +
                      "Você pode criá-la como resolução personalizada, escolher uma das suportadas " +
                      "ou utilizar o Monitor Virtual.",
                technical,
                OperationFailure.ModeNotSupported),

            NativeMethods.DISP_CHANGE_RESTART => OperationResult.Fail(
                "Esta resolução só pode ser aplicada após reiniciar o computador.",
                "Por segurança o ResolutionX não aplica mudanças que exigem reinicialização, " +
                "pois não seria possível restaurá-las automaticamente.",
                technical),

            NativeMethods.DISP_CHANGE_BADDUALVIEW => OperationResult.Fail(
                "Não foi possível aplicar esta resolução.",
                "O driver gráfico não permite essa mudança com a configuração atual de múltiplos monitores.",
                technical),

            NativeMethods.DISP_CHANGE_NOTUPDATED => OperationResult.Fail(
                "Não foi possível salvar esta resolução no Windows.",
                "O sistema não conseguiu gravar a configuração. Tente novamente.",
                technical),

            _ => OperationResult.Fail(
                "Não foi possível aplicar esta resolução.",
                "Seu driver gráfico não permite essa configuração diretamente. Tente utilizar o Monitor Virtual.",
                technical)
        };
    }

    private static string CodeName(int code) => code switch
    {
        NativeMethods.DISP_CHANGE_SUCCESSFUL => "DISP_CHANGE_SUCCESSFUL",
        NativeMethods.DISP_CHANGE_RESTART => "DISP_CHANGE_RESTART",
        NativeMethods.DISP_CHANGE_FAILED => "DISP_CHANGE_FAILED",
        NativeMethods.DISP_CHANGE_BADMODE => "DISP_CHANGE_BADMODE",
        NativeMethods.DISP_CHANGE_NOTUPDATED => "DISP_CHANGE_NOTUPDATED",
        NativeMethods.DISP_CHANGE_BADFLAGS => "DISP_CHANGE_BADFLAGS",
        NativeMethods.DISP_CHANGE_BADPARAM => "DISP_CHANGE_BADPARAM",
        NativeMethods.DISP_CHANGE_BADDUALVIEW => "DISP_CHANGE_BADDUALVIEW",
        _ => "código desconhecido"
    };
}
