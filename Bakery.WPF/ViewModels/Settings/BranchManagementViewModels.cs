using Bakery.Application.DTOs;
using Bakery.Application.Interfaces;
using Bakery.Shared.Helpers;
using Bakery.WPF.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Bakery.WPF.ViewModels;

public sealed partial class BranchesViewModel : ViewModelBase
{
    private readonly IBranchService _branchService;
    private readonly IMessageService _messageService;
    private readonly IDialogService _dialogService;
    private readonly IExceptionTranslator _exceptionTranslator;

    public BranchesViewModel(
        IBranchService branchService,
        IMessageService messageService,
        IDialogService dialogService,
        IExceptionTranslator exceptionTranslator)
    {
        _branchService = branchService;
        _messageService = messageService;
        _dialogService = dialogService;
        _exceptionTranslator = exceptionTranslator;
        Title = Loc.BranchesModule;
        Branches = [];
        _ = RefreshAsync();
    }

    public ObservableCollection<BranchDto> Branches { get; }

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private int totalBranches;

    [ObservableProperty]
    private int activeBranches;

    partial void OnSearchTextChanged(string value)
    {
        _ = RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        try
        {
            Branches.Clear();
            var all = await _branchService.GetAllAsync();
            var search = SearchText.Trim();
            
            var filtered = all.Where(b => 
                string.IsNullOrWhiteSpace(search) || 
                b.Code.Contains(search, StringComparison.OrdinalIgnoreCase) || 
                b.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || 
                (b.Notes != null && b.Notes.Contains(search, StringComparison.OrdinalIgnoreCase)));

            foreach (var branch in filtered)
            {
                Branches.Add(branch);
            }

            TotalBranches = all.Count;
            ActiveBranches = all.Count(b => b.IsActive);
        }
        catch (Exception ex)
        {
            _messageService.ShowError(_exceptionTranslator.Translate(ex));
        }
    }

    [RelayCommand]
    private async Task AddBranchAsync()
    {
        try
        {
            var result = await _dialogService.ShowDialogAsync<BranchFormDialogViewModel>(async vm =>
            {
                vm.Initialize(null);
                await Task.CompletedTask;
            });

            if (result.Result == true)
            {
                await _branchService.CreateAsync(result.ViewModel.ToCreateRequest());
                await RefreshAsync();
            }
        }
        catch (Exception ex)
        {
            _messageService.ShowError(_exceptionTranslator.Translate(ex));
        }
    }

    [RelayCommand]
    private async Task EditBranchAsync(BranchDto branch)
    {
        if (branch is null) return;

        try
        {
            var result = await _dialogService.ShowDialogAsync<BranchFormDialogViewModel>(async vm =>
            {
                vm.Initialize(branch);
                await Task.CompletedTask;
            });

            if (result.Result == true)
            {
                await _branchService.UpdateAsync(result.ViewModel.ToUpdateRequest(branch.Id));
                await RefreshAsync();
            }
        }
        catch (Exception ex)
        {
            _messageService.ShowError(_exceptionTranslator.Translate(ex));
        }
    }

    [RelayCommand]
    private async Task ToggleActiveAsync(BranchDto branch)
    {
        if (branch is null) return;

        var targetState = !branch.IsActive;
        var actionText = targetState ? "تفعيل" : "تعطيل";
        if (!_messageService.Confirm($"هل أنت متأكد من {actionText} الفرع '{branch.Name}'؟"))
        {
            return;
        }

        try
        {
            await _branchService.SetActiveAsync(branch.Id, targetState);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _messageService.ShowError(_exceptionTranslator.Translate(ex));
        }
    }

    [RelayCommand]
    private async Task DeleteBranchAsync(BranchDto branch)
    {
        if (branch is null) return;

        try
        {
            if (!await _branchService.CanDeleteAsync(branch.Id))
            {
                _messageService.ShowError("لا يمكن حذف هذا الفرع لوجود بيانات مرتبطة به أو لأنه الفرع الرئيسي/الفرع النشط حالياً.");
                return;
            }

            if (!_messageService.Confirm(string.Format(Loc.ConfirmDeleteBranch, branch.Name)))
            {
                return;
            }

            await _branchService.DeleteAsync(branch.Id);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            _messageService.ShowError(_exceptionTranslator.Translate(ex));
        }
    }
}

public sealed partial class BranchFormDialogViewModel : ObservableObject
{
    public BranchFormDialogViewModel()
    {
    }

    public void Initialize(BranchDto? branch = null)
    {
        IsEditMode = branch is not null;
        DialogTitle = IsEditMode ? Loc.EditBranch : Loc.AddBranch;
        Code = branch?.Code ?? string.Empty;
        Name = branch?.Name ?? string.Empty;
        IsActive = branch?.IsActive ?? true;
        Notes = branch?.Notes ?? string.Empty;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool isEditMode;

    [ObservableProperty]
    private string dialogTitle = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string code = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string name = string.Empty;

    [ObservableProperty]
    private bool isActive = true;

    [ObservableProperty]
    private string? notes;

    [ObservableProperty]
    private bool? dialogResult;

    public bool CanSave =>
        !string.IsNullOrWhiteSpace(Code) &&
        !string.IsNullOrWhiteSpace(Name);

    public CreateBranchRequest ToCreateRequest()
    {
        return new CreateBranchRequest(Code.Trim(), Name.Trim(), Notes?.Trim());
    }

    public UpdateBranchRequest ToUpdateRequest(int id)
    {
        return new UpdateBranchRequest(id, Code.Trim(), Name.Trim(), IsActive, Notes?.Trim());
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        DialogResult = true;
    }
}
