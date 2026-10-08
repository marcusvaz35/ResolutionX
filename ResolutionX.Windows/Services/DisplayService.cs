using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using ResolutionX.Core.Interfaces;
using ResolutionX.Core.Models;
using ResolutionX.Windows.WindowsApi;

namespace ResolutionX.Windows.Services;

/// <summary>
/// Detecta os monitores ativos combinando duas famílias de API do Windows:
/// EnumDisplayDevices/EnumDisplaySettingsEx (adaptador, modos, posição) e
/// QueryDisplayConfig/DisplayConfigGetDeviceInfo (nome real do monitor, fabricante, conexão).
/// </summary>
public sealed partial class DisplayService : IDisplayService
{
    private sealed record TargetInfo(string? FriendlyName, string? ManufacturerId, string? ProductCode, string Connection);

    [GeneratedRegex(@"DISPLAY(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex DisplayNumberRegex();

    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var targets = QueryTargets();
        var monitors = new List<MonitorInfo>();

        for (uint i = 0; ; i++)
        {
            var adapter = NativeMethods.NewDisplayDevice();
            if (!NativeMethods.EnumDisplayDevices(null, i, ref adapter, 0))
                break;

            var attached = (adapter.StateFlags & NativeMethods.DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) != 0;
            var mirroring = (adapter.StateFlags & NativeMethods.DISPLAY_DEVICE_MIRRORING_DRIVER) != 0;
            if (!attached || mirroring)
                continue;

            var current = NativeMethods.NewDevMode();
            if (!NativeMethods.EnumDisplaySettingsEx(adapter.DeviceName, NativeMethods.ENUM_CURRENT_SETTINGS, ref current, 0))
                continue;

            var currentMode = ToMode(current);
            var modes = EnumerateModes(adapter.DeviceName);
            if (!modes.Contains(currentMode))
                modes.Add(currentMode);
            modes = Sort(modes);

            targets.TryGetValue(adapter.DeviceName, out var target);
            var (dpi, scale) = GetDpi(current.dmPositionX, current.dmPositionY);

            monitors.Add(new MonitorInfo
            {
                DeviceName = adapter.DeviceName,
                Index = ParseIndex(adapter.DeviceName, monitors.Count + 1),
                FriendlyName = ResolveMonitorName(adapter.DeviceName, target),
                ManufacturerId = target?.ManufacturerId,
                ProductCode = target?.ProductCode,
                Connection = target?.Connection ?? "Conexão desconhecida",
                IsPrimary = (adapter.StateFlags & NativeMethods.DISPLAY_DEVICE_PRIMARY_DEVICE) != 0,
                CurrentMode = currentMode,
                MaxMode = modes[0],
                SupportedModes = modes,
                Orientation = DescribeOrientation(current.dmDisplayOrientation),
                PositionX = current.dmPositionX,
                PositionY = current.dmPositionY,
                Dpi = dpi,
                ScalePercent = scale,
                Gpu = GpuDriverReader.Read(adapter.DeviceString, adapter.DeviceID, adapter.DeviceKey)
            });
        }

        return monitors.OrderBy(m => m.Index).ToList();
    }

    private static DisplayMode ToMode(in DEVMODE dm)
        => new((int)dm.dmPelsWidth, (int)dm.dmPelsHeight, (int)dm.dmDisplayFrequency);

    /// <summary>Modos que o driver declara para o monitor, sem entrelaçados e sem cores abaixo de 32 bits.</summary>
    private static List<DisplayMode> EnumerateModes(string deviceName)
    {
        var modes = new HashSet<DisplayMode>();
        for (var i = 0; ; i++)
        {
            var dm = NativeMethods.NewDevMode();
            if (!NativeMethods.EnumDisplaySettingsEx(deviceName, i, ref dm, 0))
                break;

            if (dm.dmBitsPerPel < 32 || (dm.dmDisplayFlags & NativeMethods.DM_INTERLACED) != 0)
                continue;
            if (dm.dmPelsWidth == 0 || dm.dmPelsHeight == 0 || dm.dmDisplayFrequency <= 1)
                continue;

            modes.Add(ToMode(dm));
        }

        return modes.ToList();
    }

    private static List<DisplayMode> Sort(IEnumerable<DisplayMode> modes)
        => modes
            .OrderByDescending(m => (long)m.Width * m.Height)
            .ThenByDescending(m => m.Width)
            .ThenByDescending(m => m.RefreshRate)
            .ToList();

    private static int ParseIndex(string deviceName, int fallback)
    {
        var match = DisplayNumberRegex().Match(deviceName);
        return match.Success && int.TryParse(match.Groups[1].Value, out var n) ? n : fallback;
    }

    private static string ResolveMonitorName(string adapterDeviceName, TargetInfo? target)
    {
        if (!string.IsNullOrWhiteSpace(target?.FriendlyName))
            return target.FriendlyName;

        // Sem nome no EDID (comum em telas internas de notebook): usa o nome do driver do monitor.
        var monitor = NativeMethods.NewDisplayDevice();
        if (NativeMethods.EnumDisplayDevices(adapterDeviceName, 0, ref monitor, 0) &&
            !string.IsNullOrWhiteSpace(monitor.DeviceString))
            return monitor.DeviceString;

        return "Monitor";
    }

    private static string DescribeOrientation(uint orientation) => orientation switch
    {
        0 => "Paisagem",
        1 => "Retrato",
        2 => "Paisagem (invertida)",
        3 => "Retrato (invertido)",
        _ => "Desconhecida"
    };

    /// <summary>
    /// DPI efetivo do monitor. Depende de o processo ser "Per-Monitor DPI aware"
    /// (declarado no app.manifest do aplicativo); senão o Windows devolve sempre 96.
    /// </summary>
    private static (int? Dpi, int? Scale) GetDpi(int x, int y)
    {
        try
        {
            var handle = NativeMethods.MonitorFromPoint(new POINT { X = x, Y = y }, NativeMethods.MONITOR_DEFAULTTONULL);
            if (handle == IntPtr.Zero)
                return (null, null);

            if (NativeMethods.GetDpiForMonitor(handle, NativeMethods.MDT_EFFECTIVE_DPI, out var dpiX, out _) != 0 || dpiX == 0)
                return (null, null);

            return ((int)dpiX, (int)Math.Round(dpiX / 96.0 * 100));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return (null, null);
        }
    }

    /// <summary>Mapa "nome GDI do adaptador" → informações do monitor ligado a ele.</summary>
    private static Dictionary<string, TargetInfo> QueryTargets()
    {
        var result = new Dictionary<string, TargetInfo>(StringComparer.OrdinalIgnoreCase);

        DISPLAYCONFIG_PATH_INFO[] paths = [];
        uint pathCount = 0;
        var status = NativeMethods.ERROR_INSUFFICIENT_BUFFER;

        // A topologia pode mudar entre as duas chamadas; nesse caso o Windows pede um buffer maior.
        for (var attempt = 0; attempt < 3 && status == NativeMethods.ERROR_INSUFFICIENT_BUFFER; attempt++)
        {
            if (NativeMethods.GetDisplayConfigBufferSizes(NativeMethods.QDC_ONLY_ACTIVE_PATHS, out pathCount, out var modeCount)
                != NativeMethods.ERROR_SUCCESS)
                return result;

            paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
            status = NativeMethods.QueryDisplayConfig(
                NativeMethods.QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
        }

        if (status != NativeMethods.ERROR_SUCCESS)
            return result;

        for (var i = 0; i < pathCount; i++)
        {
            var path = paths[i];

            var source = new DISPLAYCONFIG_SOURCE_DEVICE_NAME
            {
                header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                {
                    type = NativeMethods.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME,
                    size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>(),
                    adapterId = path.sourceInfo.adapterId,
                    id = path.sourceInfo.id
                }
            };
            if (NativeMethods.DisplayConfigGetDeviceInfo(ref source) != NativeMethods.ERROR_SUCCESS ||
                string.IsNullOrEmpty(source.viewGdiDeviceName))
                continue;

            var target = new DISPLAYCONFIG_TARGET_DEVICE_NAME
            {
                header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                {
                    type = NativeMethods.DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME,
                    size = (uint)Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>(),
                    adapterId = path.targetInfo.adapterId,
                    id = path.targetInfo.id
                }
            };
            if (NativeMethods.DisplayConfigGetDeviceInfo(ref target) != NativeMethods.ERROR_SUCCESS)
            {
                result.TryAdd(source.viewGdiDeviceName,
                    new TargetInfo(null, null, null, DescribeConnection(path.targetInfo.outputTechnology)));
                continue;
            }

            var hasEdidIds = (target.flags & NativeMethods.TARGET_NAME_EDID_IDS_VALID) != 0;
            result.TryAdd(source.viewGdiDeviceName, new TargetInfo(
                target.monitorFriendlyDeviceName,
                hasEdidIds ? DecodeManufacturerId(target.edidManufactureId) : null,
                hasEdidIds ? target.edidProductCodeId.ToString("X4") : null,
                DescribeConnection(target.outputTechnology)));
        }

        return result;
    }

    /// <summary>
    /// O ID de fabricante do EDID são três letras de 5 bits em big-endian;
    /// o Windows entrega os dois bytes na ordem do EDID, então é preciso invertê-los.
    /// </summary>
    private static string? DecodeManufacturerId(ushort raw)
    {
        var value = (ushort)((raw >> 8) | (raw << 8));
        Span<char> letters =
        [
            (char)('A' - 1 + ((value >> 10) & 0x1F)),
            (char)('A' - 1 + ((value >> 5) & 0x1F)),
            (char)('A' - 1 + (value & 0x1F))
        ];

        foreach (var c in letters)
        {
            if (c is < 'A' or > 'Z')
                return null;
        }

        return new string(letters);
    }

    /// <summary>Valores de DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY (wingdi.h).</summary>
    private static string DescribeConnection(uint technology) => technology switch
    {
        0 => "VGA",
        1 => "S-Video",
        2 => "Vídeo composto",
        3 => "Vídeo componente",
        4 => "DVI",
        5 => "HDMI",
        6 => "Tela interna (LVDS)",
        8 => "D-Terminal",
        9 => "SDI",
        10 => "DisplayPort",
        11 => "Tela interna (eDP)",
        12 => "UDI",
        13 => "Tela interna (UDI)",
        14 => "SDTV",
        15 => "Miracast",
        16 => "Monitor indireto (cabo)",
        17 => "Monitor virtual",
        18 => "DisplayPort (túnel USB)",
        0x80000000 => "Tela interna",
        _ => "Conexão desconhecida"
    };
}
