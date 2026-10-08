using ResolutionX.Core.Models;

namespace ResolutionX.Core.Interfaces;

/// <summary>
/// Troca a resolução de um monitor FÍSICO com rede de segurança: toda troca é temporária
/// até ser confirmada, e a configuração anterior fica guardada para restauração.
/// </summary>
public interface IResolutionService
{
    /// <summary>Há uma troca temporária aguardando confirmação ou restauração.</summary>
    bool HasPendingChange { get; }

    /// <summary>Pergunta ao driver se ele aceita o modo, sem alterar nada na tela.</summary>
    OperationResult CheckMode(MonitorInfo monitor, DisplayMode mode);

    /// <summary>
    /// Guarda a configuração atual, valida o modo com o driver, aplica sem gravar no registro
    /// e confere se o Windows realmente passou a usar o modo pedido.
    /// </summary>
    OperationResult ApplyTemporary(MonitorInfo monitor, DisplayMode mode);

    /// <summary>
    /// Aceita o modo que está na tela. Com <paramref name="persist"/> ele é gravado no registro
    /// e sobrevive a reinicializações; sem, vale só até a próxima troca ou reinício.
    /// </summary>
    OperationResult ConfirmCurrent(MonitorInfo monitor, bool persist);

    /// <summary>Volta para a configuração guardada antes da última troca temporária.</summary>
    OperationResult RestorePrevious();
}
