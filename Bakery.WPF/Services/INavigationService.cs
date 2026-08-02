using CommunityToolkit.Mvvm.ComponentModel;

namespace Bakery.WPF.Services;

public interface INavigationService
{
    ObservableObject? CurrentViewModel { get; }
    bool CanNavigateTo<TViewModel>() where TViewModel : ObservableObject => true;
    TViewModel NavigateTo<TViewModel>() where TViewModel : ObservableObject;
}
