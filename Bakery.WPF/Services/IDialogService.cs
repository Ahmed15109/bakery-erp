using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Bakery.WPF.Services;

public sealed record DialogResult<TViewModel>(bool? Result, TViewModel ViewModel) where TViewModel : ObservableObject;

public interface IDialogService
{
    
    Task<DialogResult<TViewModel>> ShowDialogAsync<TViewModel>(Func<TViewModel, Task>? initialize = null) where TViewModel : ObservableObject;


    DialogResult<TViewModel> ShowDialog<TViewModel>(Action<TViewModel>? initialize = null) where TViewModel : ObservableObject;
}
