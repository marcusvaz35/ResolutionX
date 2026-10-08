namespace ResolutionX.Core.Models;

/// <summary>
/// As três formas de "mudar a resolução" que o ResolutionX nunca mistura.
/// </summary>
public enum DisplayKind
{
    /// <summary>Resolução real enviada pelo driver ao monitor físico.</summary>
    Physical,

    /// <summary>Resolução de um monitor virtual criado por um Indirect Display Driver.</summary>
    Virtual,

    /// <summary>Renderização em resolução maior, reduzida depois para a resolução física.</summary>
    Scaling
}
