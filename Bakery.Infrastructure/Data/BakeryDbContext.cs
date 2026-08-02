using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using Bakery.Domain.Interfaces;
using Bakery.Application.Interfaces;
using Bakery.Application.DTOs;
using Bakery.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Bakery.Infrastructure.Data;

public sealed class BakeryDbContext : DbContext
{
    private readonly IUserSessionService? _userSessionService;
    private readonly IInternalBranchContext? _branchContext;

    public BakeryDbContext(
        DbContextOptions<BakeryDbContext> options,
        IUserSessionService? userSessionService = null,
        IBranchContext? branchContext = null) : base(options)
    {
        _userSessionService = userSessionService;
        _branchContext = branchContext?.AsInternal();
    }

    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<UserBranch> UserBranches => Set<UserBranch>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserPermission> UserPermissions => Set<UserPermission>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<UserSafePermission> UserSafePermissions => Set<UserSafePermission>();
    public DbSet<WorkingDay> WorkingDays => Set<WorkingDay>();
    public DbSet<Party> Parties => Set<Party>();
    public DbSet<PartyLedgerEntry> PartyLedgerEntries => Set<PartyLedgerEntry>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<Unit> Units => Set<Unit>();
    public DbSet<ItemUnit> ItemUnits => Set<ItemUnit>();
    public DbSet<InventoryMovement> InventoryMovements => Set<InventoryMovement>();
    public DbSet<StockCountSession> StockCountSessions => Set<StockCountSession>();
    public DbSet<StockCountLine> StockCountLines => Set<StockCountLine>();
    public DbSet<Safe> Safes => Set<Safe>();
    public DbSet<SafeMovement> SafeMovements => Set<SafeMovement>();
    public DbSet<TransactionNumberCounter> TransactionNumberCounters => Set<TransactionNumberCounter>();
    public DbSet<PurchaseInvoice> PurchaseInvoices => Set<PurchaseInvoice>();
    public DbSet<PurchaseInvoiceLine> PurchaseInvoiceLines => Set<PurchaseInvoiceLine>();
    public DbSet<SaleInvoice> SaleInvoices => Set<SaleInvoice>();
    public DbSet<SaleInvoiceLine> SaleInvoiceLines => Set<SaleInvoiceLine>();
    public DbSet<ProductionOrder> ProductionOrders => Set<ProductionOrder>();
    public DbSet<ProductionConsumedItem> ProductionConsumedItems => Set<ProductionConsumedItem>();
    public DbSet<ProductionProducedItem> ProductionProducedItems => Set<ProductionProducedItem>();
    public DbSet<ProductionOrderEmployee> ProductionOrderEmployees => Set<ProductionOrderEmployee>();
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<RecipeItem> RecipeItems => Set<RecipeItem>();
    public DbSet<WasteEntry> WasteEntries => Set<WasteEntry>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<JobRole> JobRoles => Set<JobRole>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<EmployeeWage> EmployeeWages => Set<EmployeeWage>();
    public DbSet<Attendance> Attendances => Set<Attendance>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<PayrollPeriod> PayrollPeriods => Set<PayrollPeriod>();
    public DbSet<EmployeeSettlement> EmployeeSettlements => Set<EmployeeSettlement>();
    public DbSet<EmployeeTransaction> EmployeeTransactions => Set<EmployeeTransaction>();
    public DbSet<BackupRecord> BackupRecords => Set<BackupRecord>();

    public override int SaveChanges()
    {
        BeforeSaveChanges();
        var result = base.SaveChanges();
        AfterSave();
        return result;
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await BeforeSaveChangesAsync(cancellationToken);
        var result = await base.SaveChangesAsync(cancellationToken);
        AfterSave();
        return result;
    }

    private void BeforeSaveChanges()
    {
        var currentUser = _userSessionService?.CurrentUser?.UserName ?? "system";

        var hasBranchScopedAdded = ChangeTracker.Entries().Any(e => e.Entity is IBranchScoped && e.State == EntityState.Added);
        if (hasBranchScopedAdded)
        {
            var hasBranchInDb = Branches.IgnoreQueryFilters().Any();
            var hasBranchInTracker = ChangeTracker.Entries<Branch>().Any(e => e.State == EntityState.Added);
            if (!hasBranchInDb && !hasBranchInTracker)
            {
                var defaultBranch = new Branch
                {
                    Code = "MAIN",
                    Name = "الفرع الرئيسي",
                    IsActive = true
                };
                Branches.Add(defaultBranch);
                if (_branchContext != null && _branchContext.CurrentBranchId == null)
                {
                    _branchContext.ConfigureBranch(new BranchDto(0, defaultBranch.Code, defaultBranch.Name, defaultBranch.IsActive, defaultBranch.Notes));
                }

                foreach (var entry in ChangeTracker.Entries())
                {
                    if (entry.Entity is IBranchScoped scoped && entry.State == EntityState.Added && scoped.BranchId == 0)
                    {
                        scoped.Branch = defaultBranch;
                    }
                }
            }
        }

        int fallbackBranchId = 0;
        if (hasBranchScopedAdded && (_branchContext == null || _branchContext.CurrentBranchId == null || _branchContext.CurrentBranchId == 0))
        {
            fallbackBranchId = Branches.IgnoreQueryFilters().Select(b => b.Id).FirstOrDefault();
        }

        ProtectWorkingDaysForPendingOperations();
        ApplyAuditAndScoping(currentUser, fallbackBranchId);
    }

    private async Task BeforeSaveChangesAsync(CancellationToken cancellationToken)
    {
        var currentUser = _userSessionService?.CurrentUser?.UserName ?? "system";

        var hasBranchScopedAdded = ChangeTracker.Entries().Any(e => e.Entity is IBranchScoped && e.State == EntityState.Added);
        if (hasBranchScopedAdded)
        {
            var hasBranchInDb = await Branches.IgnoreQueryFilters().AnyAsync(cancellationToken);
            var hasBranchInTracker = ChangeTracker.Entries<Branch>().Any(e => e.State == EntityState.Added);
            if (!hasBranchInDb && !hasBranchInTracker)
            {
                var defaultBranch = new Branch
                {
                    Code = "MAIN",
                    Name = "الفرع الرئيسي",
                    IsActive = true
                };
                Branches.Add(defaultBranch);
                if (_branchContext != null && _branchContext.CurrentBranchId == null)
                {
                    _branchContext.ConfigureBranch(new BranchDto(0, defaultBranch.Code, defaultBranch.Name, defaultBranch.IsActive, defaultBranch.Notes));
                }

                foreach (var entry in ChangeTracker.Entries())
                {
                    if (entry.Entity is IBranchScoped scoped && entry.State == EntityState.Added && scoped.BranchId == 0)
                    {
                        scoped.Branch = defaultBranch;
                    }
                }
            }
        }

        int fallbackBranchId = 0;
        if (hasBranchScopedAdded && (_branchContext == null || _branchContext.CurrentBranchId == null || _branchContext.CurrentBranchId == 0))
        {
            fallbackBranchId = await Branches.IgnoreQueryFilters().Select(b => b.Id).FirstOrDefaultAsync(cancellationToken);
        }

        await ProtectWorkingDaysForPendingOperationsAsync(cancellationToken);
        ApplyAuditAndScoping(currentUser, fallbackBranchId);
    }

    private void ProtectWorkingDaysForPendingOperations()
    {
        foreach (var dayId in GetPendingOperationWorkingDayIds())
        {
            if (IsLifecycleTransition(dayId)) continue;

            var day = ChangeTracker.Entries<WorkingDay>()
                .FirstOrDefault(entry => entry.Entity.Id == dayId)?.Entity
                ?? WorkingDays.SingleOrDefault(entity => entity.Id == dayId);

            GuardOpenWorkingDay(day, dayId);
        }
    }

    private async Task ProtectWorkingDaysForPendingOperationsAsync(CancellationToken cancellationToken)
    {
        foreach (var dayId in GetPendingOperationWorkingDayIds())
        {
            if (IsLifecycleTransition(dayId)) continue;

            var day = ChangeTracker.Entries<WorkingDay>()
                .FirstOrDefault(entry => entry.Entity.Id == dayId)?.Entity
                ?? await WorkingDays.SingleOrDefaultAsync(entity => entity.Id == dayId, cancellationToken);

            GuardOpenWorkingDay(day, dayId);
        }
    }

    private IReadOnlyList<int> GetPendingOperationWorkingDayIds()
    {
        return ChangeTracker.Entries()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Where(entry => entry.Entity is not BackupRecord)
            .Where(entry => entry.Metadata.FindProperty("WorkingDayId") is not null)
            .Select(entry => entry.Property("WorkingDayId").CurrentValue)
            .OfType<int>()
            .Where(dayId => dayId > 0)
            .Distinct()
            .ToList();
    }

    private bool IsLifecycleTransition(int dayId)
    {
        var entry = ChangeTracker.Entries<WorkingDay>()
            .FirstOrDefault(candidate => candidate.Entity.Id == dayId && candidate.State == EntityState.Modified);
        if (entry is null) return false;

        var previous = entry.OriginalValues.GetValue<WorkingDayStatus>(nameof(WorkingDay.Status));
        var current = entry.Entity.Status;
        return (previous == WorkingDayStatus.Open && current == WorkingDayStatus.Closed)
            || (previous == WorkingDayStatus.Closed && current == WorkingDayStatus.Open);
    }

    private static void GuardOpenWorkingDay(WorkingDay? day, int dayId)
    {
        if (day is null || day.Status != WorkingDayStatus.Open)
        {
            throw new InvalidOperationException($"لا يمكن حفظ عملية مرتبطة بيوم العمل رقم {dayId} لأنه مغلق أو غير موجود.");
        }

        // Every operational save also updates the parent row-version. This makes
        // a concurrent close and an in-flight terminal operation mutually exclusive.
        day.UpdatedAt = DateTime.UtcNow;
    }

    private void ApplyAuditAndScoping(string currentUser, int fallbackBranchId)
    {
        var entries = ChangeTracker.Entries<BaseEntity>();

        foreach (var entry in entries)
        {
            if (entry.Entity is User user && entry.State is EntityState.Added or EntityState.Modified)
            {
                user.NormalizedUsername = user.Username.Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(user.SecurityStamp))
                {
                    user.SecurityStamp = Guid.NewGuid().ToString("N");
                }
            }
            else if (entry.Entity is Role role && entry.State is EntityState.Added or EntityState.Modified)
            {
                role.NormalizedName = role.Name.Trim().ToUpperInvariant();
            }

            switch (entry.State)
            {
                case EntityState.Added:
                    if (entry.Entity.CreatedAt == default)
                    {
                        entry.Entity.CreatedAt = DateTime.UtcNow;
                    }
                    entry.Entity.CreatedBy = currentUser;
                    entry.Entity.IsDeleted = false;

                    if (entry.Entity is IBranchScoped scoped && scoped.BranchId == 0 && scoped.Branch == null)
                    {
                        if (_branchContext?.CurrentBranchId != null && _branchContext.CurrentBranchId != 0)
                        {
                            scoped.BranchId = _branchContext.CurrentBranchId.Value;
                        }
                        else if (fallbackBranchId != 0)
                        {
                            scoped.BranchId = fallbackBranchId;
                        }
                    }
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = DateTime.UtcNow;
                    entry.Entity.UpdatedBy = currentUser;
                    if (entry.Entity.IsDeleted && entry.Entity.DeletedAt == null)
                    {
                        entry.Entity.DeletedAt = DateTime.UtcNow;
                        entry.Entity.DeletedBy = currentUser;
                    }
                    break;
                case EntityState.Deleted:
                    entry.State = EntityState.Modified;
                    entry.Entity.IsDeleted = true;
                    entry.Entity.DeletedAt = DateTime.UtcNow;
                    entry.Entity.DeletedBy = currentUser;
                    break;
            }
        }
    }

    private void AfterSave()
    {
        if (_branchContext != null && _branchContext.CurrentBranchId == 0)
        {
            var defaultBranch = ChangeTracker.Entries<Branch>().FirstOrDefault(e => e.Entity.Code == "MAIN")?.Entity;
            if (defaultBranch != null && defaultBranch.Id != 0)
            {
                _branchContext.ConfigureBranch(new BranchDto(defaultBranch.Id, defaultBranch.Code, defaultBranch.Name, defaultBranch.IsActive, defaultBranch.Notes));
            }
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BakeryDbContext).Assembly);

        // Apply Global Query Filters for IBranchScoped entities
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(IBranchScoped).IsAssignableFrom(entityType.ClrType))
            {
                var method = typeof(BakeryDbContext)
                    .GetMethod(nameof(ConfigureBranchFilter), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    ?.MakeGenericMethod(entityType.ClrType);
                method?.Invoke(this, new object[] { modelBuilder });
            }
        }
    }

    private void ConfigureBranchFilter<TEntity>(ModelBuilder modelBuilder) where TEntity : class, IBranchScoped
    {
        if (typeof(BaseEntity).IsAssignableFrom(typeof(TEntity)))
        {
            modelBuilder.Entity<TEntity>().HasQueryFilter(e => !((BaseEntity)(object)e).IsDeleted && e.BranchId == (_branchContext != null ? _branchContext.CurrentBranchId ?? 0 : 0));
        }
        else
        {
            modelBuilder.Entity<TEntity>().HasQueryFilter(e => e.BranchId == (_branchContext != null ? _branchContext.CurrentBranchId ?? 0 : 0));
        }
    }
}
