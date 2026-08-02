using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using Bakery.WPF;
using Bakery.WPF.ViewModels;
using Bakery.WPF.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace Bakery.WPF.Services;

public sealed class DialogService : IDialogService
{
    private readonly IServiceProvider _serviceProvider;

    private readonly Dictionary<Type, Type> _mappings = new()
    {
        { typeof(EmployeeLedgerViewModel), typeof(EmployeeLedgerView) },
        { typeof(UserFormDialogViewModel), typeof(UserFormDialog) },
        { typeof(ResetPasswordDialogViewModel), typeof(ResetPasswordDialog) },
        { typeof(ChangePasswordDialogViewModel), typeof(ChangePasswordDialog) },
        { typeof(RoleFormDialogViewModel), typeof(RoleFormDialog) },
        { typeof(ItemFormDialogViewModel), typeof(ItemFormDialog) },
        { typeof(InventoryAdjustmentDialogViewModel), typeof(InventoryAdjustmentDialog) },
        { typeof(PartyPaymentDialogViewModel), typeof(PartyPaymentDialog) },
        { typeof(SaleInvoiceDialogViewModel), typeof(SaleInvoiceDialog) },
        { typeof(PurchaseInvoiceDialogViewModel), typeof(PurchaseInvoiceDialog) },
        { typeof(TreasuryTransactionDialogViewModel), typeof(TreasuryTransactionDialog) },
        { typeof(TreasuryTransferDialogViewModel), typeof(TreasuryTransferDialog) },
        { typeof(ReverseTransactionDialogViewModel), typeof(ReverseTransactionDialog) },
        { typeof(SafeManagementDialogViewModel), typeof(SafeManagementDialog) },
        { typeof(SafeFormDialogViewModel), typeof(SafeFormDialog) },
        { typeof(CloseDayDialogViewModel), typeof(CloseDayDialog) },
        { typeof(ReopenWorkingDayDialogViewModel), typeof(ReopenWorkingDayDialog) },
        { typeof(BranchFormDialogViewModel), typeof(BranchFormDialog) },
        { typeof(BranchSelectionDialogViewModel), typeof(BranchSelectionDialog) },
        { typeof(SafeSelectionDialogViewModel), typeof(SafeSelectionDialog) }
    };

    public DialogService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task<DialogResult<TViewModel>> ShowDialogAsync<TViewModel>(Func<TViewModel, Task>? initialize = null) where TViewModel : ObservableObject
    {
        var (window, viewModel) = CreateDialog<TViewModel>();
        
        if (initialize != null)
        {
            await initialize(viewModel);
        }

        var result = window.ShowDialog();
        return new DialogResult<TViewModel>(result, viewModel);
    }

    public DialogResult<TViewModel> ShowDialog<TViewModel>(Action<TViewModel>? initialize = null) where TViewModel : ObservableObject
    {
        var (window, viewModel) = CreateDialog<TViewModel>();

        initialize?.Invoke(viewModel);

        var result = window.ShowDialog();
        return new DialogResult<TViewModel>(result, viewModel);
    }

    private (Window Window, TViewModel ViewModel) CreateDialog<TViewModel>() where TViewModel : ObservableObject
    {
        var vmType = typeof(TViewModel);
        if (!_mappings.TryGetValue(vmType, out var windowType))
        {
            throw new InvalidOperationException($"No dialog window mapping registered for ViewModel type {vmType.Name}.");
        }

        // resolve window from DI
        var window = (Window)_serviceProvider.GetRequiredService(windowType);

        
        if (System.Windows.Application.Current?.MainWindow != null && window != System.Windows.Application.Current.MainWindow)
        {
            window.Owner = System.Windows.Application.Current.MainWindow;
        }

        TViewModel viewModel;

        if (window.DataContext is TViewModel existingVm)
        {
            viewModel = existingVm;
        }
        else
        {
            viewModel = _serviceProvider.GetRequiredService<TViewModel>();
            window.DataContext = viewModel;
        }

        return (window, viewModel);
    }
}
