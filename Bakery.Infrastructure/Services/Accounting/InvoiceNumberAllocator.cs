using System.Data;
using System.Globalization;
using Bakery.Application.Interfaces;
using Bakery.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bakery.Infrastructure.Services;

public sealed class InvoiceNumberAllocator : IInvoiceNumberAllocator
{
    private const int LockTimeoutMilliseconds = 15_000;
    private readonly BakeryDbContext _dbContext;

    public InvoiceNumberAllocator(BakeryDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<string> AllocateSaleNumberAsync(
        int branchId,
        DateOnly businessDate,
        CancellationToken cancellationToken = default)
        => AllocateAsync(branchId, businessDate, "S", cancellationToken);

    public Task<string> AllocatePurchaseNumberAsync(
        int branchId,
        DateOnly businessDate,
        CancellationToken cancellationToken = default)
        => AllocateAsync(branchId, businessDate, "P", cancellationToken);

    private async Task<string> AllocateAsync(
        int branchId,
        DateOnly businessDate,
        string documentPrefix,
        CancellationToken cancellationToken)
    {
        if (branchId <= 0) throw new InvalidOperationException("Active branch is required for invoice numbering.");
        var transaction = _dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException("Invoice number allocation requires an active database transaction.");
        var datePart = businessDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var counterPrefix = $"INVOICE:{documentPrefix}:{datePart}";
        await AcquireLockAsync(
            transaction,
            $"BakeryERP:InvoiceNumber:{branchId}:{documentPrefix}:{datePart}",
            cancellationToken);

        var displayPrefix = $"{documentPrefix}-{datePart}-";
        var legacyMaximum = await GetExistingMaximumAsync(
            branchId, displayPrefix, documentPrefix, cancellationToken);
        var nextValue = await IncrementCounterAsync(
            transaction,
            branchId,
            counterPrefix,
            legacyMaximum,
            cancellationToken);
        return $"{displayPrefix}{nextValue:D4}";
    }

    private async Task<int> GetExistingMaximumAsync(
        int branchId,
        string displayPrefix,
        string documentPrefix,
        CancellationToken cancellationToken)
    {
        var numbers = documentPrefix == "S"
            ? await _dbContext.SaleInvoices.IgnoreQueryFilters()
                .Where(invoice => invoice.BranchId == branchId && invoice.InvoiceNumber.StartsWith(displayPrefix))
                .Select(invoice => invoice.InvoiceNumber)
                .ToListAsync(cancellationToken)
            : await _dbContext.PurchaseInvoices.IgnoreQueryFilters()
                .Where(invoice => invoice.BranchId == branchId && invoice.InvoiceNumber.StartsWith(displayPrefix))
                .Select(invoice => invoice.InvoiceNumber)
                .ToListAsync(cancellationToken);

        var maximum = 0;
        foreach (var number in numbers)
        {
            if (number.Length > displayPrefix.Length &&
                int.TryParse(
                    number.AsSpan(displayPrefix.Length),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var value))
            {
                maximum = Math.Max(maximum, value);
            }
        }
        return maximum;
    }

    private async Task AcquireLockAsync(
        IDbContextTransaction transaction,
        string resource,
        CancellationToken cancellationToken)
    {
        var connection = _dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = """
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource = @resource,
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = @timeout;
            SELECT @result;
            """;
        var resourceParameter = command.CreateParameter();
        resourceParameter.ParameterName = "@resource";
        resourceParameter.Value = resource;
        command.Parameters.Add(resourceParameter);
        var timeoutParameter = command.CreateParameter();
        timeoutParameter.ParameterName = "@timeout";
        timeoutParameter.Value = LockTimeoutMilliseconds;
        command.Parameters.Add(timeoutParameter);
        var result = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        if (result < 0)
            throw new TimeoutException("Invoice number allocation is busy. Try again.");
    }

    private async Task<int> IncrementCounterAsync(
        IDbContextTransaction transaction,
        int branchId,
        string counterPrefix,
        int legacyMaximum,
        CancellationToken cancellationToken)
    {
        var connection = _dbContext.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = """
            SET NOCOUNT ON;
            DECLARE @allocated table ([Value] int NOT NULL);

            UPDATE dbo.TransactionNumberCounters
            SET LastValue = CASE
                    WHEN LastValue < @legacyMaximum THEN @legacyMaximum + 1
                    ELSE LastValue + 1
                END,
                IsDeleted = 0,
                DeletedAt = NULL,
                DeletedBy = NULL,
                UpdatedAt = SYSUTCDATETIME()
            OUTPUT inserted.LastValue INTO @allocated ([Value])
            WHERE BranchId = @branchId AND Prefix = @prefix;

            IF @@ROWCOUNT = 0
            BEGIN
                INSERT INTO dbo.TransactionNumberCounters
                    (BranchId, Prefix, LastValue, CreatedAt, IsDeleted)
                OUTPUT inserted.LastValue INTO @allocated ([Value])
                VALUES
                    (@branchId, @prefix, @legacyMaximum + 1, SYSUTCDATETIME(), 0);
            END;

            SELECT TOP (1) [Value] FROM @allocated;
            """;
        AddParameter(command, "@branchId", branchId);
        AddParameter(command, "@prefix", counterPrefix);
        AddParameter(command, "@legacyMaximum", legacyMaximum);
        return Convert.ToInt32(
            await command.ExecuteScalarAsync(cancellationToken),
            CultureInfo.InvariantCulture);
    }

    private static void AddParameter(
        System.Data.Common.DbCommand command,
        string name,
        object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
