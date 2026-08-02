using System.Collections.ObjectModel;
using Bakery.Application.DTOs;
using Bakery.Shared.Helpers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bakery.WPF.ViewModels;

public sealed partial class BranchSelectionDialogViewModel : ViewModelBase
{
    public BranchSelectionDialogViewModel()
    {
        Title = Loc.SelectBranch;
    }

    [ObservableProperty]
    private BranchDto? selectedBranch;

    [ObservableProperty]
    private bool? dialogResult;

    public ObservableCollection<BranchDto> Branches { get; } = [];

    public void Initialize(IReadOnlyList<BranchDto> branches, BranchDto? currentBranch = null)
    {
        Branches.Clear();
        foreach (var branch in branches)
        {
            Branches.Add(branch);
        }

        if (currentBranch != null)
        {
            SelectedBranch = Branches.FirstOrDefault(b => b.Id == currentBranch.Id);
        }
        else
        {
            SelectedBranch = Branches.FirstOrDefault();
        }
    }

    [RelayCommand]
    private void Confirm()
    {
        if (SelectedBranch == null)
        {
            return;
        }

        DialogResult = true;
    }
}
