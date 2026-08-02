using System.IO;
using System.Text.Json;
using Bakery.WPF.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace Bakery.IntegrationTests;

public sealed class ApplicationConfigurationTests
{
    [Fact]
    public void UserConfiguration_MigratesLegacyOnceAndNeverOverwritesCustomerChanges()
    {
        var root = Path.Combine(
            Path.GetTempPath(), "BakeryERP", "ConfigurationTests", Guid.NewGuid().ToString("N"));
        var legacy = Path.Combine(root, "installed", "appsettings.json");
        var user = Path.Combine(root, "profile", "appsettings.user.json");
        Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
        const string legacyJson = """
            {
              "ConnectionStrings": {
                "BakeryDatabase": "Server=legacy;Database=Bakery;User Id=local;Password=local-only"
              },
              "GoogleDrive": { "ClientId": "legacy-id", "ClientSecret": "legacy-local-value" }
            }
            """;

        try
        {
            File.WriteAllText(legacy, legacyJson);
            var first = UserConfigurationBootstrapper.Ensure(user, legacy);
            first.MigratedLegacyConfiguration.Should().BeTrue();
            first.FilePath.Should().Be(Path.GetFullPath(user));
            JsonDocument.Parse(File.ReadAllText(user)).RootElement
                .GetProperty("GoogleDrive").GetProperty("ClientId").GetString()
                .Should().Be("legacy-id");

            const string customerChange = "{ \"CustomerMarker\": \"keep-me\" }";
            File.WriteAllText(user, customerChange);
            File.WriteAllText(legacy, "{ \"CustomerMarker\": \"overwrite-attempt\" }");

            var second = UserConfigurationBootstrapper.Ensure(user, legacy);
            second.MigratedLegacyConfiguration.Should().BeFalse();
            File.ReadAllText(user).Should().Be(customerChange);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ConfigurationValidation_RejectsUnsafeShapesWithoutEchoingValues()
    {
        var valid = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:BakeryDatabase"] =
                "Server=(localdb)\\MSSQLLocalDB;Database=BakeryERP;Trusted_Connection=True",
            ["GoogleDrive:ClientId"] = "",
            ["GoogleDrive:ClientSecret"] = ""
        }).Build();
        var validAction = () => ApplicationConfigurationValidator.Validate(valid);
        validAction.Should().NotThrow();

        const string sensitiveValue = "do-not-echo-this-value";
        var malformed = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:BakeryDatabase"] = $"invalid==;Password={sensitiveValue}"
        }).Build();
        var malformedAction = () => ApplicationConfigurationValidator.Validate(malformed);
        malformedAction.Should().Throw<InvalidOperationException>()
            .Which.ToString().Should().NotContain(sensitiveValue);

        var incompleteOAuth = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:BakeryDatabase"] = "Server=test;Database=BakeryERP;Trusted_Connection=True",
            ["GoogleDrive:ClientId"] = "configured-without-secret"
        }).Build();
        var oauthAction = () => ApplicationConfigurationValidator.Validate(incompleteOAuth);
        oauthAction.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void DeploymentSources_KeepDefaultsImmutableAndCustomerOverrideOutsideInstallerPayload()
    {
        var root = FindRepositoryRoot();
        var appSource = File.ReadAllText(Path.Combine(root, "Bakery.WPF", "App.xaml.cs"));
        var defaults = File.ReadAllText(Path.Combine(root, "Bakery.WPF", "appsettings.defaults.json"));
        var installer = File.ReadAllText(Path.Combine(root, "BakeryERP.iss"));

        appSource.Should().Contain("configuration.Sources.Clear()");
        appSource.Should().Contain("appsettings.defaults.json");
        appSource.Should().Contain("userConfigurationPath");
        appSource.Should().Contain("configuration.AddEnvironmentVariables()");
        appSource.Should().Contain("configuration.AddCommandLine(args)");
        defaults.Should().NotContain("apps.googleusercontent.com");
        using (var document = JsonDocument.Parse(defaults))
            document.RootElement.GetProperty("GoogleDrive").GetProperty("ClientSecret")
                .GetString().Should().BeEmpty();
        installer.Should().Contain("appsettings.json,appsettings.user.json");
        File.Exists(Path.Combine(root, "Bakery.WPF", "appsettings.json")).Should().BeFalse();
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
