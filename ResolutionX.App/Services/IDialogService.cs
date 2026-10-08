using ResolutionX.Core.Models;

namespace ResolutionX.App.Services;

/// <summary>Deixa o ViewModel abrir janelas sem conhecer as Views.</summary>
public interface IDialogService
{
    /// <summary>
    /// Pergunta "Você consegue visualizar esta tela?" no monitor que mudou.
    /// Retorna true só se o usuário confirmar dentro do prazo.
    /// </summary>
    bool ConfirmKeepResolution(MonitorInfo monitor, DisplayMode mode, int timeoutSeconds);

    void ShowDiagnostics(string report);
}
