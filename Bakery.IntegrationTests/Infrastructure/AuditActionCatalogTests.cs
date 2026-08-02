using System.Reflection;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Bakery.Application.Interfaces;
using Bakery.Infrastructure.Data;
using Bakery.Shared.Auditing;
using Bakery.Shared.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bakery.IntegrationTests;

public sealed class AuditActionCatalogTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public AuditActionCatalogTests(DatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public void Catalog_ContainsAndLocalizesEveryPublishedActionKey()
    {
        var publishedValues = typeof(AuditActionKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        publishedValues.All(AuditActionKeys.IsKnown).Should().BeTrue();
        publishedValues.All(HasLocalizedLabel).Should().BeTrue();
    }

    [Fact]
    public async Task StableActionKey_PersistsJsonDetails_AndLocalizationDoesNotChangeStoredValue()
    {
        var operationId = Guid.NewGuid();
        var details = JsonSerializer.Serialize(new
        {
            Operation = "InventoryAdjustment",
            Result = "Succeeded",
            OperationId = operationId,
            Quantity = 3.5m
        });

        using var scope = _fixture.ServiceProvider.CreateScope();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();
        var db = scope.ServiceProvider.GetRequiredService<BakeryDbContext>();
        await audit.LogAsync(
            AuditActionKeys.InventoryAdjusted,
            "AuditCatalogProbe",
            newValue: details);

        var persisted = await db.AuditLogs.AsNoTracking()
            .SingleAsync(entry => entry.EntityName == "AuditCatalogProbe" &&
                entry.NewValues!.Contains(operationId.ToString()));
        persisted.Action.Should().Be(AuditActionKeys.InventoryAdjusted);
        using var parsed = JsonDocument.Parse(persisted.NewValues!);
        parsed.RootElement.GetProperty("OperationId").GetGuid().Should().Be(operationId);
        parsed.RootElement.GetProperty("Quantity").GetDecimal().Should().Be(3.5m);
        Loc.LocalizeAuditAction(persisted.Action).Should().Be("تسوية مخزنية");
        persisted.Action.Should().NotBe(Loc.LocalizeAuditAction(persisted.Action));
    }

    [Fact]
    public async Task AuditService_RejectsFreeFormActionIdentifiers()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditService>();

        var action = () => audit.LogAsync("نص عرض حر", "Probe");

        await action.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*AuditActionKeys*");
    }

    [Fact]
    public void InfrastructureAuditWrites_UseTheCentralCatalog()
    {
        var root = FindRepositoryRoot();
        var files = Directory.GetFiles(
                Path.Combine(root, "Bakery.Infrastructure"),
                "*.cs",
                SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
        var literalAction = new Regex(
            "(?:LogAsync|TryAuditAsync|LogAuditAsync)\\(\\s*\\\"|Action\\s*=\\s*\\\"",
            RegexOptions.CultureInvariant);

        files.SelectMany(path => literalAction.Matches(File.ReadAllText(path)).Cast<Match>()
                .Select(match => $"{path}: {match.Value}"))
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

    private static bool HasLocalizedLabel(string action) =>
        AuditActionArabicLocalizer.TryGet(action, out var label) && !string.IsNullOrWhiteSpace(label);
}
