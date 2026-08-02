using System.Windows;
using Bakery.WPF.ViewModels;

namespace Bakery.WPF.Views;

public partial class SafeMismatchDialog : Window
{
    public SafeMismatchDialog()
    {
        InitializeComponent();
        
        DataContextChanged += (s, e) =>
        {
            if (e.NewValue is SafeMismatchDialogViewModel vm)
            {
                vm.RequestClose += (sender, result) =>
                {
                    DialogResult = result;
                    Close();
                };
            }
        };
    }
}
