using System.Security;
using Microsoft.Win32;
using ResolutionX.Core.Interfaces;
using ResolutionX.Core.Models;
using ResolutionX.Core.Services;

namespace ResolutionX.Windows.Services;

/// <summary>
/// Lê o EDID que o Windows guarda no registro para cada monitor:
/// HKLM\SYSTEM\CurrentControlSet\Enum\DISPLAY\{modelo}\{instância}\Device Parameters.
/// A leitura não exige administrador.
/// </summary>
public sealed class EdidService : IEdidService
{
    internal const string OverrideKeyName = "EDID_OVERRIDE";

    internal static string? DeviceParametersPath(string? instanceId)
        => string.IsNullOrEmpty(instanceId)
            ? null
            : $@"SYSTEM\CurrentControlSet\Enum\{instanceId}\Device Parameters";

    public byte[]? ReadEdid(MonitorInfo monitor)
    {
        using var key = OpenDeviceParameters(monitor);
        return key?.GetValue("EDID") as byte[];
    }

    /// <summary>O EDID substituto é gravado um bloco de 128 bytes por valor: "0", "1", "2"...</summary>
    public byte[]? ReadOverride(MonitorInfo monitor)
    {
        using var key = OpenDeviceParameters(monitor);
        if (key is null)
            return null;

        try
        {
            using var overrideKey = key.OpenSubKey(OverrideKeyName);
            if (overrideKey is null)
                return null;

            var blocks = new List<byte>();
            for (var i = 0; overrideKey.GetValue(i.ToString()) is byte[] block; i++)
                blocks.AddRange(block);

            return blocks.Count > 0 ? blocks.ToArray() : null;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    public EdidInfo? GetInfo(MonitorInfo monitor) => EdidParser.Parse(ReadEdid(monitor));

    private static RegistryKey? OpenDeviceParameters(MonitorInfo monitor)
    {
        var path = DeviceParametersPath(monitor.InstanceId);
        if (path is null)
            return null;

        try
        {
            return Registry.LocalMachine.OpenSubKey(path);
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }
}
