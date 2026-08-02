using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace Bakery.WPF.Services;

public sealed record UserConfigurationPreparation(
    string FilePath,
    bool MigratedLegacyConfiguration);

public static class UserConfigurationBootstrapper
{
    public static UserConfigurationPreparation Ensure(
        string userConfigurationPath,
        string legacyInstalledConfigurationPath)
    {
        var destination = Path.GetFullPath(userConfigurationPath);
        if (File.Exists(destination))
        {
            ValidateJson(destination);
            return new UserConfigurationPreparation(destination, false);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var migrateLegacy = File.Exists(legacyInstalledConfigurationPath);
        var content = migrateLegacy
            ? File.ReadAllBytes(legacyInstalledConfigurationPath)
            : "{}"u8.ToArray();
        var temporaryPath = destination + ".new-" + Guid.NewGuid().ToString("N");
        try
        {
            using (JsonDocument.Parse(content)) { }
            File.WriteAllBytes(temporaryPath, content);
            File.Move(temporaryPath, destination, overwrite: false);
            return new UserConfigurationPreparation(destination, migrateLegacy);
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(content);
        }
    }

    private static void ValidateJson(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var _ = JsonDocument.Parse(stream);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}

public static class ApplicationConfigurationValidator
{
    public static void Validate(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("BakeryDatabase");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "إعداد اتصال قاعدة البيانات BakeryDatabase غير موجود في إعدادات التطبيق.");

        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            if (string.IsNullOrWhiteSpace(builder.DataSource) ||
                string.IsNullOrWhiteSpace(builder.InitialCatalog))
            {
                throw new InvalidOperationException(
                    "إعداد اتصال قاعدة البيانات يجب أن يحدد الخادم واسم قاعدة البيانات.");
            }
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException(
                "صيغة إعداد اتصال قاعدة البيانات غير صالحة. راجع ملف إعدادات المستخدم.");
        }

        var googleClientId = configuration["GoogleDrive:ClientId"];
        var googleClientSecret = configuration["GoogleDrive:ClientSecret"];
        if (string.IsNullOrWhiteSpace(googleClientId) != string.IsNullOrWhiteSpace(googleClientSecret))
        {
            throw new InvalidOperationException(
                "إعداد Google Drive غير مكتمل. يجب توفير ClientId وClientSecret معاً أو تركهما فارغين.");
        }
    }
}
