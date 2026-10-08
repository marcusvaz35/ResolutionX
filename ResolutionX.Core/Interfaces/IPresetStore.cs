using ResolutionX.Core.Models;

namespace ResolutionX.Core.Interfaces;

public interface IPresetStore
{
    /// <summary>Presets embutidos seguidos dos salvos pelo usuário.</summary>
    IReadOnlyList<ResolutionPreset> GetAll();

    /// <summary>Salva um preset do usuário. Retorna false se a mesma resolução já existir.</summary>
    bool Add(ResolutionPreset preset);

    /// <summary>Exclui um preset do usuário. Presets embutidos não são removidos.</summary>
    bool Remove(ResolutionPreset preset);
}
