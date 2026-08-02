using CommunityToolkit.Mvvm.ComponentModel;

namespace Bakery.WPF.ViewModels;

public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty]
    private bool isBusy;

    partial void OnIsBusyChanged(bool value) => OnBusyStateChanged(value);

    protected virtual void OnBusyStateChanged(bool value)
    {
    }

    [ObservableProperty]
    private string title = string.Empty;
}
