using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Bakery.WPF.Authorization;

namespace Bakery.WPF.ViewModels;

public sealed partial class NavigationItemViewModel : ObservableObject
{
    private readonly Action? _navigate;
    private Func<bool> _canNavigate = () => true;

    public NavigationItemViewModel(string title, string iconKind, Type targetType, Action navigate, int groupId = 1)
    {
        Title = title;
        IconKind = iconKind;
        TargetType = targetType;
        _navigate = navigate;
        GroupId = groupId;
        PermissionKeys = NavigationAuthorizationPolicy.GetRequiredPermissions(targetType);
        SubItems = [];
    }

    public NavigationItemViewModel(string title, string iconKind, IEnumerable<NavigationItemViewModel> subItems, int groupId = 1)
    {
        Title = title;
        IconKind = iconKind;
        TargetType = null;
        _navigate = null;
        GroupId = groupId;
        PermissionKeys = subItems.SelectMany(s => s.PermissionKeys).Distinct().ToArray();
        SubItems = new ObservableCollection<NavigationItemViewModel>(subItems);
    }

    public string Title { get; }
    public string IconKind { get; }
    public Type? TargetType { get; }
    public int GroupId { get; }
    public IReadOnlyCollection<string> PermissionKeys { get; }
    public ObservableCollection<NavigationItemViewModel> SubItems { get; }
    public bool HasSubItems => SubItems.Count > 0;

    [ObservableProperty]
    private Thickness groupMargin = new(0);

    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    private bool isExpanded;

    [ObservableProperty]
    private bool isSubItemSelected;

    [RelayCommand(CanExecute = nameof(CanNavigate))]
    public void Navigate()
    {
        if (HasSubItems)
        {
            ToggleExpand();
        }
        else
        {
            _navigate?.Invoke();
        }
    }

    [RelayCommand]
    public void ToggleExpand()
    {
        if (HasSubItems)
        {
            IsExpanded = !IsExpanded;
        }
    }

    private bool CanNavigate() => _canNavigate();

    public void ConfigureAuthorization(Func<bool> canNavigate)
    {
        _canNavigate = canNavigate;
        NavigateCommand.NotifyCanExecuteChanged();
        foreach (var sub in SubItems)
        {
            sub.ConfigureAuthorization(canNavigate);
        }
    }
}
