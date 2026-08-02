using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Bakery.Application.DTOs.Accounting;
using Bakery.Shared.Helpers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bakery.WPF.ViewModels;

public sealed partial class SafeSelectionDialogViewModel : ViewModelBase
{
    public SafeSelectionDialogViewModel()
    {
        Title = "اختر الخزنة";
    }

    [ObservableProperty]
    private SafeDto? selectedSafe;

    [ObservableProperty]
    private bool? dialogResult;

    public ObservableCollection<SafeDto> Safes { get; } = [];

    public void Initialize(IReadOnlyList<SafeDto> safes, SafeDto? currentSafe = null)
    {
        Safes.Clear();
        foreach (var safe in safes)
        {
            Safes.Add(safe);
        }

        if (currentSafe != null)
        {
            SelectedSafe = Safes.FirstOrDefault(s => s.Id == currentSafe.Id);
        }
        else
        {
            SelectedSafe = Safes.FirstOrDefault();
        }
    }

    [RelayCommand]
    private void Confirm()
    {
        if (SelectedSafe == null)
        {
            return;
        }

        DialogResult = true;
    }
}
