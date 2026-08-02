using Bakery.Application.Interfaces;
using Bakery.Application.Security;
using Bakery.Shared.Helpers;
using Bakery.WPF.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace Bakery.WPF.ViewModels;

public sealed partial class ReportsViewModel : ViewModelBase
{
    private readonly IPermissionService _permissionService;
    private readonly INavigationService _navigationService;

    public ReportsViewModel(IPermissionService permissionService, INavigationService navigationService)
    {
        _permissionService = permissionService;
        _navigationService = navigationService;
        Title = Loc.Reports;
        ReportCategories = [];
        LoadCategories();
    }

    public ObservableCollection<ReportCategory> ReportCategories { get; }

    private void LoadCategories()
    {
        ReportCategories.Clear();
        
        if (_permissionService.HasPermission(PermissionKeys.ReportsSales))
        {
            ReportCategories.Add(new ReportCategory("المبيعات", "Cart", "#D97706", ReportCategoryType.Sales));
        }
        if (_permissionService.HasPermission(PermissionKeys.ProductionView))
        {
            ReportCategories.Add(new ReportCategory("الإنتاج", "Factory", "#8B5E4C", ReportCategoryType.Production));
        }
        if (_permissionService.HasPermission(PermissionKeys.ReportsInventory))
        {
            ReportCategories.Add(new ReportCategory("المخزون", "PackageVariant", "#6B625D", ReportCategoryType.Inventory));
        }
        if (_permissionService.HasPermission(PermissionKeys.ReportsFinancial))
        {
            ReportCategories.Add(new ReportCategory("الحسابات", "CashMultiple", "#2B211D", ReportCategoryType.Accounts));
        }
    }

    [RelayCommand]
    private void GenerateReport(ReportCategory category)
    {
        var detailsVm = _navigationService.NavigateTo<ReportDetailsViewModel>();
        detailsVm.Initialize(category.CategoryType);
    }
}

public class ReportCategory(string name, string iconKind, string color, ReportCategoryType categoryType)
{
    public string Name { get; } = name;
    public string IconKind { get; } = iconKind;
    public string Color { get; } = color;
    public ReportCategoryType CategoryType { get; } = categoryType;
}

public enum ReportCategoryType
{
    Sales,
    Production,
    Inventory,
    Accounts
}
