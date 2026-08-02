using System.IO;
using FluentAssertions;

namespace Bakery.IntegrationTests;

public sealed class InstallerPrerequisiteContractTests
{
    [Fact]
    public void Installer_BlocksBeforeInstallationWhenLocalDbIsMissing()
    {
        var scriptPath = FindRepositoryFile("BakeryERP.iss");
        var script = File.ReadAllText(scriptPath);

        script.Should().Contain("function IsLocalDbInstalled(): Boolean;");
        script.Should().Contain("function InitializeSetup(): Boolean;");
        script.Should().Contain("Microsoft SQL Server Express LocalDB (64-bit)");
        script.Should().Contain("لن يكتمل التثبيت قبل توفير قاعدة البيانات");
        script.Should().Contain("Result := False;");
        script.Should().Contain("https://www.microsoft.com/download/details.aspx?id=104781");
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine([current.FullName, .. segments]);
            if (File.Exists(candidate)) return candidate;
            current = current.Parent;
        }

        throw new FileNotFoundException($"Could not locate repository file: {Path.Combine(segments)}");
    }
}
