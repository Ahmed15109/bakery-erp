using System.Windows;
using System.Windows.Input;
using Bakery.WPF.ViewModels;

namespace Bakery.WPF;

public partial class ItemFormDialog : Window
{
    public ItemFormDialog(ItemFormDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose += (_, result) => { DialogResult = result; Close(); };
        
        // Ensure ESC key closes the dialog
        PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) viewModel.CancelCommand.Execute(null); };
    }

    private void Overlay_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is ItemFormDialogViewModel vm) vm.CancelCommand.Execute(null);
    }
}
