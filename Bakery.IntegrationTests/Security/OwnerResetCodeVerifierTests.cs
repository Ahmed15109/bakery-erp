using Bakery.Application.Interfaces;
using Bakery.Infrastructure.Services;
using Bakery.WPF.Services;
using FluentAssertions;
using System.IO;

namespace Bakery.IntegrationTests;

public sealed class OwnerResetCodeVerifierTests
{
    [Fact]
    public void FixedOwnerCode_VerifiesCorrectWrongAndSurroundingWhitespaceInputs()
    {
        var verifier = new OwnerResetCodeVerifier();

        verifier.Verify("124578").Should().BeTrue();
        verifier.Verify("  \t124578\r\n").Should().BeTrue();
        verifier.Verify("1245780").Should().BeFalse();
        verifier.Verify(" 124 578 ").Should().BeFalse();
        verifier.Verify(null).Should().BeFalse();
    }

    [Fact]
    public void Authorization_IsOpaqueAndSingleUse()
    {
        var verifier = new OwnerResetCodeVerifier();
        var authorization = verifier.Authorize("124578");

        authorization.Should().NotBeNull();
        verifier.TryConsumeAuthorization(authorization!).Should().BeTrue();
        verifier.TryConsumeAuthorization(authorization!).Should().BeFalse();
    }

    [Fact]
    public void DialogAttemptSession_LocksAfterFiveFailures_AndNewSessionStartsFresh()
    {
        var verifier = new OwnerResetCodeVerifier();
        var session = new OwnerResetCodeAttemptSession(verifier);

        for (var attempt = 1; attempt <= OwnerResetCodeAttemptSession.MaximumFailedAttempts; attempt++)
        {
            session.TryAuthorize($"خطأ-{attempt}").Should().BeNull();
        }

        session.IsLocked.Should().BeTrue();
        session.RemainingAttempts.Should().Be(0);
        session.TryAuthorize("124578").Should().BeNull("a locked dialog session cannot be reused");

        var reopenedDialogSession = new OwnerResetCodeAttemptSession(verifier);
        reopenedDialogSession.IsLocked.Should().BeFalse();
        reopenedDialogSession.TryAuthorize("124578").Should().NotBeNull();
    }

    [Fact]
    public void PlainOwnerCodeAndDigest_AreAbsentFromUiConfigurationLoggingAndReportingSources()
    {
        var root = FindRepositoryRoot();
        var restrictedFiles = Directory.GetFiles(Path.Combine(root, "Bakery.WPF"), "*.xaml", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Path.Combine(root, "Bakery.WPF"), "*.json", SearchOption.AllDirectories))
            .Concat(Directory.GetFiles(Path.Combine(root, "Bakery.WPF", "Logging"), "*.cs", SearchOption.AllDirectories))
            .Concat(Directory.GetFiles(Path.Combine(root, "Bakery.Reporting"), "*.cs", SearchOption.AllDirectories))
            .ToArray();
        var plainCode = "124" + "578";
        var digestPrefix = "45 58 F3 3B 9A 10 1F 15";

        restrictedFiles.SelectMany(path => new[]
            {
                File.ReadAllText(path).Contains(plainCode, StringComparison.Ordinal) ? path : null,
                File.ReadAllText(path).Contains(digestPrefix, StringComparison.OrdinalIgnoreCase) ? path : null
            })
            .Where(path => path is not null)
            .Should().BeEmpty();
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "BakeryERP.sln"))) return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate BakeryERP.sln.");
    }
}
