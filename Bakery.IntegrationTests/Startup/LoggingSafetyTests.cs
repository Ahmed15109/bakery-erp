using System.Text.Json;
using System.IO;
using Bakery.Application;
using Bakery.Shared.Security;
using Bakery.WPF.Logging;
using FluentAssertions;
using Serilog;
using Xunit;

namespace Bakery.IntegrationTests;

public sealed class LoggingSafetyTests
{
    [Theory]
    [InlineData("Password=TopSecret-2026;Server=local", "TopSecret-2026")]
    [InlineData("{\"ClientSecret\":\"oauth-secret-value\"}", "oauth-secret-value")]
    [InlineData("Authorization: Bearer access.token.value", "access.token.value")]
    [InlineData("refresh_token=refresh-token-value", "refresh-token-value")]
    public void Redactor_RemovesCredentialValues(string input, string sensitiveValue)
    {
        var result = SensitiveDataRedactor.Redact(input);

        result.Should().NotContain(sensitiveValue);
        result.Should().Contain(SensitiveDataRedactor.Replacement);
    }

    [Fact]
    public void FileFormatter_RedactsPropertiesMessagesAndExceptions_WhileKeepingValidJson()
    {
        var directory = Path.Combine(Path.GetTempPath(), "BakeryERP", "LoggingSafetyTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "redacted.log");
        Directory.CreateDirectory(directory);
        try
        {
            using (var logger = new LoggerConfiguration()
                .WriteTo.File(new RedactingJsonFormatter(), path, shared: true)
                .CreateLogger())
            {
                logger.Error(
                    new InvalidOperationException("Authorization: Bearer exception-token"),
                    "Provider failed with {Password} and access_token={AccessToken}",
                    "property-password",
                    "message-token");
            }

            var content = File.ReadAllText(path);
            content.Should().NotContain("property-password");
            content.Should().NotContain("message-token");
            content.Should().NotContain("exception-token");
            content.Should().Contain(SensitiveDataRedactor.Replacement);
            JsonDocument.Parse(content).RootElement.GetProperty("Properties").ValueKind
                .Should().Be(JsonValueKind.Object);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void UserErrorBoundary_HidesUnexpectedProviderText_ButKeepsExplicitBusinessErrors()
    {
        const string providerText = "Login failed; Password=should-never-reach-ui";

        UserErrorMessages.FromException(new Exception(providerText))
            .Should().Be(UserErrorMessages.Unexpected)
            .And.NotContain(providerText);
        UserErrorMessages.FromException(new InvalidOperationException("لا يمكن تعديل يوم عمل مغلق."))
            .Should().Be("لا يمكن تعديل يوم عمل مغلق.");
    }
}
