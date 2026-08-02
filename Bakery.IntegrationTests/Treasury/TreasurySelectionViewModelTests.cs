using Bakery.Application.DTOs;
using Bakery.Application.DTOs.Accounting;
using Bakery.Application.Interfaces;
using Bakery.Domain.Entities;
using Bakery.Domain.Enums;
using Bakery.WPF.Services;
using Bakery.WPF.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentAssertions;
using Xunit;

namespace Bakery.IntegrationTests;

public sealed class TreasurySelectionViewModelTests
{
    [Fact]
    public async Task DelayedPreviousTreasuryResponse_CannotOverwriteRepeatedSelection()
    {
        var safeService = new FakeSafeService { DelayTreasuryId = 1 };
        using var viewModel = CreateViewModel(safeService);
        await safeService.DelayedRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        viewModel.SelectedTreasuryId = 2;
        safeService.ReleaseDelayedRequest.TrySetResult();
        await viewModel.Initialization.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitUntilAsync(() => viewModel.CurrentSafeBalance == 200m && !viewModel.IsLoading);

        viewModel.SelectedTreasuryId.Should().Be(2);
        viewModel.TreasurySummary!.TreasuryId.Should().Be(2);
        viewModel.CurrentSafeBalance.Should().Be(200m);
        viewModel.Transactions.Should().OnlyContain(movement => movement.TreasuryId == 2);

        viewModel.SelectedTreasuryId = 1;
        await WaitUntilAsync(() => viewModel.CurrentSafeBalance == 100m && !viewModel.IsLoading);
        viewModel.SelectedTreasuryId = 2;
        await WaitUntilAsync(() => viewModel.CurrentSafeBalance == 200m && !viewModel.IsLoading);
        viewModel.Transactions.Should().OnlyContain(movement => movement.TreasuryId == 2);
    }

    [Fact]
    public async Task TreasuryLoad_DoesNotRunDatabaseReadsConcurrently()
    {
        var safeService = new FakeSafeService
        {
            RejectConcurrentDatabaseReads = true,
            DatabaseReadDelay = TimeSpan.FromMilliseconds(50)
        };
        var messages = new RecordingMessageService();

        using var viewModel = CreateViewModel(safeService, messages: messages);
        await viewModel.Initialization.WaitAsync(TimeSpan.FromSeconds(5));

        safeService.MaxConcurrentDatabaseReads.Should().Be(1);
        viewModel.TreasurySummary.Should().NotBeNull();
        viewModel.LoadError.Should().BeEmpty();
        messages.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task FailedTreasuryLoad_ClearsPreviouslyDisplayedValues()
    {
        var safeService = new FakeSafeService();
        var messages = new RecordingMessageService();
        using var viewModel = CreateViewModel(safeService, messages: messages);
        await viewModel.Initialization.WaitAsync(TimeSpan.FromSeconds(5));
        viewModel.CurrentSafeBalance.Should().Be(100m);

        safeService.FailingTreasuryId = 2;
        viewModel.SelectedTreasuryId = 2;
        await WaitUntilAsync(() => !viewModel.IsLoading && !string.IsNullOrWhiteSpace(viewModel.LoadError));

        viewModel.SelectedTreasuryId.Should().Be(2);
        viewModel.TreasurySummary.Should().BeNull();
        viewModel.CurrentSafeBalance.Should().Be(0m);
        viewModel.TodayIncome.Should().Be(0m);
        viewModel.TodayExpenses.Should().Be(0m);
        viewModel.Transactions.Should().BeEmpty();
        messages.Errors.Should().ContainSingle(message => message.Contains("تعذر تحميل بيانات الخزينة المحددة"));
    }

    [Fact]
    public async Task PrintReport_UsesOnlySelectedTreasury()
    {
        var safeService = new FakeSafeService();
        var printService = new RecordingPrintService();
        using var viewModel = CreateViewModel(safeService, printService: printService);
        await viewModel.Initialization.WaitAsync(TimeSpan.FromSeconds(5));

        viewModel.SelectedTreasuryId = 2;
        await WaitUntilAsync(() => viewModel.CurrentSafeBalance == 200m && !viewModel.IsLoading);
        await viewModel.PrintReportCommand.ExecuteAsync(null);

        safeService.LastReportTreasuryId.Should().Be(2);
        var report = printService.LastDocument.Should().BeOfType<PdfReportRequest>().Subject;
        report.Title.Should().Contain("Treasury 2");
        report.Title.Should().NotContain("Treasury 1");

        var reportRow = report.Data.Should().ContainSingle().Which;
        var reportRowValues = reportRow.GetType()
            .GetProperties()
            .Select(property => property.GetValue(reportRow)?.ToString())
            .ToArray();
        reportRowValues.Should().Contain("TX-2");
        reportRowValues.Should().Contain("Movement 2");
        reportRowValues.Should().NotContain("TX-1");
        reportRowValues.Should().NotContain("Movement 1");
    }

    [Fact]
    public async Task ActionsAndFilters_UseSelectedTreasuryWithoutDefaultFallback()
    {
        var safeService = new FakeSafeService();
        var dialogService = new RecordingDialogService(safeService);
        using var viewModel = CreateViewModel(safeService, dialogService: dialogService);
        await viewModel.Initialization.WaitAsync(TimeSpan.FromSeconds(5));

        viewModel.SelectedTreasuryId = 2;
        await WaitUntilAsync(() => viewModel.CurrentSafeBalance == 200m && !viewModel.IsLoading);

        viewModel.SearchText = "cashier";
        viewModel.SelectedMovementTypeFilter = viewModel.MovementTypeFilters.Single(option => option.Value == SafeMovementType.Adjustment);
        await WaitUntilAsync(() => safeService.LastLedgerTreasuryId == 2 && safeService.LastSearch == "cashier" && safeService.LastMovementType == SafeMovementType.Adjustment && !viewModel.IsLoading);

        await viewModel.TransactionCommand.ExecuteAsync("Deposit");
        await viewModel.TransactionCommand.ExecuteAsync("Withdraw");
        await viewModel.TransferCommand.ExecuteAsync(null);

        dialogService.DepositTreasuryId.Should().Be(2);
        dialogService.WithdrawTreasuryId.Should().Be(2);
        dialogService.TransferSourceTreasuryId.Should().Be(2);
        safeService.LastDepositRequest!.SafeId.Should().Be(2);
        safeService.LastWithdrawalRequest!.SafeId.Should().Be(2);
        safeService.LastTransferSourceId.Should().Be(2);
        safeService.DefaultTreasuryCallCount.Should().Be(0);
    }

    private static TreasuryViewModel CreateViewModel(
        FakeSafeService safeService,
        RecordingMessageService? messages = null,
        IDialogService? dialogService = null,
        RecordingPrintService? printService = null)
    {
        var safeContext = new FakeSafeContext();
        return new TreasuryViewModel(
            safeService,
            new FakeWorkingDayService(),
            messages ?? new RecordingMessageService(),
            dialogService ?? new RecordingDialogService(safeService),
            new AllowAllPermissionService(),
            printService ?? new RecordingPrintService(),
            safeContext,
            new FakeSafeSwitchService(safeContext));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(6);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("The expected treasury state was not reached.");
            await Task.Delay(25);
        }
    }

    private sealed class FakeSafeService : ISafeService
    {
        public int? DelayTreasuryId { get; set; }
        public int? FailingTreasuryId { get; set; }
        public bool RejectConcurrentDatabaseReads { get; set; }
        public TimeSpan DatabaseReadDelay { get; set; }
        public int MaxConcurrentDatabaseReads => Volatile.Read(ref _maxConcurrentDatabaseReads);
        public TaskCompletionSource DelayedRequestStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseDelayedRequest { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int? LastLedgerTreasuryId { get; private set; }
        public int? LastReportTreasuryId { get; private set; }
        public SafeMovementType? LastMovementType { get; private set; }
        public string? LastSearch { get; private set; }
        public int DefaultTreasuryCallCount { get; private set; }
        public ManualCashTransactionRequest? LastDepositRequest { get; private set; }
        public ManualCashTransactionRequest? LastWithdrawalRequest { get; private set; }
        public int? LastTransferSourceId { get; private set; }

        private int _activeDatabaseReads;
        private int _maxConcurrentDatabaseReads;

        private readonly IReadOnlyList<SafeDto> _safes =
        [
            new(1, "Safe 1", "الخزينة الأولى", 100m, SafeType.Daily, "الفرع"),
            new(2, "Safe 2", "الخزينة الثانية", 200m, SafeType.Normal, "الفرع")
        ];

        public async Task<TreasurySnapshotDto> GetTreasurySnapshotAsync(int treasuryId, CancellationToken cancellationToken = default)
        {
            return await TrackDatabaseReadAsync(async () =>
            {
                if (DelayTreasuryId == treasuryId && !ReleaseDelayedRequest.Task.IsCompleted)
                {
                    DelayedRequestStarted.TrySetResult();
                    await ReleaseDelayedRequest.Task; // Deliberately ignores cancellation to simulate a late server response.
                }
                if (FailingTreasuryId == treasuryId) throw new InvalidOperationException("Simulated load failure");
                return Snapshot(treasuryId);
            }, cancellationToken);
        }

        public async Task<IReadOnlyList<SafeMovementDto>> GetLedgerAsync(
            int safeId,
            DateTime? startDate = null,
            DateTime? endDate = null,
            int? workingDayId = null,
            SafeMovementType? movementType = null,
            string? search = null,
            CancellationToken cancellationToken = default)
        {
            return await TrackDatabaseReadAsync(() =>
            {
                LastLedgerTreasuryId = safeId;
                LastMovementType = movementType;
                LastSearch = search;
                IReadOnlyList<SafeMovementDto> result = [Movement(safeId)];
                return Task.FromResult(result);
            }, cancellationToken);
        }

        private async Task<T> TrackDatabaseReadAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
        {
            var activeReads = Interlocked.Increment(ref _activeDatabaseReads);
            UpdateMaximumConcurrentReads(activeReads);
            try
            {
                if (RejectConcurrentDatabaseReads && activeReads > 1)
                {
                    throw new InvalidOperationException("A second database read started before the previous read completed.");
                }

                if (DatabaseReadDelay > TimeSpan.Zero)
                {
                    await Task.Delay(DatabaseReadDelay, cancellationToken);
                }

                return await operation();
            }
            finally
            {
                Interlocked.Decrement(ref _activeDatabaseReads);
            }
        }

        private void UpdateMaximumConcurrentReads(int activeReads)
        {
            var currentMaximum = Volatile.Read(ref _maxConcurrentDatabaseReads);
            while (activeReads > currentMaximum)
            {
                var observed = Interlocked.CompareExchange(ref _maxConcurrentDatabaseReads, activeReads, currentMaximum);
                if (observed == currentMaximum) return;
                currentMaximum = observed;
            }
        }

        public Task<TreasuryReportDto> GetTreasuryReportAsync(
            int treasuryId,
            DateTime? startDate = null,
            DateTime? endDate = null,
            SafeMovementType? movementType = null,
            string? search = null,
            CancellationToken cancellationToken = default)
        {
            LastReportTreasuryId = treasuryId;
            return Task.FromResult(new TreasuryReportDto(
                treasuryId,
                Snapshot(treasuryId),
                [Movement(treasuryId)],
                startDate,
                endDate,
                movementType,
                search));
        }

        public Task<IReadOnlyList<SafeDto>> ListSafesAsync(CancellationToken cancellationToken = default) => Task.FromResult(_safes);
        public Task<IReadOnlyList<SafeDto>> ListSafesForDepositAsync(CancellationToken cancellationToken = default) => Task.FromResult(_safes);
        public Task<IReadOnlyList<SafeDto>> ListSafesForWithdrawAsync(CancellationToken cancellationToken = default) => Task.FromResult(_safes);
        public Task<IReadOnlyList<SafeDto>> ListSafesForTransferSourceAsync(CancellationToken cancellationToken = default) => Task.FromResult(_safes);
        public Task<IReadOnlyList<SafeDto>> ListSafesForTransferDestAsync(CancellationToken cancellationToken = default) => Task.FromResult(_safes);
        public Task<decimal> GetBalanceAsync(int safeId, CancellationToken cancellationToken = default) => Task.FromResult(safeId * 100m);
        public Task<IReadOnlyList<SafeMovementDto>> GetMovementsAsync(int safeId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SafeMovementDto>>([Movement(safeId)]);
        public Task<Safe> GetDefaultCashSafeAsync(CancellationToken cancellationToken = default) { DefaultTreasuryCallCount++; throw new InvalidOperationException("Default treasury must not be used."); }
        public Task<int> GetDefaultSafeIdAsync(CancellationToken cancellationToken = default) { DefaultTreasuryCallCount++; throw new InvalidOperationException("Default treasury must not be used."); }
        public Task<bool> ManualDepositAsync(ManualCashTransactionRequest request, CancellationToken ct = default)
        {
            LastDepositRequest = request;
            return Task.FromResult(true);
        }
        public Task<bool> ManualWithdrawalAsync(ManualCashTransactionRequest request, CancellationToken ct = default)
        {
            LastWithdrawalRequest = request;
            return Task.FromResult(true);
        }
        public Task<bool> TransferAsync(int sourceSafeId, int destinationSafeId, decimal amount, string? notes, string? idempotencyKey = null, CancellationToken cancellationToken = default)
        {
            LastTransferSourceId = sourceSafeId;
            return Task.FromResult(true);
        }
        public Task ValidateSufficientBalanceAsync(int safeId, decimal amount, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> DepositAsync(int safeId, decimal amount, string description, SafeMovementType type = SafeMovementType.Adjustment, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> WithdrawAsync(int safeId, decimal amount, string description, SafeMovementType type = SafeMovementType.Adjustment, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> ReverseManualTransactionAsync(ReverseTransactionRequest request, CancellationToken ct = default) => Task.FromResult(true);
        public Task<IReadOnlyList<SafeManagementDto>> ListAllSafesForManagementAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SafeManagementDto> CreateSafeAsync(CreateSafeRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SafeManagementDto> UpdateSafeAsync(UpdateSafeRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> DeactivateSafeAsync(int safeId, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        private static TreasurySnapshotDto Snapshot(int treasuryId) => new(
            treasuryId,
            $"Treasury {treasuryId}",
            treasuryId == 1 ? SafeType.Daily : SafeType.Normal,
            "Branch",
            10,
            new DateOnly(2026, 7, 19),
            WorkingDayStatus.Open,
            treasuryId * 100m,
            treasuryId * 10m,
            treasuryId * 5m,
            treasuryId * 50m,
            treasuryId * 8m,
            treasuryId * 55m,
            treasuryId == 1 ? 20m : 0m,
            true,
            true,
            true,
            true,
            true);

        private static SafeMovementDto Movement(int treasuryId) => new(
            Id: treasuryId * 100,
            TreasuryId: treasuryId,
            Date: new DateTime(2026, 7, 19, 12, 0, 0),
            SafeName: $"Treasury {treasuryId}",
            Description: $"Movement {treasuryId}",
            Type: SafeMovementType.Adjustment,
            Amount: treasuryId * 10m,
            RunningBalance: treasuryId * 100m,
            ReferenceType: null,
            ReferenceId: null,
            Notes: null,
            TransferId: null,
            CounterpartSafeName: null,
            Origin: CashMovementOrigin.System,
            TransactionNumber: $"TX-{treasuryId}",
            Reason: null,
            ReasonText: null,
            IsReversed: false,
            OriginalTransactionId: null,
            CreatedBy: "User",
            ReversedBy: null,
            ReversedAt: null,
            ReverseReason: null,
            BalanceBefore: null,
            BalanceAfter: treasuryId * 100m);
    }

    private sealed class FakeSafeContext : ISafeContext
    {
        public int? CurrentSafeId => CurrentSafe?.Id;
        public SafeDto? CurrentSafe { get; private set; }
        public event EventHandler<SafeChangedEventArgs>? SafeChanged;
        public void Set(SafeDto safe)
        {
            CurrentSafe = safe;
            SafeChanged?.Invoke(this, new SafeChangedEventArgs(safe));
        }
    }

    private sealed class FakeSafeSwitchService(FakeSafeContext context) : ISafeSwitchService
    {
        public Task SwitchSafeAsync(SafeDto safe)
        {
            context.Set(safe);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingDialogService(FakeSafeService safeService) : IDialogService
    {
        public int? DepositTreasuryId { get; private set; }
        public int? WithdrawTreasuryId { get; private set; }
        public int? TransferSourceTreasuryId { get; private set; }

        public async Task<DialogResult<TViewModel>> ShowDialogAsync<TViewModel>(Func<TViewModel, Task>? initialize = null)
            where TViewModel : ObservableObject
        {
            ObservableObject instance = typeof(TViewModel) == typeof(TreasuryTransactionDialogViewModel)
                ? new TreasuryTransactionDialogViewModel(safeService, new RecordingMessageService())
                : typeof(TViewModel) == typeof(TreasuryTransferDialogViewModel)
                    ? new TreasuryTransferDialogViewModel(safeService, new RecordingMessageService())
                    : throw new InvalidOperationException($"Unexpected dialog {typeof(TViewModel).Name}");

            var typed = (TViewModel)instance;
            if (initialize is not null) await initialize(typed);

            if (instance is TreasuryTransactionDialogViewModel transaction)
            {
                if (transaction.IsDeposit) DepositTreasuryId = transaction.SelectedSafeId;
                else WithdrawTreasuryId = transaction.SelectedSafeId;
                transaction.Amount = 10m;
                transaction.SelectedReason = ManualMovementReason.OwnerCapital;
                await transaction.SaveCommand.ExecuteAsync(null);
            }
            else if (instance is TreasuryTransferDialogViewModel transfer)
            {
                TransferSourceTreasuryId = transfer.SourceSafeId;
                transfer.Amount = 10m;
                await transfer.SaveCommand.ExecuteAsync(null);
            }

            return new DialogResult<TViewModel>(false, typed);
        }

        public DialogResult<TViewModel> ShowDialog<TViewModel>(Action<TViewModel>? initialize = null)
            where TViewModel : ObservableObject => throw new NotImplementedException();
    }

    private sealed class RecordingPrintService : IReportPrintService
    {
        public object? LastDocument { get; private set; }
        public Task PrintReportAsync(object documentData, string printerName = "", bool silent = false)
        {
            LastDocument = documentData;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingMessageService : IMessageService
    {
        public List<string> Errors { get; } = [];
        public void ShowInfo(string message) { }
        public void ShowError(string message) => Errors.Add(message);
        public bool Confirm(string message) => true;
        public Task<string?> ShowInputAsync(string title, string prompt, string defaultValue = "") => Task.FromResult<string?>(null);
    }

    private sealed class AllowAllPermissionService : IPermissionService
    {
        public bool HasPermission(string key) => true;
        public bool HasAnyPermission(params string[] keys) => true;
        public void EnsurePermission(string key) { }
        public bool IsAdmin() => true;
    }

    private sealed class FakeWorkingDayService : IWorkingDayService
    {
        public Task<WorkingDayResult> OpenDayAsync(OpenWorkingDayRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkingDayResult> CloseCurrentDayAsync(CloseWorkingDayRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkingDayResult> EndCurrentDayAndOpenNextAsync(CloseWorkingDayRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkingDayCloseReadinessDto> GetEndOfDayReadinessAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkingDayResult> ReopenDayAsync(int dayId, string reason, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkingDayResult> AutoOpenIfNeededAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkingDayResult> SimplifiedCloseAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkingDay?> GetCurrentOpenDayAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkingDay> EnsureActiveWorkingDayAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkingDaySummaryDto?> GetCurrentDaySummaryAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkingDayReopenEligibilityDto> GetReopenEligibilityAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<DashboardTrendPointDto>> GetRecentDashboardTrendAsync(int days = 7, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<decimal> CalculateExpectedClosingCashAsync(int workingDayId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<(bool Match, decimal Difference, string Details)> VerifyTreasuryIntegrityAsync(int dayId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ClosingReportDto?> GetClosingReportAsync(int dayId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}
