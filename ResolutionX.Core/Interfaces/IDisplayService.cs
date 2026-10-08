using ResolutionX.Core.Models;

namespace ResolutionX.Core.Interfaces;

public interface IDisplayService
{
    /// <summary>Lê do Windows os monitores ativos, com modo atual, modos suportados, GPU e driver.</summary>
    IReadOnlyList<MonitorInfo> GetMonitors();
}
