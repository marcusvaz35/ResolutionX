namespace ResolutionX.Core.Models;

public enum OperationFailure
{
    None,
    Other,
    /// <summary>O driver não oferece a resolução pedida; ela pode ser criada como personalizada.</summary>
    ModeNotSupported,
    /// <summary>O usuário recusou o pedido de permissão de administrador.</summary>
    ElevationDeclined
}

/// <summary>
/// Resultado de uma operação no sistema. <see cref="Message"/> e <see cref="Suggestion"/> são
/// textos para o usuário; códigos e nomes de API ficam apenas em <see cref="TechnicalDetails"/>.
/// </summary>
public sealed record OperationResult(
    bool Success,
    string Message,
    string? Suggestion = null,
    string? TechnicalDetails = null,
    OperationFailure Failure = OperationFailure.None)
{
    public static OperationResult Ok(string message = "") => new(true, message);

    public static OperationResult Fail(
        string message,
        string? suggestion = null,
        string? technicalDetails = null,
        OperationFailure failure = OperationFailure.Other)
        => new(false, message, suggestion, technicalDetails, failure);
}
