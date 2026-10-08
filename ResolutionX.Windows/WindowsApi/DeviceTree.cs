using System.Runtime.InteropServices;

namespace ResolutionX.Windows.WindowsApi;

/// <summary>Consulta a árvore de dispositivos do Windows (Configuration Manager, cfgmgr32.dll).</summary>
internal static class DeviceTree
{
    private const int CR_SUCCESS = 0;
    private const int MaxDeviceIdLength = 512;

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNodeW(out uint pdnDevInst, string pDeviceID, uint ulFlags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_Parent(out uint pdnDevInst, uint dnDevInst, uint ulFlags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_IDW(uint dnDevInst, [Out] char[] buffer, uint bufferLen, uint ulFlags);

    /// <summary>
    /// ID de instância do dispositivo pai. Para um monitor (DISPLAY\...), o pai é a placa de vídeo.
    /// </summary>
    public static string? GetParentInstanceId(string instanceId)
    {
        try
        {
            if (CM_Locate_DevNodeW(out var device, instanceId, 0) != CR_SUCCESS)
                return null;
            if (CM_Get_Parent(out var parent, device, 0) != CR_SUCCESS)
                return null;

            var buffer = new char[MaxDeviceIdLength];
            if (CM_Get_Device_IDW(parent, buffer, (uint)buffer.Length, 0) != CR_SUCCESS)
                return null;

            var length = Array.IndexOf(buffer, '\0');
            var id = new string(buffer, 0, length < 0 ? buffer.Length : length);
            return id.Length > 0 && !id.StartsWith("HTREE", StringComparison.OrdinalIgnoreCase) ? id : null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }
}
