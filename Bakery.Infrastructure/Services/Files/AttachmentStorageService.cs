using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;

namespace Bakery.Infrastructure.Services;

public sealed class AttachmentStorageService : IAttachmentStorageService
{
    private readonly IPermissionService _permissionService;
    private readonly IApplicationPathService _applicationPaths;

    public AttachmentStorageService(
        IPermissionService permissionService,
        IApplicationPathService applicationPaths)
    {
        _permissionService = permissionService;
        _applicationPaths = applicationPaths;
    }

    public async Task<string> SaveAttachmentAsync(string sourceFilePath, CancellationToken ct = default)
    {
        _permissionService.EnsureAnyPermission(PermissionKeys.CashDeposit, PermissionKeys.CashWithdraw);
        if (string.IsNullOrWhiteSpace(sourceFilePath))
        {
            return string.Empty;
        }

        if (!File.Exists(sourceFilePath))
        {
            throw new FileNotFoundException("الملف المرفق غير موجود", sourceFilePath);
        }

        var folderPath = _applicationPaths.AttachmentsDirectory;
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }

        var fileName = $"{Guid.NewGuid():N}{Path.GetExtension(sourceFilePath)}";
        var destPath = Path.Combine(folderPath, fileName);

        using (var sourceStream = new FileStream(sourceFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true))
        using (var destStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
        {
            await sourceStream.CopyToAsync(destStream, ct);
        }

        return destPath;
    }
}
