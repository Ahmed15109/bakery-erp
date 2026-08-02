namespace Bakery.Application.Security;

public static class PasswordPolicy
{
    public const int MinimumLength = 12;

    public static void EnsureValid(string? password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < MinimumLength)
        {
            throw new InvalidOperationException(
                $"يجب أن تتكون كلمة المرور من {MinimumLength} حرفاً على الأقل.");
        }
    }
}
