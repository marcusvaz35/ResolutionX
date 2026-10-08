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

    /// <summary>Pergunta de sim/não. Retorna true se o usuário escolher "Sim".</summary>
    bool Confirm(string title, string message);

    void ShowDiagnostics(string report);
}
