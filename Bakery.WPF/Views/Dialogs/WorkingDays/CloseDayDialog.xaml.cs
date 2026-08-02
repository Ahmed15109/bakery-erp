using System;
using System.Windows;
using Bakery.WPF.ViewModels;

namespace Bakery.WPF;

public partial class CloseDayDialog
{
    public CloseDayDialog(CloseDayDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // Restrain maximum dialog height to 90% of screen work area so it remains accessible on lower screen resolutions
        MaxHeight = SystemParameters.WorkArea.Height * 0.90;

        viewModel.RequestClose += (_, result) =>
        {
            DialogResult = result;
            Close();
        };
    }
}
