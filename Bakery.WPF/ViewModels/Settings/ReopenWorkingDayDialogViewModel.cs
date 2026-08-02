using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Bakery.WPF.ViewModels;

public sealed partial class ReopenWorkingDayDialogViewModel : ViewModelBase
{
    [ObservableProperty] private int workingDayId;
    [ObservableProperty] private DateOnly businessDate;
    [ObservableProperty] private string reason = string.Empty;

    public event EventHandler<bool>? RequestClose;

    public string BusinessDateText => BusinessDate.ToString("dd/MM/yyyy");
    public bool CanConfirm => !string.IsNullOrWhiteSpace(Reason) && ContainsArabicLetter(Reason);

    public void Initialize(int dayId, DateOnly date)
    {
        WorkingDayId = dayId;
        BusinessDate = date;
        Reason = string.Empty;
        OnPropertyChanged(nameof(BusinessDateText));
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    partial void OnReasonChanged(string value)
    {
        OnPropertyChanged(nameof(CanConfirm));
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm() => RequestClose?.Invoke(this, true);

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(this, false);

    private static bool ContainsArabicLetter(string value)
        => value.Any(character =>
            character is >= '\u0600' and <= '\u06FF' or
                         >= '\u0750' and <= '\u077F' or
                         >= '\u08A0' and <= '\u08FF');
}
