namespace ResolutionX.Core.Models;

/// <summary>
/// Resultado de uma operação no sistema. <see cref="Message"/> e <see cref="Suggestion"/> são
/// textos para o usuário; códigos e nomes de API ficam apenas em <see cref="TechnicalDetails"/>.
/// </summary>
public sealed record OperationResult(
    bool Success,
    string Message,
    string? Suggestion = null,
    string? TechnicalDetails = null)
{
    public static OperationResult Ok(string message = "") => new(true, message);

    public static OperationResult Fail(string message, string? suggestion = null, string? technicalDetails = null)
        => new(false, message, suggestion, technicalDetails);
}
