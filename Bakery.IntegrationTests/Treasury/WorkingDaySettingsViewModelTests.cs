using Bakery.Application.DTOs;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using Bakery.Application.DTOs.Accounting;
using Bakery.WPF.Services;
using Bakery.WPF.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentAssertions;

namespace Bakery.IntegrationTests;

public sealed class WorkingDaySettingsViewModelTests
{
    [Fact]
    public void PermissionCatalog_UsesOneCanonicalReopenPermission_WithViewDependency()
    {
        PermissionKeys.WorkingDayReopen.Should().Be("WorkingDay.Reopen");
        PermissionCatalog.All.Count(item =>
            item.Key.Equals(PermissionKeys.WorkingDayReopen, StringComparison.OrdinalIgnoreCase)).Should().Be(1);
        var keys = PermissionCatalog.All.Select(item => item.Key).ToArray();
        keys.Distinct(StringComparer.OrdinalIgnoreCase).Should().HaveCount(keys.Length);
        PermissionPolicyCatalog.GetRequiredParents(PermissionKeys.WorkingDayReopen)
            .Should().BeEquivalentTo([PermissionKeys.WorkingDayView]);
    }

    [Fact]
    public async Task ReopenButton_IsUnavailableWithoutCanonicalPermission()
    {
        var permissions = new StubPermissionService(PermissionKeys.SettingsSystem);
        var closedDay = CreateSummary(WorkingDayStatus.Closed);
        var viewModel = CreateViewModel(
            new StubWorkingDayService(
                CreateSummary(WorkingDayStatus.Open, closedDay.BusinessDate.AddDays(1), 43),
                closedDay),
            permissions,
            new StubDialogService());

        await viewModel.InitializationTask;

        viewModel.CanUseReopenPermission.Should().BeFalse();
        viewModel.ReopenWorkingDayCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task SuccessfulSettingsReopen_UsesExactDateAndRefreshesAllOperationalConsumers()
    {
        var date = new DateOnly(2042, 6, 17);
        var closedAt = DateTime.UtcNow.AddMinutes(-10);
        var closedDay = CreateSummary(WorkingDayStatus.Closed, date, 42, closedAt, "closing-admin");
        var currentDay = CreateSummary(WorkingDayStatus.Open, date.AddDays(1), 43);
        var workingDays = new StubWorkingDayService(currentDay, closedDay);
        var dialog = new StubDialogService { ReopenReason = "تصحيح إقفال الخزينة" };
        var notifier = new OperationalContextRefreshNotifier();
        var activeDayRefreshes = 0;
        var dashboardRefreshes = 0;
        var treasuryRefreshes = 0;
        notifier.RefreshRequested += () =>
        {
            activeDayRefreshes++;
            return Task.CompletedTask;
        };
        notifier.RefreshRequested += () =>
        {
            dashboardRefreshes++;
            return Task.CompletedTask;
        };
        notifier.RefreshRequested += () =>
        {
            treasuryRefreshes++;
            return Task.CompletedTask;
        };
        var viewModel = CreateViewModel(
            workingDays,
            new StubPermissionService(PermissionKeys.SettingsSystem, PermissionKeys.WorkingDayView, PermissionKeys.WorkingDayReopen),
            dialog,
            notifier);
        await viewModel.InitializationTask;

        viewModel.WorkingDaySummary.Should().BeEquivalentTo(currentDay);
        viewModel.LastClosedWorkingDaySummary.Should().BeEquivalentTo(closedDay);
        viewModel.WorkingDayBusinessDateText.Should().Be("18/06/2042");
        viewModel.LastClosedBusinessDateText.Should().Be("17/06/2042");
        viewModel.WorkingDayClosedByText.Should().Be("closing-admin");
        viewModel.WorkingDayClosedAtText.Should().NotBe("غير متاح");
        viewModel.ReopenButtonText.Should().Be("إعادة فتح يوم 17/06/2042");
        viewModel.ReopenWorkingDayCommand.CanExecute(null).Should().BeTrue();

        await viewModel.ReopenWorkingDayCommand.ExecuteAsync(null);

        workingDays.ReopenCallCount.Should().Be(1);
        workingDays.LastReason.Should().Be("تصحيح إقفال الخزينة");
        dialog.PromptedBusinessDate.Should().Be(date);
        activeDayRefreshes.Should().Be(1);
        dashboardRefreshes.Should().Be(1);
        treasuryRefreshes.Should().Be(1);
        viewModel.WorkingDaySummary!.Status.Should().Be(WorkingDayStatus.Open);
        viewModel.WorkingDaySummary.BusinessDate.Should().Be(date);
        viewModel.WorkingDayStatusText.Should().Be("مفتوح");
    }

    [Fact]
    public async Task SuccessfulReopen_SerializesSettingsReloadAndHeaderRefreshReads()
    {
        var date = new DateOnly(2042, 6, 19);
        var workingDays = new StubWorkingDayService(
            CreateSummary(WorkingDayStatus.Open, date.AddDays(1), 71),
            CreateSummary(WorkingDayStatus.Closed, date, 70),
            readDelay: TimeSpan.FromMilliseconds(60));
        var notifier = new OperationalContextRefreshNotifier();
        var headerRefreshes = 0;
        notifier.RefreshRequested += async () =>
        {
            await workingDays.GetCurrentDaySummaryAsync();
            headerRefreshes++;
        };
        var viewModel = CreateViewModel(
            workingDays,
            new StubPermissionService(PermissionKeys.WorkingDayView, PermissionKeys.WorkingDayReopen),
            new StubDialogService { ReopenReason = "تصحيح يوم العمل بعد الإغلاق" },
            notifier);
        await viewModel.InitializationTask;

        await viewModel.ReopenWorkingDayCommand.ExecuteAsync(null);

        workingDays.ReopenCallCount.Should().Be(1);
        headerRefreshes.Should().Be(1);
        workingDays.MaxConcurrentReadOperations.Should().Be(1,
            "Settings and header refreshes share a scoped DbContext and must run sequentially");
    }

    [Fact]
    public async Task BlockedSuccessor_ShowsExactReasonAndDisablesReopen()
    {
        var date = new DateOnly(2042, 6, 20);
        const string blocker = "توجد حركات خزينة أو تحويلات مالية على يوم العمل التالي.";
        var workingDays = new StubWorkingDayService(
            CreateSummary(WorkingDayStatus.Open, date.AddDays(1), 51),
            CreateSummary(WorkingDayStatus.Closed, date, 50),
            canReopen: false,
            blockingReason: blocker);
        var viewModel = CreateViewModel(
            workingDays,
            new StubPermissionService(PermissionKeys.WorkingDayView, PermissionKeys.WorkingDayReopen),
            new StubDialogService());

        await viewModel.InitializationTask;

        viewModel.IsReopenEligible.Should().BeFalse();
        viewModel.ReopenEligibilityText.Should().Contain(blocker);
        viewModel.ReopenWorkingDayCommand.CanExecute(null).Should().BeFalse();
        workingDays.ReopenCallCount.Should().Be(0);
    }

    [Fact]
    public async Task ResolvingBlocker_RemovesIt_EnablesReopen_AndRefreshesConsumersSequentially()
    {
        var date = new DateOnly(2042, 7, 1);
        var blocker = new WorkingDayReopenBlockerDto(
            "PURCHASE:901", WorkingDayReopenBlockerKind.PurchaseInvoice, 901,
            "مسودة فاتورة مشتريات", "PUR-901", "مسودة اختبار", 100, "المبلغ",
            "admin", DateTime.UtcNow, "مسودة", "توجد مسودة على يوم العمل الحالي.",
            WorkingDayReopenActionKind.DeleteDraft, "حذف المسودة", PermissionKeys.PurchasesDelete,
            true, "حذف المسودة التي لم تُرحّل.");
        var workingDays = new StubWorkingDayService(
            CreateSummary(WorkingDayStatus.Open, date.AddDays(1), 902),
            CreateSummary(WorkingDayStatus.Closed, date, 900),
            canReopen: false,
            blockingReason: blocker.BlockingReason,
            reopenBlockers: [blocker]);
        var resolver = new StubReopenResolutionService(workingDays);
        var notifier = new OperationalContextRefreshNotifier();
        var refreshOrder = new List<string>();
        foreach (var name in new[] { "header", "dashboard", "treasury", "inventory", "parties", "production", "reports", "contexts" })
        {
            notifier.RefreshRequested += async () =>
            {
                await Task.Yield();
                refreshOrder.Add(name);
            };
        }
        var messages = new RecordingMessageService { InputText = "حذف المسودة لتصحيح يوم العمل" };
        var permissions = new StubPermissionService(
            PermissionKeys.WorkingDayView, PermissionKeys.WorkingDayReopen, PermissionKeys.PurchasesDelete);
        var viewModel = new SettingsViewModel(
            new StubResetService(), workingDays, permissions,
            new StubUserSessionService(new AuthenticatedUserDto(1, "admin", "Admin", [], true)),
            messages, new StubNavigationService(), new StubDialogService(), new NullOwnerResetPrompt(), notifier, resolver);
        await viewModel.InitializationTask;

        viewModel.ReopenBlockerCount.Should().Be(1);
        viewModel.ReopenWorkingDayCommand.CanExecute(null).Should().BeFalse();
        await viewModel.ResolveReopenBlockerCommand.ExecuteAsync(blocker);

        resolver.CallCount.Should().Be(1);
        resolver.LastReason.Should().Be("حذف المسودة لتصحيح يوم العمل");
        viewModel.ReopenBlockers.Should().BeEmpty();
        viewModel.ReopenBlockerCount.Should().Be(0);
        viewModel.IsReopenEligible.Should().BeTrue();
        viewModel.ReopenWorkingDayCommand.CanExecute(null).Should().BeTrue();
        refreshOrder.Should().ContainInOrder("header", "dashboard", "treasury", "inventory", "parties", "production", "reports", "contexts");
    }

    [Fact]
    public async Task BlockerNavigation_OpensPurchasesAndSelectsExactDraft()
    {
        var invoice = new InvoiceDto(
            901, "PUR-901", DateTime.UtcNow, "Supplier", PaymentType.Credit,
            InvoiceStatus.Draft, 100, 0, 100);
        var purchases = new PurchasesViewModel(new StubPurchaseInvoiceService(invoice), new StubDialogService());
        var navigation = new StubNavigationService { NextViewModel = purchases };
        var blocker = new WorkingDayReopenBlockerDto(
            "PURCHASE:901", WorkingDayReopenBlockerKind.PurchaseInvoice, 901,
            "مسودة فاتورة مشتريات", "PUR-901", "مسودة اختبار", 100, "المبلغ",
            "admin", DateTime.UtcNow, "مسودة", "توجد مسودة على يوم العمل الحالي.",
            WorkingDayReopenActionKind.DeleteDraft, "حذف المسودة", PermissionKeys.PurchasesDelete,
            true, "حذف المسودة التي لم تُرحّل.");
        var workingDays = new StubWorkingDayService(
            CreateSummary(WorkingDayStatus.Open, new DateOnly(2042, 7, 3), 902),
            CreateSummary(WorkingDayStatus.Closed, new DateOnly(2042, 7, 2), 900),
            false, blocker.BlockingReason, reopenBlockers: [blocker]);
        var viewModel = new SettingsViewModel(
            new StubResetService(), workingDays,
            new StubPermissionService(PermissionKeys.WorkingDayView, PermissionKeys.WorkingDayReopen, PermissionKeys.PurchasesView),
            new StubUserSessionService(new AuthenticatedUserDto(1, "admin", "Admin", [], true)),
            new RecordingMessageService(), navigation, new StubDialogService(), new NullOwnerResetPrompt(),
            new OperationalContextRefreshNotifier());
        await viewModel.InitializationTask;

        await viewModel.OpenReopenBlockerCommand.ExecuteAsync(blocker);

        navigation.LastViewModelType.Should().Be(typeof(PurchasesViewModel));
        purchases.StatusFilter.Should().BeNull();
        purchases.SelectedInvoice.Should().NotBeNull();
        purchases.SelectedInvoice!.Id.Should().Be(901);
        purchases.SelectedInvoice.Status.Should().Be(InvoiceStatus.Draft);
    }

    [Fact]
    public async Task FactoryResetCommand_DoubleClickExecutesResetAtMostOnce()
    {
        var reset = new CountingResetService();
        var permissions = new StubPermissionService(PermissionKeys.SettingsSystem, PermissionKeys.SettingsResetSystem);
        var session = new StubUserSessionService(new AuthenticatedUserDto(
            1,
            "admin",
            "Admin",
            [PermissionKeys.SettingsSystem, PermissionKeys.SettingsResetSystem],
            true));
        var viewModel = new SettingsViewModel(
            reset,
            new StubWorkingDayService(CreateSummary(WorkingDayStatus.Open), null),
            permissions,
            session,
            new RecordingMessageService(),
            new StubNavigationService(),
            new StubDialogService(),
            new AuthorizedOwnerResetPrompt(),
            new OperationalContextRefreshNotifier())
        {
            ConfirmationText = "DELETE"
        };
        await viewModel.InitializationTask;

        var firstClick = viewModel.FactoryResetCommand.ExecuteAsync(null);
        var secondClick = viewModel.FactoryResetCommand.ExecuteAsync(null);
        await Task.WhenAll(firstClick, secondClick);

        reset.CallCount.Should().Be(1);
        viewModel.ConfirmationText.Should().BeEmpty();
    }

    private static SettingsViewModel CreateViewModel(
        IWorkingDayService workingDayService,
        IPermissionService permissionService,
        IDialogService dialogService,
        IOperationalContextRefreshNotifier? notifier = null)
    {
        var user = new AuthenticatedUserDto(
            1,
            "admin",
            "Admin",
            [PermissionKeys.SettingsSystem, PermissionKeys.WorkingDayView, PermissionKeys.WorkingDayReopen],
            true);
        return new SettingsViewModel(
            new StubResetService(),
            workingDayService,
            permissionService,
            new StubUserSessionService(user),
            new RecordingMessageService(),
            new StubNavigationService(),
            dialogService,
            new NullOwnerResetPrompt(),
            notifier ?? new OperationalContextRefreshNotifier());
    }

    private static WorkingDaySummaryDto CreateSummary(
        WorkingDayStatus status,
        DateOnly? date = null,
        int workingDayId = 42,
        DateTime? closedAt = null,
        string? closedBy = null)
        => new(
            WorkingDayId: workingDayId,
            BusinessDate: date ?? new DateOnly(2042, 6, 16),
            Status: status,
            OpeningCash: 0,
            TotalSales: 0,
            TotalPurchases: 0,
            Expenses: 0,
            Wages: 0,
            SafeTransfers: 0,
            ExpectedCash: 0,
            ActualCash: 0,
            CashDifference: 0,
            LastClosedAt: status == WorkingDayStatus.Closed ? closedAt ?? DateTime.UtcNow.AddMinutes(-10) : null,
            LastClosedBy: status == WorkingDayStatus.Closed ? closedBy ?? "admin" : null);

    private sealed class StubWorkingDayService(
        WorkingDaySummaryDto? currentDay,
        WorkingDaySummaryDto? lastClosedDay,
        bool canReopen = true,
        string? blockingReason = null,
        TimeSpan? readDelay = null,
        IReadOnlyList<WorkingDayReopenBlockerDto>? reopenBlockers = null) : IWorkingDayService
    {
        private WorkingDaySummaryDto? _currentDay = currentDay;
        private WorkingDaySummaryDto? _lastClosedDay = lastClosedDay;
        private bool _canReopen = canReopen;
        private readonly string? _blockingReason = blockingReason;
        private readonly TimeSpan _readDelay = readDelay ?? TimeSpan.Zero;
        private IReadOnlyList<WorkingDayReopenBlockerDto> _reopenBlockers = reopenBlockers ?? [];
        private int _activeReadOperations;
        private int _maxConcurrentReadOperations;
        public int ReopenCallCount { get; private set; }
        public string? LastReason { get; private set; }
        public int MaxConcurrentReadOperations => Volatile.Read(ref _maxConcurrentReadOperations);

        public Task<WorkingDaySummaryDto?> GetCurrentDaySummaryAsync(CancellationToken cancellationToken = default)
            => TrackReadAsync(() => _currentDay, cancellationToken);

        public Task<WorkingDayReopenEligibilityDto> GetReopenEligibilityAsync(CancellationToken cancellationToken = default)
            => TrackReadAsync(BuildEligibility, cancellationToken);

        private WorkingDayReopenEligibilityDto BuildEligibility()
        {
            IReadOnlyList<string> reasons = _blockingReason is null ? [] : [_blockingReason];
            var status = _lastClosedDay is null
                ? "لا يوجد يوم عمل مغلق متاح لإعادة الفتح"
                : _canReopen
                    ? "متاح لإعادة الفتح."
                    : $"إعادة الفتح غير متاحة: {_blockingReason}";
            return new WorkingDayReopenEligibilityDto(
                _currentDay,
                _lastClosedDay,
                _canReopen && _lastClosedDay is not null,
                status,
                reasons,
                _reopenBlockers);
        }

        public void ResolveBlocker(string code)
        {
            _reopenBlockers = _reopenBlockers.Where(item => item.Code != code).ToArray();
            if (_reopenBlockers.Count == 0)
            {
                _canReopen = true;
            }
        }

        private async Task<T> TrackReadAsync<T>(Func<T> read, CancellationToken cancellationToken)
        {
            var active = Interlocked.Increment(ref _activeReadOperations);
            while (true)
            {
                var observed = Volatile.Read(ref _maxConcurrentReadOperations);
                if (active <= observed ||
                    Interlocked.CompareExchange(ref _maxConcurrentReadOperations, active, observed) == observed)
                {
                    break;
                }
            }

            try
            {
                if (_readDelay > TimeSpan.Zero)
                    await Task.Delay(_readDelay, cancellationToken);
                return read();
            }
            finally
            {
                Interlocked.Decrement(ref _activeReadOperations);
            }
        }

        public Task<WorkingDayResult> ReopenDayAsync(int dayId, string reason, CancellationToken cancellationToken = default)
        {
            ReopenCallCount++;
            LastReason = reason;
            var reopened = _lastClosedDay! with
            {
                Status = WorkingDayStatus.Open,
                ReopenReason = reason,
                LastClosedAt = _lastClosedDay.LastClosedAt,
                LastClosedBy = _lastClosedDay.LastClosedBy
            };
            _currentDay = reopened;
            _lastClosedDay = null;
            _canReopen = false;
            return Task.FromResult(new WorkingDayResult(true, null, reopened));
        }

        public Task<WorkingDay?> GetCurrentOpenDayAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkingDayResult> OpenDayAsync(OpenWorkingDayRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkingDayResult> CloseCurrentDayAsync(CloseWorkingDayRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkingDayResult> EndCurrentDayAndOpenNextAsync(CloseWorkingDayRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkingDayCloseReadinessDto> GetEndOfDayReadinessAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkingDayResult> AutoOpenIfNeededAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkingDayResult> SimplifiedCloseAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkingDay> EnsureActiveWorkingDayAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<DashboardTrendPointDto>> GetRecentDashboardTrendAsync(int days = 7, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<decimal> CalculateExpectedClosingCashAsync(int workingDayId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<(bool Match, decimal Difference, string Details)> VerifyTreasuryIntegrityAsync(int dayId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ClosingReportDto?> GetClosingReportAsync(int dayId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class StubPermissionService(params string[] permissions) : IPermissionService
    {
        private readonly HashSet<string> _permissions = permissions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        public bool HasPermission(string permissionKey) => _permissions.Contains(permissionKey);
        public void EnsurePermission(string permissionKey)
        {
            if (!HasPermission(permissionKey)) throw new UnauthorizedAccessException();
        }
        public bool IsAdmin() => true;
    }

    private sealed class StubDialogService : IDialogService
    {
        public string ReopenReason { get; init; } = "تصحيح إداري";
        public DateOnly? PromptedBusinessDate { get; private set; }

        public async Task<DialogResult<TViewModel>> ShowDialogAsync<TViewModel>(Func<TViewModel, Task>? initialize = null)
            where TViewModel : ObservableObject
        {
            if (typeof(TViewModel) != typeof(ReopenWorkingDayDialogViewModel)) throw new NotSupportedException();
            var viewModel = new ReopenWorkingDayDialogViewModel();
            if (initialize is not null) await initialize((TViewModel)(object)viewModel);
            PromptedBusinessDate = viewModel.BusinessDate;
            viewModel.Reason = ReopenReason;
            return (DialogResult<TViewModel>)(object)new DialogResult<ReopenWorkingDayDialogViewModel>(true, viewModel);
        }

        public DialogResult<TViewModel> ShowDialog<TViewModel>(Action<TViewModel>? initialize = null)
            where TViewModel : ObservableObject => throw new NotSupportedException();
    }

    private sealed class StubResetService : ISystemResetService
    {
        public Task ResetTransactionalDataAsync(IOwnerResetAuthorization authorization, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }

    private sealed class CountingResetService : ISystemResetService
    {
        public int CallCount { get; private set; }

        public async Task ResetTransactionalDataAsync(
            IOwnerResetAuthorization authorization,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            await Task.Delay(50, cancellationToken);
        }
    }

    private sealed class StubUserSessionService(AuthenticatedUserDto user) : IUserSessionService
    {
        public AuthenticatedUserDto? CurrentUser { get; private set; } = user;
        public int? UserId => CurrentUser?.UserId;
        public string Username => CurrentUser?.Username ?? string.Empty;
        public string FullName => CurrentUser?.FullName ?? string.Empty;
        public IReadOnlyCollection<string> Permissions => CurrentUser?.Permissions ?? [];
        public bool IsAuthenticated => CurrentUser is not null;
        public bool IsSuperAdmin => CurrentUser?.IsSuperAdmin == true;
        public void SignIn(AuthenticatedUserDto authenticatedUser) => CurrentUser = authenticatedUser;
        public void SignOut() => CurrentUser = null;
        public bool HasPermission(string permissionKey) => IsSuperAdmin || Permissions.Contains(permissionKey);
    }

    private sealed class RecordingMessageService : IMessageService
    {
        public string? InputText { get; init; }
        public void ShowInfo(string message) { }
        public void ShowError(string message) { }
        public bool Confirm(string message) => true;
        public Task<string?> ShowInputAsync(string title, string prompt, string defaultValue = "") => Task.FromResult(InputText);
    }

    private sealed class StubReopenResolutionService(StubWorkingDayService workingDays) : IWorkingDayReopenResolutionService
    {
        public int CallCount { get; private set; }
        public string? LastReason { get; private set; }

        public async Task<WorkingDayReopenBlockerResolutionResult> ResolveAsync(
            ResolveWorkingDayReopenBlockerRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastReason = request.Reason;
            workingDays.ResolveBlocker(request.BlockerCode);
            var eligibility = await workingDays.GetReopenEligibilityAsync(cancellationToken);
            return new WorkingDayReopenBlockerResolutionResult(true, null, eligibility);
        }
    }

    private sealed class StubNavigationService : INavigationService
    {
        public ObservableObject? CurrentViewModel => null;
        public ObservableObject? NextViewModel { get; init; }
        public Type? LastViewModelType { get; private set; }
        public TViewModel NavigateTo<TViewModel>() where TViewModel : ObservableObject
        {
            LastViewModelType = typeof(TViewModel);
            return NextViewModel is TViewModel viewModel ? viewModel : null!;
        }
    }

    private sealed class StubPurchaseInvoiceService(params InvoiceDto[] invoices) : IPurchaseInvoiceService
    {
        public Task<IReadOnlyList<InvoiceDto>> ListAsync(InvoiceStatus? status = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<InvoiceDto>>(invoices.Where(item => !status.HasValue || item.Status == status).ToArray());
        public Task<(bool Succeeded, string? ErrorMessage, int? InvoiceId)> SaveDraftAsync(SavePurchaseInvoiceRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<(bool Succeeded, string? ErrorMessage)> PostAsync(int invoiceId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<(bool Succeeded, string? ErrorMessage)> CancelAsync(int invoiceId, string reason, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<InvoicePrintDto?> GetPrintAsync(int invoiceId, string layout, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class NullOwnerResetPrompt : IOwnerResetAuthorizationPrompt
    {
        public Task<IOwnerResetAuthorization?> RequestAuthorizationAsync()
            => Task.FromResult<IOwnerResetAuthorization?>(null);
    }


    private sealed class AuthorizedOwnerResetPrompt : IOwnerResetAuthorizationPrompt
    {
        public Task<IOwnerResetAuthorization?> RequestAuthorizationAsync()
            => Task.FromResult<IOwnerResetAuthorization?>(new TestOwnerResetAuthorization());
    }

    private sealed class TestOwnerResetAuthorization : IOwnerResetAuthorization;
}
