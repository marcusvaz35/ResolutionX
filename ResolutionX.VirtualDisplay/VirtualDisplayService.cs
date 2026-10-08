using ResolutionX.Core.Interfaces;
using ResolutionX.Core.Models;

namespace ResolutionX.VirtualDisplay;

/// <summary>
/// Ponto único de comunicação com o driver de monitor virtual (Indirect Display Driver).
///
/// ESTADO NA FASE 1: o driver ainda não existe (ver ResolutionX.Driver/README.md), então este
/// serviço informa honestamente que o recurso está indisponível e recusa todas as operações.
/// Ele não simula monitores.
///
/// O QUE FALTA (Fases 4 e 5):
///  1. O driver IDD (UMDF + IddCx, em C++), que cria os monitores e anuncia os modos ao Windows.
///  2. O canal de controle: o driver expõe uma interface de dispositivo e este serviço envia
///     comandos por DeviceIoControl (criar, remover, trocar modo, habilitar, desabilitar).
///  3. Instalação/remoção do driver com elevação de administrador, só nesse momento.
/// </summary>
public sealed class VirtualDisplayService : IVirtualDisplayService
{
    private const string NotAvailable =
        "O driver de monitor virtual ainda não faz parte desta versão do ResolutionX.";

    public VirtualDisplayStatus GetStatus() => new(DriverInstalled: false, NotAvailable);

    public IReadOnlyList<VirtualDisplayInfo> GetVirtualDisplays() => [];

    public OperationResult CreateVirtualDisplay(int width, int height, int refreshRate) => Unavailable();

    public OperationResult RemoveVirtualDisplay(string id) => Unavailable();

    public OperationResult SetVirtualResolution(string id, int width, int height, int refreshRate) => Unavailable();

    public OperationResult EnableVirtualDisplay(string id) => Unavailable();

    public OperationResult DisableVirtualDisplay(string id) => Unavailable();

    private static OperationResult Unavailable() => OperationResult.Fail(
        "O Monitor Virtual ainda não está disponível.",
        NotAvailable,
        "VirtualDisplayService: driver IDD não instalado (implementação prevista para as Fases 4 e 5).");
}
