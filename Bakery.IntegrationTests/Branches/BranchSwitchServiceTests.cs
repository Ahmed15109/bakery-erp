using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakery.Application.DTOs;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Infrastructure.Services;
using FluentAssertions;
using Xunit;

using Bakery.Application.DTOs.Accounting;
using Bakery.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Bakery.IntegrationTests;

public class BranchSwitchServiceTests : IDisposable
{
    private readonly FakeUserSessionService _userSessionService;
    private readonly FakeBranchService _branchService;
    private readonly FakeBranchContext _branchContext;
    private readonly FakeSafeContext _safeContext;
    private readonly FakeUserSafePermissionService _userSafePermissionService;
    private readonly BakeryDbContext _dbContext;
    private readonly BranchSwitchService _sut;

    public BranchSwitchServiceTests()
    {
        _userSessionService = new FakeUserSessionService();
        _branchService = new FakeBranchService();
        _branchContext = new FakeBranchContext();
        _safeContext = new FakeSafeContext();
        _userSafePermissionService = new FakeUserSafePermissionService();

        var dbName = $"BakeryERP_Test_{Guid.NewGuid():N}";
        var connectionString = $"Server=(localdb)\\mssqllocaldb;Database={dbName};Trusted_Connection=True;MultipleActiveResultSets=true";
        var options = new DbContextOptionsBuilder<BakeryDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        _dbContext = new BakeryDbContext(options);
        _dbContext.Database.EnsureCreated();

        _sut = new BranchSwitchService(
            _userSessionService,
            _branchService,
            _branchContext,
            _safeContext,
            _userSafePermissionService,
            _dbContext);
    }

    public void Dispose()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
    }

    [Fact]
    public async Task SwitchBranch_WithPermissionAndAssignedBranch_Succeeds()
    {
        // Arrange
        var branchDto = new BranchDto(2, "B2", "Branch Two", true, null);
        var currentUser = new AuthenticatedUserDto(42, "user", "User", [PermissionKeys.BranchesSwitch]);

        _userSessionService.CurrentUser = currentUser;
        _branchService.Branches.AddRange(new[]
        {
            new BranchDto(1, "B1", "Branch One", true, null),
            branchDto
        });

        // Act
        await _sut.SwitchBranchAsync(branchDto);

        // Assert
        _branchContext.CurrentBranch.Should().Be(branchDto);
    }

    [Fact]
    public async Task SwitchBranch_WithoutPermission_ThrowsUnauthorizedAccessException()
    {
        // Arrange
        var branchDto = new BranchDto(2, "B2", "Branch Two", true, null);
        var currentUser = new AuthenticatedUserDto(42, "user", "User", Array.Empty<string>());
        _userSessionService.CurrentUser = currentUser;

        // Act
        Func<Task> act = async () => await _sut.SwitchBranchAsync(branchDto);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("The current user is not allowed to switch branches.");
        _branchContext.CurrentBranch.Should().BeNull();
    }

    [Fact]
    public async Task SwitchBranch_BranchNotAssignedToUser_ThrowsInvalidOperationException()
    {
        // Arrange
        var branchDto = new BranchDto(3, "B3", "Unassigned Branch", true, null);
        var currentUser = new AuthenticatedUserDto(42, "user", "User", [PermissionKeys.BranchesSwitch]);

        _userSessionService.CurrentUser = currentUser;
        _branchService.Branches.AddRange(new[]
        {
            new BranchDto(1, "B1", "Branch One", true, null),
            new BranchDto(2, "B2", "Branch Two", true, null)
        });

        // Act
        Func<Task> act = async () => await _sut.SwitchBranchAsync(branchDto);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Requested branch is not assigned to the current user.");
        _branchContext.CurrentBranch.Should().BeNull();
    }

    private class FakeUserSessionService : IUserSessionService
    {
        public AuthenticatedUserDto? CurrentUser { get; set; }
        public bool IsAuthenticated => CurrentUser != null;
        public void SignIn(AuthenticatedUserDto user) => CurrentUser = user;
        public void SignOut() => CurrentUser = null;
        public bool HasPermission(string permissionKey) => CurrentUser?.Permissions.Contains(permissionKey) ?? false;

        public int? UserId => CurrentUser?.UserId;
        public string Username => CurrentUser?.Username ?? string.Empty;
        public string FullName => CurrentUser?.FullName ?? string.Empty;
        public IReadOnlyCollection<string> Permissions => CurrentUser?.Permissions ?? Array.Empty<string>();
        public bool IsSuperAdmin => CurrentUser?.IsSuperAdmin ?? false;
    }

    private class FakeBranchContext : IInternalBranchContext
    {
        public int? CurrentBranchId => CurrentBranch?.Id;
        public BranchDto? CurrentBranch { get; private set; }
        public void ConfigureBranch(BranchDto branch) => CurrentBranch = branch;
        public void Clear() => CurrentBranch = null;
    }

    private class FakeBranchService : IBranchService
    {
        public List<BranchDto> Branches { get; } = new();

        public Task<IReadOnlyList<BranchDto>> GetUserBranchesAsync(int userId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<BranchDto>>(Branches);
        }

        public Task<IReadOnlyList<BranchDto>> GetAllAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<BranchDto?> GetByIdAsync(int id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<BranchDto> CreateAsync(CreateBranchRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<BranchDto> UpdateAsync(UpdateBranchRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task SetActiveAsync(int id, bool isActive, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> CanDeleteAsync(int id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task DeleteAsync(int id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private class FakeSafeContext : IInternalSafeContext
    {
        public int? CurrentSafeId => CurrentSafe?.Id;
        public SafeDto? CurrentSafe { get; private set; }
        public event EventHandler<SafeChangedEventArgs>? SafeChanged;
        public void ConfigureSafe(SafeDto safe)
        {
            CurrentSafe = safe;
            SafeChanged?.Invoke(this, new SafeChangedEventArgs(safe));
        }
        public void Clear()
        {
            CurrentSafe = null;
            SafeChanged?.Invoke(this, new SafeChangedEventArgs(null));
        }
    }

    private class FakeUserSafePermissionService : IUserSafePermissionService
    {
        public Task<GetUserSafePermissionsResponse> GetUserPermissionsAsync(int userId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task UpdateUserPermissionsAsync(UpdateUserSafePermissionsRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> CanAccessSafeAsync(int userId, int safeId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> CanViewBalanceAsync(int userId, int safeId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> CanViewLedgerAsync(int userId, int safeId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> CanCashInAsync(int userId, int safeId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> CanCashOutAsync(int userId, int safeId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> CanTransferFromAsync(int userId, int safeId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> CanReceiveTransferAsync(int userId, int safeId, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
