using System.IO;
using Bakery.WPF.Services;
using FluentAssertions;

namespace Bakery.IntegrationTests;

public sealed class InstallerLifecycleTests
{
    [Fact]
    public void ApplicationMutex_AllowsOnlyOneInstanceAndReleasesOnExit()
    {
        var name = $"BakeryERP.Tests.{Guid.NewGuid():N}";

        using (var first = new SingleInstanceGuard(name))
        using (var second = new SingleInstanceGuard(name))
        {
            first.IsPrimaryInstance.Should().BeTrue();
            second.IsPrimaryInstance.Should().BeFalse();
        }

        using var afterExit = new SingleInstanceGuard(name);
        afterExit.IsPrimaryInstance.Should().BeTrue();
    }

    [Fact]
    public void Installer_CoordinatesLockedApplicationAndPreservesBusinessDataOnUninstall()
    {
        var repositoryRoot = FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(repositoryRoot, "BakeryERP.iss"));
        var appSource = File.ReadAllText(Path.Combine(repositoryRoot, "Bakery.WPF", "App.xaml.cs"));
        var guardSource = File.ReadAllText(Path.Combine(
            repositoryRoot, "Bakery.WPF", "Services", "SingleInstanceGuard.cs"));

        var mutexDirective = $"AppMutex={SingleInstanceGuard.ProductionMutexName}";
        script.Should().Contain(mutexDirective);
        guardSource.Should().Contain(SingleInstanceGuard.ProductionMutexName);
        appSource.Should().Contain("new SingleInstanceGuard(SingleInstanceGuard.ProductionMutexName)");
        appSource.Should().Contain("_instanceGuard?.Dispose()");

        script.Should().Contain("CloseApplications=yes");
        script.Should().Contain("CloseApplicationsFilter={#MyAppExeName}");
        script.Should().Contain("RestartApplications=no");
        script.Should().Contain("SetupMutex=");
        script.Should().Contain("UsePreviousAppDir=yes");
        script.Should().Contain("function InitializeUninstall(): Boolean;");
        script.Should().Contain("ستبقى قاعدة البيانات والنسخ الاحتياطية والمرفقات والإعدادات");
        script.Should().Contain("appsettings.legacy-uninstall.json");
        script.Should().Contain("CopyFile(LegacyConfiguration, PreservedConfiguration, False)");
        script.Should().Contain("not ForceDirectories(PreservedConfigurationDirectory)");
        script.Should().NotContain("[UninstallDelete]");
        script.Should().NotContain("RemoveDir(ExpandConstant('{localappdata}\\BakeryERP",
            "the installer must never delete the data root");
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "BakeryERP.sln")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Bakery ERP repository root.");
    }
}
