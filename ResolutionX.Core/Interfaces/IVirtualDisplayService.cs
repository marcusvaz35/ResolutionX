using ResolutionX.Core.Models;

namespace ResolutionX.Core.Interfaces;

/// <summary>
/// Contrato entre o aplicativo e o driver de monitor virtual (Indirect Display Driver).
/// O aplicativo nunca fala com o driver diretamente: tudo passa por aqui.
/// </summary>
public interface IVirtualDisplayService
{
    VirtualDisplayStatus GetStatus();

    IReadOnlyList<VirtualDisplayInfo> GetVirtualDisplays();

    OperationResult CreateVirtualDisplay(int width, int height, int refreshRate);

    OperationResult RemoveVirtualDisplay(string id);

    OperationResult SetVirtualResolution(string id, int width, int height, int refreshRate);

    OperationResult EnableVirtualDisplay(string id);

    OperationResult DisableVirtualDisplay(string id);
}
