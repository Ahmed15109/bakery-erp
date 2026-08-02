using System.Data;
using System.Text.Json;
using Bakery.Application.DTOs;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Domain.Entities;
using Bakery.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Bakery.Infrastructure.Services;

public sealed class FirstRunSetupService : IFirstRunSetupService
{
    private const string SetupLockName = "BakeryERP.FirstRunAdministrator";
    private readonly BakeryDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<FirstRunSetupService> _logger;

    public FirstRunSetupService(
        BakeryDbContext dbContext,
        IPasswordHasher passwordHasher,
        ILogger<FirstRunSetupService> logger)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public async Task<bool> IsSetupRequiredAsync(CancellationToken cancellationToken = default)
        => !await _dbContext.Users.IgnoreQueryFilters().AnyAsync(cancellationToken);

    public async Task<FirstRunSetupResult> CreateInitialAdministratorAsync(
        FirstRunAdminRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            Validate(request);
        }
        catch (InvalidOperationException exception)
        {
            return new FirstRunSetupResult(false, exception.Message);
        }

        var passwordHash = await Task.Run(
            () => _passwordHasher.HashPassword(request.Password),
            cancellationToken);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            await AcquireSetupLockAsync(transaction, cancellationToken);

            if (await _dbContext.Users.IgnoreQueryFilters().AnyAsync(cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return new FirstRunSetupResult(false, "تم إعداد مسؤول النظام بالفعل. سجّل الدخول بالحساب الموجود.");
            }

            var branch = await _dbContext.Branches.IgnoreQueryFilters()
                .SingleOrDefaultAsync(item => item.Code == "MAIN" && !item.IsDeleted, cancellationToken)
                ?? throw new InvalidOperationException("تعذر العثور على الفرع الرئيسي. أعد تشغيل التطبيق لإكمال تهيئة قاعدة البيانات.");
            var administratorRole = await _dbContext.Roles.IgnoreQueryFilters()
                .SingleOrDefaultAsync(item =>
                    item.Name == "مسؤول النظام" && item.IsSystem && !item.IsDeleted,
                    cancellationToken)
                ?? throw new InvalidOperationException("تعذر العثور على دور مسؤول النظام. أعد تشغيل التطبيق لإكمال تهيئة قاعدة البيانات.");

            var username = request.Username.Trim();
            var user = new User
            {
                Username = username,
                NormalizedUsername = username.ToUpperInvariant(),
                FullName = request.FullName.Trim(),
                PasswordHash = passwordHash,
                IsActive = true,
                IsSuperAdmin = true,
                MustChangePassword = false,
                SecurityStamp = Guid.NewGuid().ToString("N")
            };
            user.UserRoles.Add(new UserRole { RoleId = administratorRole.Id });
            user.UserBranches.Add(new UserBranch { BranchId = branch.Id });
            _dbContext.Users.Add(user);
            await _dbContext.SaveChangesAsync(cancellationToken);

            _dbContext.AuditLogs.Add(new AuditLog
            {
                BranchId = branch.Id,
                UserId = user.Id,
                Action = AuditActionKeys.FirstRunAdministratorCreated,
                EntityName = nameof(User),
                EntityId = user.Id,
                NewValues = JsonSerializer.Serialize(new
                {
                    user.Username,
                    user.FullName,
                    user.IsSuperAdmin,
                    BranchCode = branch.Code,
                    Role = administratorRole.Name
                }),
                MachineName = Environment.MachineName,
                OccurredAt = DateTime.UtcNow
            });
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new FirstRunSetupResult(true, UserId: user.Id);
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            _dbContext.ChangeTracker.Clear();
            _logger.LogError(exception, "First-run administrator creation failed");
            if (await _dbContext.Users.IgnoreQueryFilters().AnyAsync(cancellationToken))
            {
                return new FirstRunSetupResult(false, "تم إعداد مسؤول النظام بالفعل. سجّل الدخول بالحساب الموجود.");
            }

            return new FirstRunSetupResult(false, Bakery.Application.UserErrorMessages.FromException(exception));
        }
    }

    private async Task AcquireSetupLockAsync(
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        var connection = _dbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = """
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource = @resource,
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = 15000;
            SELECT @result;
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@resource";
        parameter.Value = SetupLockName;
        command.Parameters.Add(parameter);
        var result = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        if (result < 0)
        {
            throw new InvalidOperationException("تعذر تأمين عملية الإعداد الأولي. أغلق أي نسخة أخرى من التطبيق ثم حاول مرة أخرى.");
        }
    }

    private static void Validate(FirstRunAdminRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) ||
            request.Username.Trim().Length is < 3 or > 100 ||
            request.Username.Any(char.IsWhiteSpace))
        {
            throw new InvalidOperationException("اسم المستخدم يجب أن يكون من 3 إلى 100 حرف وبدون مسافات.");
        }

        if (string.IsNullOrWhiteSpace(request.FullName) || request.FullName.Trim().Length > 150)
        {
            throw new InvalidOperationException("الاسم الكامل مطلوب ويجب ألا يتجاوز 150 حرفاً.");
        }

        PasswordPolicy.EnsureValid(request.Password);
        if (!string.Equals(request.Password, request.ConfirmPassword, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("كلمتا المرور غير متطابقتين.");
        }
    }
}
