using System.Security;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using ResolutionX.Core.Models;

namespace ResolutionX.Windows.Services;

/// <summary>
/// Descobre fabricante e driver de um adaptador de vídeo a partir dos dados que
/// EnumDisplayDevices devolve (nome, ID de hardware e chave de registro do adaptador).
/// </summary>
internal static partial class GpuDriverReader
{
    private const string RegistryMachinePrefix = @"\Registry\Machine\";
    private const string DisplayClassKey =
        @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    [GeneratedRegex(@"VEN_([0-9A-F]{4})", RegexOptions.IgnoreCase)]
    private static partial Regex VendorIdRegex();

    public static GpuInfo Read(string adapterName, string deviceId, string deviceKey)
    {
        var (provider, version, date) = ReadFromAdapterKey(deviceKey);
        if (version is null)
            (provider, version, date) = ReadFromClassKey(adapterName, deviceId);

        return new GpuInfo(
            string.IsNullOrWhiteSpace(adapterName) ? "Adaptador de vídeo desconhecido" : adapterName,
            DetectVendor(adapterName, deviceId),
            provider,
            version,
            date,
            deviceId);
    }

    private static GpuVendor DetectVendor(string adapterName, string deviceId)
    {
        // O driver básico roda sobre hardware de qualquer fabricante, então o nome vem antes do ID.
        if (adapterName.Contains("Basic Display", StringComparison.OrdinalIgnoreCase) ||
            adapterName.Contains("Basic Render", StringComparison.OrdinalIgnoreCase))
            return GpuVendor.MicrosoftBasic;

        var match = VendorIdRegex().Match(deviceId);
        if (match.Success)
        {
            switch (match.Groups[1].Value.ToUpperInvariant())
            {
                case "8086": return GpuVendor.Intel;
                case "10DE": return GpuVendor.Nvidia;
                case "1002":
                case "1022": return GpuVendor.Amd;
                case "5143":
                case "4D4F": return GpuVendor.Qualcomm;
                case "1414": return GpuVendor.Microsoft;
            }
        }

        if (adapterName.Contains("Intel", StringComparison.OrdinalIgnoreCase)) return GpuVendor.Intel;
        if (adapterName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)) return GpuVendor.Nvidia;
        if (adapterName.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
            adapterName.Contains("Radeon", StringComparison.OrdinalIgnoreCase)) return GpuVendor.Amd;
        if (adapterName.Contains("Qualcomm", StringComparison.OrdinalIgnoreCase) ||
            adapterName.Contains("Adreno", StringComparison.OrdinalIgnoreCase)) return GpuVendor.Qualcomm;
        if (adapterName.Contains("Microsoft", StringComparison.OrdinalIgnoreCase)) return GpuVendor.Microsoft;

        return string.IsNullOrWhiteSpace(adapterName) ? GpuVendor.Unknown : GpuVendor.Other;
    }

    /// <summary>Lê a chave que o próprio Windows aponta para o adaptador (Control\Video\{guid}\000x).</summary>
    private static (string? Provider, string? Version, string? Date) ReadFromAdapterKey(string deviceKey)
    {
        if (!deviceKey.StartsWith(RegistryMachinePrefix, StringComparison.OrdinalIgnoreCase))
            return default;

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(deviceKey[RegistryMachinePrefix.Length..]);
            return key is null ? default : ReadDriverValues(key);
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return default;
        }
    }

    /// <summary>Alternativa: procura o adaptador entre os drivers da classe "Display".</summary>
    private static (string? Provider, string? Version, string? Date) ReadFromClassKey(string adapterName, string deviceId)
    {
        try
        {
            using var classKey = Registry.LocalMachine.OpenSubKey(DisplayClassKey);
            if (classKey is null)
                return default;

            foreach (var name in classKey.GetSubKeyNames())
            {
                try
                {
                    using var key = classKey.OpenSubKey(name);
                    if (key is null)
                        continue;

                    var matchingId = key.GetValue("MatchingDeviceId") as string;
                    var description = key.GetValue("DriverDesc") as string;

                    var sameHardware = !string.IsNullOrEmpty(matchingId) &&
                                       deviceId.StartsWith(matchingId, StringComparison.OrdinalIgnoreCase);
                    var sameName = !string.IsNullOrEmpty(description) &&
                                   string.Equals(description, adapterName, StringComparison.OrdinalIgnoreCase);

                    if (sameHardware || sameName)
                        return ReadDriverValues(key);
                }
                catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException)
                {
                    // Subchaves como "Properties" são restritas; não interessam aqui.
                }
            }
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
        }

        return default;
    }

    private static (string? Provider, string? Version, string? Date) ReadDriverValues(RegistryKey key)
        => (key.GetValue("ProviderName") as string,
            key.GetValue("DriverVersion") as string,
            key.GetValue("DriverDate") as string);
}
