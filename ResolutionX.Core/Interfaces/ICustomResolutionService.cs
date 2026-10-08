using ResolutionX.Core.Models;

namespace ResolutionX.Core.Interfaces;

/// <summary>
/// Cria resoluções que o driver não oferece para um monitor FÍSICO, acrescentando-as ao EDID
/// que o Windows usa para aquele monitor (mecanismo oficial EDID_OVERRIDE).
///
/// Criar apenas inclui a resolução na lista do driver. Testar, aplicar e restaurar continuam
/// sendo feitos por <see cref="IResolutionService"/>, com a mesma confirmação de segurança.
/// </summary>
public interface ICustomResolutionService
{
    /// <summary>Resoluções personalizadas criadas pelo ResolutionX para este monitor.</summary>
    IReadOnlyList<DisplayMode> GetCustomResolutions(MonitorInfo monitor);

    /// <summary>Verifica, sem alterar nada, se a resolução pode ser criada para este monitor.</summary>
    OperationResult CheckCanCreate(MonitorInfo monitor, DisplayMode mode);

    /// <summary>
    /// Grava a resolução no EDID substituto e reinicia o driver de vídeo. Exige administrador:
    /// o Windows mostra o pedido de permissão. O resultado é conferido lendo o registro de volta.
    /// </summary>
    OperationResult CreateResolution(MonitorInfo monitor, DisplayMode mode);

    /// <summary>Remove uma resolução criada. Ao remover a última, o EDID original volta a valer.</summary>
    OperationResult DeleteResolution(MonitorInfo monitor, DisplayMode mode);
}
