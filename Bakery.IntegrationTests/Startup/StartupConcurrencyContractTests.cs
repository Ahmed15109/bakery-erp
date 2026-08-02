using System.IO;
using FluentAssertions;

namespace Bakery.IntegrationTests;

public sealed class StartupConcurrencyContractTests
{
    [Fact]
    public void MainSessionStartup_AwaitsSharedDbContextWorkSequentially()
    {
        var repositoryRoot = FindRepositoryRoot();
        var mainSource = File.ReadAllText(Path.Combine(
            repositoryRoot, "Bakery.WPF", "ViewModels", "Shell", "MainViewModel.cs"));
        var dashboardSource = File.ReadAllText(Path.Combine(
            repositoryRoot, "Bakery.WPF", "ViewModels", "Dashboard", "DashboardViewModel.cs"));
        var appSource = File.ReadAllText(Path.Combine(repositoryRoot, "Bakery.WPF", "App.xaml.cs"));

        mainSource.Should().Contain("InitializationTask = InitializeAsync();");
        mainSource.Should().Contain("await LoadBranchCountAsync();");
        mainSource.Should().Contain("await LoadSafeCountAsync();");
        mainSource.Should().Contain("await dashboard.InitializationTask;");
        mainSource.Should().NotContain("_ = LoadBranchCountAsync();");
        mainSource.Should().NotContain("_ = LoadSafeCountAsync();");
        dashboardSource.Should().Contain("InitializationTask = RefreshAsync();");
        dashboardSource.Should().NotContain("_ = RefreshAsync();");
        appSource.Should().Contain("await mainViewModel.InitializationTask;");
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
