using FluentValidation;

namespace Bakery.Application;

/// <summary>
/// Defines the only exception messages that may cross a service/UI boundary.
/// Provider and unexpected exception details belong in the protected diagnostic log.
/// </summary>
public static class UserErrorMessages
{
    public const string Unexpected =
        "حدث خطأ غير متوقع في النظام. يرجى المحاولة مرة أخرى، وإن استمر الخطأ فتواصل مع الدعم الفني.";

    public const string DatabaseOperationFailed =
        "تعذر إكمال العملية في قاعدة البيانات. يرجى المحاولة مرة أخرى، وإن استمر الخطأ فتواصل مع الدعم الفني.";

    public static string FromException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception switch
        {
            ValidationException validation => validation.Message,
            System.ComponentModel.DataAnnotations.ValidationException validation => validation.Message,
            UnauthorizedAccessException unauthorized => unauthorized.Message,
            InvalidOperationException invalidOperation => invalidOperation.Message,
            _ => Unexpected
        };
    }
}
