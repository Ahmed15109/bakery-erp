using Bakery.Application.DTOs;
using Bakery.Application.DTOs.Accounting;
using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using Bakery.WPF.Services;
using Bakery.WPF.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentAssertions;
using Xunit;

namespace Bakery.IntegrationTests;

public sealed class CloseDayDialogViewModelTests
{
    [Fact]
    public async Task CloseCommand_WhenInvokedTwiceWhileSubmitting_ShouldSendOnlyOneRequest()
    {
        var service = new BlockingWorkingDayService();
        var viewModel = new CloseDayDialogViewModel(
            service,
            new RecordingMessageService(),
            new NoOpNavigationService(),
            new StubPermissionService(PermissionKeys.WorkingDayOverrideCloseBlockers));
        await viewModel.LoadAsync();

        var firstExecution = viewModel.CloseDayCommand.ExecuteAsync(null);
        await service.CallStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var secondExecution = viewModel.CloseDayCommand.ExecuteAsync(null);

        service.Release.SetResult();
        await Task.WhenAll(firstExecution, secondExecution);

        service.CloseCallCount.Should().Be(1);
        viewModel.IsSubmitting.Should().BeFalse();
    }

    [Fact]
    public async Task OverrideControls_WithoutDedicatedPermission_RemainUnavailableAndCannotBypassBlocker()
    {
        var service = new BlockingWorkingDayService(hasBlockers: true);
        var viewModel = new CloseDayDialogViewModel(
            service,
            new RecordingMessageService(),
            new NoOpNavigationService(),
            new StubPermissionService(PermissionKeys.WorkingDayClose));

        await viewModel.LoadAsync();
        viewModel.AdminOverride = true;

        viewModel.CanOverrideCloseBlockers.Should().BeFalse();
        viewModel.AdminOverride.Should().BeFalse();
        viewModel.CloseDayCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task OverrideControls_WithDedicatedPermission_CanEnableCloseWhenBlockerExists()
    {
        var service = new BlockingWorkingDayService(hasBlockers: true);
        var viewModel = new CloseDayDialogViewModel(
            service,
            new RecordingMessageService(),
            new NoOpNavigationService(),
            new StubPermissionService(
                PermissionKeys.WorkingDayClose,
                PermissionKeys.WorkingDayOverrideCloseBlockers));

        await viewModel.LoadAsync();
        viewModel.AdminOverride = true;

        viewModel.CanOverrideCloseBlockers.Should().BeTrue();
        viewModel.AdminOverride.Should().BeTrue();
        viewModel.CloseDayCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task OpenPurchaseBlocker_NavigatesToDraftsAndSelectsExactInvoice()
    {
        var draft = new InvoiceDto(
            17,
            "0001-20260725-P",
            DateTime.UtcNow,
            "Supplier A",
            PaymentType.Credit,
            InvoiceStatus.Draft,
            100m,
            0m,
            100m);
        var postedCredit = new InvoiceDto(
            18,
            "0002-20260725-P",
            DateTime.UtcNow,
            "Supplier A",
            PaymentType.Credit,
            InvoiceStatus.Posted,
            100m,
            0m,
            100m);
        var purchaseService = new RecordingPurchaseInvoiceService([draft, postedCredit]);
        var purchasesViewModel = new PurchasesViewModel(purchaseService, new NoOpDialogService());
        var navigation = new RecordingNavigationService(purchasesViewModel);
        var blocker = new WorkingDayBlockerDto(
            WorkingDayBlockerKind.PurchaseInvoice,
            "PURCHASE_INVOICE_17",
            "Draft purchase",
            draft.Id,
            draft.InvoiceNumber,
            "عرض فواتير المشتريات المسودة");
        var service = new BlockingWorkingDayService(blockers: [blocker]);
        var viewModel = new CloseDayDialogViewModel(
            service,
            new RecordingMessageService(),
            navigation,
            new StubPermissionService(PermissionKeys.WorkingDayClose));
        await viewModel.LoadAsync();

        await viewModel.OpenBlockerCommand.ExecuteAsync(blocker);

        navigation.PurchasesNavigationCount.Should().Be(1);
        purchasesViewModel.StatusFilter.Should().Be(InvoiceStatus.Draft);
        purchasesViewModel.Invoices.Should().ContainSingle().Which.Should().Be(draft);
        purchasesViewModel.SelectedInvoice.Should().Be(draft);
        purchaseService.RequestedStatuses.Should().Equal(null, InvoiceStatus.Draft);
        purchasesViewModel.Invoices.Should().NotContain(postedCredit);
    }

    private sealed class BlockingWorkingDayService : IWorkingDayService
    {
        private readonly WorkingDaySummaryDto _current = CreateSummary(41, new DateOnly(2035, 1, 7));
        private readonly IReadOnlyList<WorkingDayBlockerDto> _blockers;
        public BlockingWorkingDayService(
            bool hasBlockers = false,
            IReadOnlyList<WorkingDayBlockerDto>? blockers = null)
        {
            _blockers = blockers ?? (hasBlockers
                ? [new WorkingDayBlockerDto(WorkingDayBlockerKind.Validation, "TEST_BLOCKER", "Test blocker")]
                : []);
        }
        public TaskCompletionSource CallStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int CloseCallCount { get; private set; }

        public Task<WorkingDayCloseReadinessDto> GetEndOfDayReadinessAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkingDayCloseReadinessDto(_current, _blockers));

        public async Task<WorkingDayResult> EndCurrentDayAndOpenNextAsync(
            CloseWorkingDayRequest request,
            CancellationToken cancellationToken = default)
        {
            CloseCallCount++;
            CallStarted.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return new WorkingDayResult(
                true,
                null,
                CreateSummary(42, _current.BusinessDate.AddDays(1)));
        }

        private static WorkingDaySummaryDto CreateSummary(int id, DateOnly date)
            => new(
                id,
                date,
                WorkingDayStatus.Open,
                100m,
                0m,
                0m,
                0m,
                0m,
                0m,
                100m,
                null,
                null,
                DailySafeBalance: 100m);

        public Task<WorkingDay?> GetCurrentOpenDayAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkingDayResult> OpenDayAsync(OpenWorkingDayRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkingDayResult> CloseCurrentDayAsync(CloseWorkingDayRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkingDayResult> AutoOpenIfNeededAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkingDayResult> SimplifiedCloseAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkingDay> EnsureActiveWorkingDayAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkingDaySummaryDto?> GetCurrentDaySummaryAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkingDayReopenEligibilityDto> GetReopenEligibilityAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<DashboardTrendPointDto>> GetRecentDashboardTrendAsync(int days = 7, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<decimal> CalculateExpectedClosingCashAsync(int workingDayId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<(bool Match, decimal Difference, string Details)> VerifyTreasuryIntegrityAsync(int dayId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<WorkingDayResult> ReopenDayAsync(int dayId, string reason, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ClosingReportDto?> GetClosingReportAsync(int dayId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class RecordingMessageService : IMessageService
    {
        public void ShowInfo(string message) { }
        public void ShowError(string message) { }
        public bool Confirm(string message) => true;
        public Task<string?> ShowInputAsync(string title, string prompt, string defaultValue = "") => Task.FromResult<string?>(null);
    }

    private sealed class NoOpNavigationService : INavigationService
    {
        public ObservableObject? CurrentViewModel => null;
        public TViewModel NavigateTo<TViewModel>() where TViewModel : ObservableObject
            => throw new InvalidOperationException("Navigation is not expected in this test.");
    }

    private sealed class RecordingNavigationService(PurchasesViewModel purchases) : INavigationService
    {
        public ObservableObject? CurrentViewModel { get; private set; }
        public int PurchasesNavigationCount { get; private set; }

        public TViewModel NavigateTo<TViewModel>() where TViewModel : ObservableObject
        {
            if (typeof(TViewModel) != typeof(PurchasesViewModel))
                throw new InvalidOperationException($"Unexpected navigation to {typeof(TViewModel).Name}.");

            PurchasesNavigationCount++;
            CurrentViewModel = purchases;
            return (TViewModel)(object)purchases;
        }
    }

    private sealed class RecordingPurchaseInvoiceService(IReadOnlyList<InvoiceDto> invoices) : IPurchaseInvoiceService
    {
        public List<InvoiceStatus?> RequestedStatuses { get; } = [];

        public Task<IReadOnlyList<InvoiceDto>> ListAsync(
            InvoiceStatus? status = null,
            CancellationToken cancellationToken = default)
        {
            RequestedStatuses.Add(status);
            IReadOnlyList<InvoiceDto> result = status.HasValue
                ? invoices.Where(invoice => invoice.Status == status.Value).ToList()
                : invoices;
            return Task.FromResult(result);
        }

        public Task<(bool Succeeded, string? ErrorMessage, int? InvoiceId)> SaveDraftAsync(
            SavePurchaseInvoiceRequest request,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<(bool Succeeded, string? ErrorMessage)> PostAsync(
            int invoiceId,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<(bool Succeeded, string? ErrorMessage)> CancelAsync(
            int invoiceId,
            string reason,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<InvoicePrintDto?> GetPrintAsync(
            int invoiceId,
            string layout,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class NoOpDialogService : IDialogService
    {
        public Task<DialogResult<TViewModel>> ShowDialogAsync<TViewModel>(
            Func<TViewModel, Task>? initialize = null) where TViewModel : ObservableObject
            => throw new NotImplementedException();

        public DialogResult<TViewModel> ShowDialog<TViewModel>(
            Action<TViewModel>? initialize = null) where TViewModel : ObservableObject
            => throw new NotImplementedException();
    }

    private sealed class StubPermissionService(params string[] permissions) : IPermissionService
    {
        private readonly HashSet<string> _permissions = permissions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        public bool HasPermission(string permissionKey) => _permissions.Contains(permissionKey);
        public void EnsurePermission(string permissionKey)
        {
            if (!HasPermission(permissionKey)) throw new UnauthorizedAccessException();
        }
        public bool IsAdmin() => false;
    }
}
